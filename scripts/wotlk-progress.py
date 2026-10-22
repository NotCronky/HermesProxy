#!/usr/bin/env python3
"""Generate the WotLK progress page: a 3.4.3 client on 3.3.5a servers, backend by backend.

Every opcode HermesProxy reads from, or writes to, a 3.3.5a server gets one row, built from two
sources and nothing typed by hand:

    code   the handler that reads it (SMSG) or the method that builds it (CMSG), and the files
           those hand the packet to
    logs   what the proxy saw in sessions of a 3.4.3 client against a 3.3.5a server: on each
           backend, the most recent session that touched the opcode, and whether it went through
           cleanly

A session's backend is the realm it entered (REALMS, or --realm); sessions on any other realm, a
public server for one, are left out. A clean pass only means nothing threw: a field read from the
wrong offset can still come out wrong without an exception.

With a TrinityCore 3.3.5 checkout (--tc-335), a server packet TrinityCore never sends is marked as
such and left out of the work. With a TrinityCore wotlk_classic checkout (--tc-wotlk-classic), a
request the 3.4.3 client sent that nothing answered says whether the native server acts on it.

Usage
-----
    python scripts/wotlk-progress.py --tc-335 <TrinityCore 3.3.5 checkout> --tc-wotlk-classic <TrinityCore wotlk_classic checkout>
    python scripts/wotlk-progress.py --logs HermesProxy/bin/Release/Logs --realm "My Realm=AzerothCore"

The checkouts can also come from HERMES_TC_335_DIR and HERMES_TC_WOTLK_CLASSIC_DIR. Both are
optional: without one, what it feeds is left out.
"""
from __future__ import annotations

import argparse
import glob
import os
import re
import subprocess
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from progress_common import (
    CLIENT_NO_HANDLER,
    CLIENT_RECV,
    CLIENT_SENT,
    CMSG_EXC,
    HP,
    NO_HANDLER,
    NO_MODERN_MAP,
    RECV,
    REPO,
    SENT,
    SMSG_EXC,
    SOURCES_MARK,
    Seen,
    client_opcode_names,
    cmsg_packet_types,
    content_hash,
    deliberately_unhandled,
    enum_names,
    feature,
    method_bodies,
    native_handlers,
    packet_sites,
    previous_page,
    read_capture,
    send_segment,
    server_sent,
    session_time,
    sources_at_page_commit,
    touched_files,
)

MODERN_BUILD = 'V3_4_3_54261'
LEGACY_BUILD = 'V3_3_5a_12340'
CLIENT_DOCS = '3.4.3.54261'

# The realm a session entered -> its backend. Each core's stock realm name, and the CMaNGOS realm
# of the test server this page was first made from, which was renamed.
REALMS = {
    'Trinity': 'TrinityCore',
    'AzerothCore': 'AzerothCore',
    'MaNGOS': 'CMaNGOS',
    'Wrath of the Lich King Realm': 'CMaNGOS',
}
# Column order: TrinityCore 3.3.5 is the primary backend, AzerothCore the secondary.
BACKENDS = ['TrinityCore', 'AzerothCore', 'CMaNGOS']

REALM_ADDED = re.compile(r'Added realm "([^"]*)"')
NO_LEGACY_MAP = re.compile(r'No ' + LEGACY_BUILD + r' opcode mapping for ([A-Z0-9_]+)')

# Read by hand: the writer is called, never with what it takes to send this one.
NEVER_SENT_335: dict[str, str] = {}


@dataclass
class Evidence:
    sessions: dict[str, list[str]] = field(default_factory=lambda: defaultdict(list))
    smsg: dict[str, dict[str, Seen]] = field(default_factory=lambda: defaultdict(dict))  # backend -> opcode
    cmsg: dict[str, dict[str, Seen]] = field(default_factory=lambda: defaultdict(dict))
    client_errors: dict[str, Seen] = field(default_factory=dict)  # 3.4.3 client side, any backend
    unanswered: dict[str, Seen] = field(default_factory=dict)
    other_realms: dict[str, int] = field(default_factory=lambda: defaultdict(int))


def read_logs(dirs: list[Path], names: dict[int, list[str]], realms: dict[str, str]) -> Evidence:
    ev = Evidence()
    client_names = client_opcode_names(CLIENT_DOCS)
    files = sorted({f for d in dirs for f in d.glob('hermes-*.log')}, key=lambda p: p.name)
    for f in files:
        with f.open(encoding='utf-8', errors='ignore') as fh:
            head = ''.join(next(fh, '') for _ in range(40))
            if (f'Modern (Client) Build: "{MODERN_BUILD}"' not in head
                    or f'Legacy (Server) Build: "{LEGACY_BUILD}"' not in head):
                continue
            session = f.stem.removeprefix('hermes-')
            backend = None
            per_smsg: dict[str, Seen] = {}
            per_cmsg: dict[str, Seen] = {}
            per_client: dict[str, Seen] = {}
            per_unanswered: dict[str, Seen] = {}
            client_ok: set[str] = set()
            fh.seek(0)
            for line in fh:
                if backend is None and (m := REALM_ADDED.search(line)):
                    backend = realms.get(m.group(1), '')
                    if not backend:
                        ev.other_realms[m.group(1)] += 1
                        break
                elif (m := RECV.search(line)):
                    per_smsg.setdefault(m.group(1), Seen(session)).count += 1
                    client_ok.add(m.group(1))
                elif (m := SENT.search(line)):
                    per_cmsg.setdefault(m.group(1), Seen(session)).count += 1
                elif (m := CMSG_EXC.search(line)):
                    s = per_client.setdefault(m.group(1), Seen(session)); s.errors += 1; s.sample = 'exception'
                elif (m := SMSG_EXC.search(line)):
                    s = per_smsg.setdefault(m.group(1), Seen(session)); s.errors += 1
                elif (m := NO_LEGACY_MAP.search(line)):
                    s = per_cmsg.setdefault(m.group(1), Seen(session)); s.dropped += 1
                elif (m := NO_MODERN_MAP.search(line)):
                    s = per_client.setdefault(m.group(1), Seen(session)); s.dropped += 1; s.sample = 'no 3.4.3 mapping'
                elif (m := NO_HANDLER.search(line)):
                    s = per_smsg.setdefault(m.group(1), Seen(session)); s.unhandled += 1
                elif (m := CLIENT_NO_HANDLER.search(line)):
                    name = m.group(1)
                    if name == 'MSG_NULL_ACTION':
                        name = client_names.get(int(m.group(2)), f'0x{int(m.group(2)):x}')
                    per_unanswered.setdefault(name, Seen(session)).count += 1
                elif (m := CLIENT_RECV.search(line)) or (m := CLIENT_SENT.search(line)):
                    client_ok.add(m.group(1))
            # A session that never entered a realm played nothing, and one on another realm is
            # not one of ours.
            if not backend:
                continue
            ev.sessions[backend].append(session)
            for capture in sorted((f.parent.parent / 'PacketsLog').glob(f'legacy_*_{session}_*.pkt')):
                smsg_counts, cmsg_counts = read_capture(capture, names)
                for op, n in smsg_counts.items():
                    per_smsg.setdefault(op, Seen(session)).count = n
                for op, n in cmsg_counts.items():
                    per_cmsg.setdefault(op, Seen(session)).count = n
            # the most recent session on a backend that touched an opcode is the one that counts
            ev.smsg[backend].update(per_smsg)
            ev.cmsg[backend].update(per_cmsg)
            # an error a later session no longer had is gone: that session handled the opcode
            for op in client_ok - per_client.keys():
                ev.client_errors.pop(op, None)
            ev.client_errors.update(per_client)
            ev.unanswered.update(per_unanswered)
    return ev


def latest(by_backend: dict[str, Seen]) -> Seen | None:
    """The most recent of an opcode's sessions, whatever the backend."""
    return max(by_backend.values(), key=lambda s: s.session, default=None)


def failing(seen: Seen | None) -> bool:
    return bool(seen and (seen.errors or seen.dropped))


def status(direction: str, by_backend: dict[str, Seen], changed_since: bool, never_sent: bool) -> str:
    seen = latest(by_backend)
    if never_sent and not any(s.count for s in by_backend.values()):
        return 'not sent'
    if seen and changed_since:
        return 'retest'
    if any(failing(s) for s in by_backend.values()):
        return 'failing'
    if any(s.count for s in by_backend.values()):
        return 'seen ok'
    return 'unseen'


ORDER = ['failing', 'retest', 'unseen', 'seen ok', 'not sent']
ICON = {'failing': '❌', 'retest': '🔁', 'unseen': '·', 'seen ok': '✅', 'not sent': '∅'}
# The statuses a play session still has to clear, in the order shown.
WORK = ['failing', 'unseen', 'retest']


def cell(seen: Seen | None) -> str:
    """One backend's word on an opcode: how its latest session there went, and when."""
    if not seen or not (seen.count or seen.errors or seen.dropped):
        return ''
    day = f'{seen.session[4:6]}-{seen.session[6:8]}'
    return f'{ICON["failing"] if failing(seen) else ICON["seen ok"]} {day}'


def work_section(graded: list, ev: Evidence) -> list[str]:
    """Per feature: how many of its opcodes each backend has passed, and what is left to play."""
    per: dict[str, dict[str, set[str]]] = defaultdict(lambda: defaultdict(set))
    total: dict[str, set[str]] = defaultdict(set)
    ok: dict[str, dict[str, set[str]]] = defaultdict(lambda: defaultdict(set))
    for st, d, op, by_backend, where in graded:
        if st == 'not sent':
            continue
        name = feature(where, op)
        total[name].add(op)
        for backend, seen in by_backend.items():
            if seen.count and not failing(seen):
                ok[name][backend].add(op)
        if st in WORK:
            per[name][st].add(op)

    md = ['## What\'s left', '',
          'Per feature: how many of its opcodes each backend\'s latest session passed cleanly, and what '
          f'still asks for a session. {ICON["retest"]} goes by file, so one edit marks every opcode the file '
          'handles: it is counted, not named. A server packet TrinityCore 3.3.5 never sends is not counted.', '',
          '| Feature | Opcodes | ' + ' | '.join(BACKENDS) + f' | {ICON["failing"]} | {ICON["unseen"]} '
          f'| {ICON["retest"]} | To play |',
          '|---|---:|' + '---:|' * len(BACKENDS) + '---:|---:|---:|---|']

    def weight(name: str) -> tuple:
        by = per.get(name, {})
        return (-len(by.get('failing', ())), -len(by.get('unseen', ())), -len(by.get('retest', ())), name)

    for name in sorted(total, key=weight):
        by = per.get(name, {})
        cells = [str(len(ok[name][b])) if ok[name][b] else '' for b in BACKENDS]
        counts = [str(len(by[k])) if by.get(k) else '' for k in WORK]
        named = '<br>'.join(f'{ICON[k]} ' + ', '.join(f'`{op}`' for op in sorted(by[k]))
                            for k in ('failing', 'unseen') if by.get(k))
        md.append(f'| {name} | {len(total[name])} | ' + ' | '.join(cells) + ' | ' + ' | '.join(counts) + f' | {named} |')
    return md + ['']


def code_since(op: str, session: str) -> str:
    """The day of the last commit that added or removed a mention of the opcode, if after the session."""
    out = subprocess.run(['git', '-C', str(REPO), 'log', '-1', '--format=%ct', f'-S{op}', '--', 'HermesProxy'],
                         capture_output=True, text=True).stdout.strip()
    if not out or int(out) <= session_time(session):
        return ''
    return datetime.fromtimestamp(int(out)).strftime('%Y-%m-%d')


def client_section(ev: Evidence, tc: Path | None) -> list[str]:
    """The 3.4.3 client's side: readers that threw, and requests nothing answered."""
    md = ['## 3.4.3 client side', '']
    # A later session clears an error only when it sends the opcode, and the send of a modern
    # opcode is not always logged: an opcode the 3.4.3 table has by now has its mapping.
    modern = enum_names(HP / 'World' / 'Enums' / MODERN_BUILD / 'Opcode.cs')
    errors = {op: e for op, e in ev.client_errors.items()
              if not (e.sample == 'no 3.4.3 mapping' and op in modern)}
    if errors:
        md += ['### Errors', '',
               'A CMSG reader that threw, or a translated SMSG with no 3.4.3 opcode, in the most recent '
               'session that had it; an error a later session no longer had is left out, and so is a '
               'missing opcode the 3.4.3 table has by now. "Code since" is the day of the last commit '
               'that changed the opcode\'s mentions, when it is later than the session: retest those.', '',
               '| Opcode | Problem | Count | Session | Code since |', '|---|---|---:|---|---|']
        for op, e in sorted(errors.items()):
            md.append(f'| `{op}` | {e.sample} | {e.errors + e.dropped} | {e.session} | {code_since(op, e.session)} |')
        md.append('')
    else:
        md += ['No reader threw and no translated packet lacked a 3.4.3 opcode.', '']

    handled = set(cmsg_packet_types())
    acts = native_handlers(tc)
    on_purpose = deliberately_unhandled()
    left_alone = sorted(n for n in ev.unanswered if n in on_purpose and n not in handled)
    rows = [(n, s) for n, s in ev.unanswered.items() if n not in handled and n not in on_purpose]
    if rows:
        def rank(row):
            name, seen = row
            return (0 if acts.get(name) else 1 if name not in acts else 2, -seen.count, name)

        md += ['### Requests nothing answered', '',
               'What the 3.4.3 client sent and no handler took, most recent session per opcode; a request '
               'that has a handler by now is left out. The client holds some windows until a request is '
               'answered, so these can hang a feature, not just lack one. "TrinityCore" says whether the '
               'native 3.4.3 server acts on the request (wotlk_classic `Opcodes.cpp`): one it ignores too is '
               'not waited for. Those it acts on are first.', '',
               '| Opcode | TrinityCore wotlk_classic | Count | Session |', '|---|---|---:|---|']
        for name, seen in sorted(rows, key=rank):
            says = '?' if name not in acts else ('acts on it' if acts[name] else 'ignores it')
            md.append(f'| `{name}` | {says} | {seen.count} | {seen.session} |')
        md.append('')
    if left_alone:
        md += [f'{len(left_alone)} more that the client sent are left alone on purpose, each with its reason in '
               '`World/Logging/DeliberatelyUnhandledOpcodes.cs`: '
               + ', '.join(f'`{n}`' for n in left_alone) + '.', '']
    return md


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--tc-335', type=Path, default=os.environ.get('HERMES_TC_335_DIR'),
                    help='TrinityCore 3.3.5 git checkout, for the server packets it never sends')
    ap.add_argument('--tc-wotlk-classic', type=Path, default=os.environ.get('HERMES_TC_WOTLK_CLASSIC_DIR'),
                    help='TrinityCore wotlk_classic checkout, for the requests nothing answered')
    ap.add_argument('--realm', action='append', default=[], metavar='NAME=BACKEND',
                    help='a realm name and the backend it runs, in addition to REALMS')
    ap.add_argument('--logs', type=Path, action='append')
    ap.add_argument('--out', type=Path, default=REPO / 'docs' / 'wotlk-progress.md')
    args = ap.parse_args()
    logs = args.logs or [p for p in (HP / 'bin' / 'Release' / 'Logs', HP / 'bin' / 'Debug' / 'Logs') if p.is_dir()]
    realms = dict(REALMS)
    for pair in args.realm:
        name, _, backend = pair.partition('=')
        realms[name] = backend
        if backend not in BACKENDS:
            BACKENDS.append(backend)

    v335 = enum_names(HP / 'World' / 'Enums' / LEGACY_BUILD / 'Opcode.cs')
    sources = glob.glob(str(HP / 'World' / '**' / '*.cs'), recursive=True)
    methods = method_bodies(sources)
    names: dict[int, list[str]] = defaultdict(list)
    for name, value in v335.items():
        names[value].append(name)
    ev = read_logs(logs, names, realms)

    smsg_handlers: dict[str, list[tuple[str, str]]] = defaultdict(list)
    cmsg_sites: dict[str, list[tuple[str, str]]] = defaultdict(list)
    forwarded: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for name, defs in methods.items():
        for rel, body in defs:
            for m in re.finditer(r'HandlesSmsg\(Opcode\.([A-Z0-9_]+)', body):
                if '/Client/' in rel:
                    smsg_handlers[m.group(1)].append((rel, body))
            for op in set().union(*(ops for _, ops in packet_sites(body))):
                cmsg_sites[op].append((rel, body))
            if 'new WorldPacket(opcode)' in body:
                for m in re.finditer(r'HandlesCmsg\(Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)', body):
                    forwarded[m.group(1)].append((rel, body))

    # A player's movement goes to the server under its legacy MSG_MOVE_* name, built from a variable
    # opcode that no line names; the movement table of the 4.3.4 writer lists the same set.
    sequences_file = HP / 'World' / 'Objects' / 'LegacyMovementSequencesCata.cs'
    movement = set(re.findall(r'\[Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)\]', sequences_file.read_text(encoding='utf-8-sig'))) \
        if sequences_file.exists() else set()
    movement_writer = os.path.relpath(HP / 'World' / 'Server' / 'Systems' / 'MovementSystem.cs', REPO).replace(os.sep, '/')

    def evidence(direction: str, op: str) -> dict[str, Seen]:
        table = ev.smsg if direction == 'S' else ev.cmsg
        return {b: table[b][op] for b in BACKENDS if op in table.get(b, {})}

    rows = []   # (dir, opcode, seen per backend, where, files)
    for op, defs in smsg_handlers.items():
        if op not in v335:
            continue
        files = set().union(*(touched_files(r, b, methods) for r, b in defs))
        rows.append(('S', op, evidence('S', op), defs[0][0], files))
    sent_ops = {op for table in ev.cmsg.values() for op in table if op.startswith(('CMSG', 'MSG'))}
    for op in set(cmsg_sites) | set(forwarded) | sent_ops | movement:
        if op not in v335:
            continue
        defs = cmsg_sites.get(op, []) or forwarded.get(op, [])
        where = defs[0][0] if defs else (movement_writer if op in movement else '(variable opcode)')
        files = set().union(*(touched_files(r, send_segment(b, op), methods) for r, b in defs)) if defs else set()
        if op in movement and not defs:
            files = {movement_writer}
        rows.append(('C', op, evidence('C', op), where, files))

    all_files = sorted(set().union(*(r[4] for r in rows)))
    current = {f: content_hash((REPO / f).read_bytes()) for f in all_files}
    previous_rows, previous_sources = previous_page(args.out, ICON, skip=len(BACKENDS))
    if previous_sources is None:
        previous_sources = sources_at_page_commit(args.out, all_files)

    # As on the Cataclysm page: a file's time counts only when its content differs from what the
    # page was last generated from; a checkout or a rebase rewrites files it does not change.
    def changed_since(key: tuple[str, str], seen: Seen | None, files: set[str]) -> bool:
        if not seen or not files:
            return False
        changed = [f for f in files if current[f] != previous_sources.get(f)]
        if changed:
            return max((REPO / f).stat().st_mtime for f in changed) > session_time(seen.session)
        return previous_rows.get(key) == ('retest', seen.session)

    sent = server_sent(Path(args.tc_335) if args.tc_335 else None, NEVER_SENT_335)

    def never_sent(d: str, op: str) -> bool:
        return d == 'S' and sent is not None and v335[op] not in sent

    graded = [(status(d, by, changed_since((d, op), latest(by), files), never_sent(d, op)), d, op, by, where)
              for d, op, by, where, files in rows]
    graded.sort(key=lambda r: (ORDER.index(r[0]), r[1], r[2]))
    counts = defaultdict(int)
    for g in graded:
        counts[g[0]] += 1

    backends_line = ', '.join(f'{b} {len(ev.sessions.get(b, []))}'
                              + (f' (latest {ev.sessions[b][-1]})' if ev.sessions.get(b) else '') for b in BACKENDS)
    others = sum(ev.other_realms.values())
    md = ['# WotLK — progress',
          '',
          '<!-- Generated by scripts/wotlk-progress.py. Do not edit by hand; re-run it. -->',
          '',
          f'A 3.4.3 client on 3.3.5a servers: {len(graded)} opcodes HermesProxy reads from or writes to the '
          f'server. Sessions per backend: {backends_line}.'
          + (f' {others} session(s) on other realms are left out.' if others else ''),
          '',
          'A backend\'s column shows how the latest session there that touched the opcode went, and its day: '
          f'{ICON["seen ok"]} cleanly, {ICON["failing"]} with an exception or a dropped packet.',
          '',
          '| Status | Count | Meaning |',
          '|---|---:|---|',
          f'| {ICON["failing"]} failing | {counts["failing"]} | a backend\'s latest session that touched it threw or dropped it |',
          f'| {ICON["retest"]} retest | {counts["retest"]} | its file changed after the latest session that exercised it |',
          f'| {ICON["unseen"]} unseen | {counts["unseen"]} | no session on any backend exercised it yet |',
          f'| {ICON["seen ok"]} seen ok | {counts["seen ok"]} | the latest session on each backend that touched it passed it cleanly |',
          f'| {ICON["not sent"]} not sent | {counts["not sent"] if sent is not None else "?"} | a server packet '
          'TrinityCore 3.3.5 never sends, and no session saw' + ('' if sent is not None else ' (needs --tc-335)') + ' |',
          '']
    md += work_section(graded, ev)
    md += client_section(ev, Path(args.tc_wotlk_classic) if args.tc_wotlk_classic else None)

    md += ['## Server side (3.3.5a)', '',
           '| | Dir | Opcode | ' + ' | '.join(BACKENDS) + ' | Last session | Seen | Errors | HP file |',
           '|---|---|---|' + '---|' * len(BACKENDS) + '---|---:|---:|---|']
    for st, d, op, by, where in graded:
        seen = latest(by)
        last = seen.session if seen else ''
        n = sum(s.count for s in by.values()) or ''
        err = sum(s.errors + s.dropped for s in by.values()) or ''
        md.append(f'| {ICON[st]} | {d} | `{op}` | ' + ' | '.join(cell(by.get(b)) for b in BACKENDS)
                  + f' | {last} | {n} | {err} | {where} |')
    md += ['', SOURCES_MARK] + [f'{f} {current[f]}' for f in all_files] + ['-->', '']

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text('\n'.join(md), encoding='utf-8')
    print(f'wrote {args.out}: ' + ', '.join(f'{k} {counts[k]}' for k in ORDER)
          + '; sessions ' + ', '.join(f'{b} {len(ev.sessions.get(b, []))}' for b in BACKENDS))


if __name__ == '__main__':
    main()
