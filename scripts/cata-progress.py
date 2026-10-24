#!/usr/bin/env python3
"""Generate the Cataclysm progress page: the 4.4.2 client side and the 4.3.4 legacy side.

Client side: every opcode whose layout the 4.4.2 client reads differently from the 3.4.3 one
(docs/protocol, the clients' own readers), against whether HermesProxy's writer or codec for it
has a 4.4.2 shape yet, and whether 4.4.2 sessions sent it (modern .pkt captures).

Legacy side: every opcode HermesProxy reads from, or writes to, a legacy server gets one row,
built from three sources and nothing typed by hand:

    code   is the opcode in the 4.3.4 table, and does its handler (SMSG) or the method that
           builds it (CMSG) carry a 4.3.4 branch: IsCataLegacy, a *Cata method or codec
    WPP    did the layout change in 4.x: a parser in WowPacketParser's 4.3.4 module, or a
           Cataclysm / V4 branch in the shared parser (optional, see --wpp)
    logs   what the proxy saw in sessions against a 4.3.4 server: the most recent session
           that touched the opcode, and whether it went through cleanly

A clean pass in a log only means nothing threw: a field read from the wrong offset can still
come out wrong without an exception. Rows whose layout changed and that have no 4.3.4 branch
are therefore listed as suspect even when the logs are clean.

With a TrinityCore 4.3.4 checkout (--tc-434), a server packet whose opcode value the server
never sends is marked as such and left out of the work: 4.3.4 relays a player's movement as
SMSG_MOVE_UPDATE, so the 3.3.5a MSG_MOVE_* relays never come; and nothing calls
Player::SendAttackSwingDeadTarget, the one writer of SMSG_ATTACKSWING_DEADTARGET. The checkout is
read through git (the HEAD tree), so a sparse one does.

Usage
-----
    python scripts/cata-progress.py --wpp <WowPacketParser checkout> --tc-cata <TrinityCore cata_classic checkout> \
        --tc-434 <TrinityCore 4.3.4 checkout>
    python scripts/cata-progress.py --wpp <...> --logs HermesProxy/bin/Release/Logs --out docs/cata-progress.md

The checkouts can also come from HERMES_WPP_DIR, HERMES_TC_CATA_DIR and HERMES_TC_434_DIR. All are
optional: without one, the column it feeds reads "?".
"""
from __future__ import annotations

import argparse
import glob
import os
import re
from collections import defaultdict
from difflib import SequenceMatcher
from pathlib import Path

from progress_common import (
    CLIENT_NO_HANDLER,
    CLIENT_RECV,
    CLIENT_SENT,
    CMSG_EXC,
    DOCS,
    HP,
    LogEvidence,
    NO_HANDLER,
    NO_MODERN_MAP,
    RECV,
    REPO,
    SENT,
    SMSG_EXC,
    SOURCES_MARK,
    Seen,
    _match,
    client_opcode_names,
    cmsg_packet_types,
    content_hash,
    deliberately_unhandled,
    doc_layouts,
    doc_links,
    enum_names,
    feature,
    method_bodies,
    native_handlers,
    packet_site,
    packet_sites,
    previous_page,
    read_capture,
    send_segment,
    server_sent,
    session_time,
    sources_at_page_commit,
    touched_files,
)
LEGACY_BUILD = 'V4_3_4_15595'

# What marks code as 4.3.4-aware: the legacy-expansion gate, a *Cata reader or writer, a
# masked GUID, or a version check against a 4.x build (code that already tracked a 4.x change).
CATA_MARKERS = re.compile(r'IsCataLegacy|LegacyHasItemQuery|\w+Cata\s*\(|MaskedGuid\.|ClientVersionBuild\.V4_\d')


def ported(body: str, methods: dict[str, list[tuple[str, str]]], depth: int = 2) -> bool:
    """A 4.3.4 branch in the code that reads or writes this packet: the body itself, or a
    method it hands the packet to. Callees that never see the packet are not followed, or a
    handler would count as ported for an unrelated packet it happens to send."""
    if CATA_MARKERS.search(body):
        return True
    if depth == 0:
        return False
    for callee in set(re.findall(r'\b([A-Z]\w+)\s*\(\s*(?:ref\s+)?packet\b', body)):
        for _, callee_body in methods.get(callee, []):
            if ported(callee_body, methods, depth - 1):
                return True
    return False


def cata_returns_before(body: str, opcode: str) -> bool:
    """The method is done with a 4.3.4 server before it builds this opcode's packet: an
    `if (IsCataLegacy) { ... return; }` above it."""
    site = packet_site(body, opcode)
    if site == -1:
        return False
    for m in re.finditer(r'\bif\s*\(', body[:site]):
        close = _match(body, m.end() - 1, '(', ')')
        if not re.fullmatch(r'\s*(?:[\w.]+\.)?IsCataLegacy\s*', body[m.end():close]):
            continue
        brace = re.compile(r'\s*').match(body, close + 1).end()
        if brace < site and body[brace] == '{':
            end = _match(body, brace, '{', '}')
            if end < site and re.search(r'\breturn\b', body[brace:end]):
                return True
    return False


# Read by hand: every sender is behind a gate that is false on a 4.3.4 server.
RETIRED_ON_434 = {
    'CMSG_ITEM_QUERY_SINGLE': 'LegacyHasItemQuery: a 4.3.4 server sends item records as hotfixes',
}


def wpp_signals(wpp: Path | None) -> dict[str, str]:
    if wpp is None:
        return {}
    signals: dict[str, str] = {}

    def scan(paths, module):
        for f in paths:
            text = Path(f).read_text(encoding='utf-8-sig', errors='ignore')
            for m in re.finditer(r'\[Parser\(Opcode\.([A-Z0-9_]+)', text):
                op = m.group(1)
                body_start = text.find('{', m.end())
                nxt = text.find('[Parser(Opcode.', body_start)
                chunk = text[m.start(): nxt if nxt != -1 else len(text)]
                if module:
                    signals[op] = 'changed'
                elif signals.get(op) != 'changed':
                    signals[op] = 'changed' if re.search(r'V4_\d_\d|ClientType\.Cataclysm', chunk) else 'same'

    scan(glob.glob(str(wpp / 'WowPacketParser' / 'Parsing' / 'Parsers' / '*.cs')), module=False)
    scan(glob.glob(str(wpp / 'WowPacketParserModule.V4_3_4_15595' / 'Parsers' / '*.cs')), module=True)
    return signals


NO_LEGACY_MAP = re.compile(r'No ' + LEGACY_BUILD + r' opcode mapping for ([A-Z0-9_]+)')
def read_logs(dirs: list[Path], names: dict[int, list[str]]) -> LogEvidence:
    ev = LogEvidence()
    client_names = client_opcode_names('4.4.2.60895', 'V4_4_2_60895')
    files = sorted({f for d in dirs for f in d.glob('hermes-*.log')}, key=lambda p: p.name)
    for f in files:
        with f.open(encoding='utf-8', errors='ignore') as fh:
            head = ''.join(next(fh, '') for _ in range(40))
            if f'Legacy (Server) Build: "{LEGACY_BUILD}"' not in head:
                continue
            session = f.stem.removeprefix('hermes-')
            ev.sessions.append(session)
            per_smsg: dict[str, Seen] = {}
            per_cmsg: dict[str, Seen] = {}
            per_client: dict[str, Seen] = {}
            per_unanswered: dict[str, Seen] = {}
            client_ok: set[str] = set()
            fh.seek(0)
            for line in fh:
                if (m := RECV.search(line)):
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
                    s = per_client.setdefault(m.group(1), Seen(session)); s.dropped += 1; s.sample = 'no 4.4.2 mapping'
                elif (m := NO_HANDLER.search(line)):
                    s = per_smsg.setdefault(m.group(1), Seen(session)); s.unhandled += 1
                elif (m := CLIENT_NO_HANDLER.search(line)):
                    name = m.group(1)
                    if name == 'MSG_NULL_ACTION':
                        name = client_names.get(int(m.group(2)), f'0x{int(m.group(2)):x}')
                    per_unanswered.setdefault(name, Seen(session)).count += 1
                elif (m := CLIENT_RECV.search(line)) or (m := CLIENT_SENT.search(line)):
                    client_ok.add(m.group(1))
            for capture in sorted((f.parent.parent / 'PacketsLog').glob(f'legacy_*_{session}_*.pkt')):
                smsg_counts, cmsg_counts = read_capture(capture, names)
                for op, n in smsg_counts.items():
                    per_smsg.setdefault(op, Seen(session)).count = n
                for op, n in cmsg_counts.items():
                    per_cmsg.setdefault(op, Seen(session)).count = n
            # the most recent session that touched an opcode is the one that counts
            ev.smsg.update(per_smsg)
            ev.cmsg.update(per_cmsg)
            # an error a later session no longer had is gone: that session handled the opcode
            for op in client_ok - per_client.keys():
                ev.client_errors.pop(op, None)
            ev.client_errors.update(per_client)
            ev.unanswered.update(per_unanswered)
    return ev


def class_has_442_variant(type_name: str, sources: list[str], cache: dict[str, str]) -> bool:
    """A ServerPacketLayouts entry for 4.4.2 inside the class, or a 4.4.2 [PacketCodec]."""
    codec = re.compile(r'\[PacketCodec\(typeof\(' + re.escape(type_name) + r'\)[^\]]*AddedIn\s*=\s*ClientVersionBuild\.V4_4_2')
    decl = re.compile(r'class ' + re.escape(type_name) + r'\b[^{]*\{')
    for f in sources:
        text = cache.get(f)
        if text is None:
            text = cache[f] = Path(f).read_text(encoding='utf-8-sig', errors='ignore')
        if codec.search(text):
            return True
        m = decl.search(text)
        if m:
            end = _match(text, m.end() - 1, '{', '}')
            if 'V4_4_2_60895' in text[m.start():end]:
                return True
    return False


def modern_counts(log_dirs: list[Path]) -> dict[str, int]:
    """Opcode counts from every 4.4.2 client capture, whatever the backend."""
    table = enum_names(HP / 'World' / 'Enums' / 'V4_4_2_60895' / 'Opcode.cs')
    names: dict[int, list[str]] = defaultdict(list)
    for name, value in table.items():
        if value:
            names[value].append(name)
    counts: dict[str, int] = defaultdict(int)
    for d in log_dirs:
        for capture in (d.parent / 'PacketsLog').glob('modern_60895_*.pkt'):
            # PKT 3.1: a 66-byte header plus its optional block, then records of
            # (direction, connection, tick, extra length, data length), the extra, and the data,
            # which starts with the 4-byte opcode.
            data = capture.read_bytes()
            off = 66 + int.from_bytes(data[62:66], 'little')
            while off + 20 <= len(data):
                extra = int.from_bytes(data[off + 12:off + 16], 'little')
                size = int.from_bytes(data[off + 16:off + 20], 'little')
                body = data[off + 20 + extra:off + 20 + extra + size]
                off += 20 + extra + size
                if len(body) >= 4:
                    for name in names.get(int.from_bytes(body[:4], 'little'), []):
                        counts[name] += 1
    return counts


def normalise(layout: str) -> str:
    """Drop notation that does not change the bytes: grouping braces, and the optional mark on a
    string's bytes (the length already says whether there are any)."""
    layout = layout.replace('{', ' ').replace('}', ' ')
    layout = re.sub(r'opt\[\s*bytes\s*\]', 'bytes', layout)
    # an alternative with nothing in it has no bytes
    layout = re.sub(r'alt\[\s*\]', ' ', layout)
    # u32*3 and u32 u32 u32 are the same bytes
    layout = re.sub(r'\b(\w+)\*(\d+)\b', lambda m: ' '.join([m.group(1)] * int(m.group(2))), layout)
    return ' '.join(layout.split())


# Differences someone checked and found harmless, with the reason. The only hand-kept part of
# the client side: a docs difference stays a todo until it has a 4.4.2 shape in HermesProxy or
# a line here.
# A shared change is matched by what it turns into what, whatever the context around it.
REVIEWED_HUNKS = [
    ('loop[ u32 u8 ]', 'loop[ u8 u32 ]',
     'item modifiers swap to (type, value); HermesProxy writes none, and a read takes the same five bytes'),
    ('u8 u8 loop[ u32 u32 u32', 'u8 u8 loop[ u8 u32 u32',
     'SpellCastLogData gains two ints and a byte power type; HermesProxy never sends log data'),
]
REVIEWED_OPCODES = {
    'SMSG_AURA_UPDATE': 'notation: both builds read a 1-bit and a 9-bit field first',
    'SMSG_RESURRECT_REQUEST': 'notation: 11-bit name length and two bits in both; HermesProxy '
                              'matches TrinityCore cata_classic',
    'SMSG_CHAT_SERVER_MESSAGE': 'notation: an 11-bit length in both',
    'SMSG_TRADE_STATUS': 'notation: HermesProxy matches TrinityCore cata_classic bit for bit',
    'SMSG_TRADE_UPDATED': 'notation, plus item modifiers (never written)',
    'SMSG_LFG_TELEPORT_DENIED': 'notation: four bits in one byte either way',
    'SMSG_RESUME_TOKEN': 'notation: two bits in one byte either way',
    'SMSG_SUSPEND_TOKEN': 'notation: two bits in one byte either way',
    'SMSG_PET_SPELLS_MESSAGE': 'same bytes: the 3.4.3 u16 is (CommandState, Flag 0), 4.4.2 reads '
                               'two bytes',
    'SMSG_PARTY_MEMBER_FULL_STATE': 'phase flags widen to u32; HermesProxy sends no phases',
    'SMSG_PARTY_MEMBER_PARTIAL_STATE': 'phase flags widen to u32; HermesProxy sends no phases',
    'SMSG_GM_TICKET_CASE_STATUS': 'two more strings per case; HermesProxy always sends an empty case list',
    'SMSG_AUTH_RESPONSE': 'the queue block gains a field, written by AuthWaitInfo.Shapes; the rest is notation',
    'SMSG_WAIT_QUEUE_UPDATE': 'the queue block gains a field, written by AuthWaitInfo.Shapes',
    'CMSG_PETITION_BUY': 'notation: a 7-bit title length in both; HermesProxy matches TrinityCore cata_classic',
    'CMSG_PARTY_UNINVITE': 'notation: the 3.4.3 codec already reads what TrinityCore cata_classic reads',
    'CMSG_SET_PLAYER_DECLINED_NAMES': 'notation: five 7-bit lengths, then five strings, in both',
    'CMSG_BANKER_ACTIVATE': '4.4.2 appends an int32 interaction type; HermesProxy reads the GUID and '
                            'nothing after it',
    'CMSG_TABARD_VENDOR_ACTIVATE': 'as CMSG_BANKER_ACTIVATE',
    # Identical "Bit fields" lines in both docs: the difference is how the RE wrote the bits.
    'SMSG_CHANNEL_NOTIFY_LEFT': 'notation: bit fields 7 · 1 in both',
    'SMSG_GUILD_EVENT_MOTD': 'notation: bit fields 11 in both',
    'SMSG_GUILD_EVENT_PLAYER_JOINED': 'notation: bit fields 6 in both',
    'SMSG_GENERATE_RANDOM_CHARACTER_NAME_RESULT': 'notation: bit fields 1 · 6 in both',
    'SMSG_ARENA_TEAM_EVENT': 'notation: bit fields 8 · 9 · 9 · 9 in both',
    'SMSG_QUERY_PETITION_RESPONSE': 'notation: bit fields 1 in both',
    'SMSG_LFG_PLAYER_INFO': 'notation: bit fields 1 in both',
    'CMSG_PET_RENAME': 'notation: bit fields 8 · 1 in both',
    'SMSG_GUILD_EVENT_PRESENCE_CHANGE': '4.4.2 reads one bit fewer of the same byte (6 · 1 against '
                                        '6 · 1 · 1); the extra bit is ignored',
}
for _ack in ('COLLISION_HEIGHT', 'KNOCK_BACK', 'FEATHER_FALL', 'FORCE_ROOT', 'FORCE_UNROOT',
             'GRAVITY_DISABLE', 'GRAVITY_ENABLE', 'HOVER', 'SET_CAN_FLY', 'WATER_WALK'):
    REVIEWED_OPCODES[f'CMSG_MOVE_{_ack}_ACK' if _ack != 'COLLISION_HEIGHT' else 'CMSG_MOVE_SET_COLLISION_HEIGHT_ACK'] = \
        'notation: MovementInfo then the ack index in both'
for _speed in ('FLIGHT_BACK', 'FLIGHT', 'PITCH_RATE', 'RUN_BACK', 'RUN', 'SWIM_BACK', 'SWIM',
               'TURN_RATE', 'WALK'):
    _name = f'CMSG_MOVE_FORCE_{_speed}_SPEED_CHANGE_ACK' if 'RATE' not in _speed \
        else f'CMSG_MOVE_FORCE_{_speed}_CHANGE_ACK'
    REVIEWED_OPCODES[_name] = 'notation: MovementInfo, the ack index and the speed in both'


def reviewed(name: str, hunks: list[str]) -> str:
    """Why a docs difference is harmless, or '' while it is not known to be."""
    if name in REVIEWED_OPCODES:
        return REVIEWED_OPCODES[name]
    if hunks and all(hunk_review(h) for h in hunks):
        return '; '.join(dict.fromkeys(hunk_review(h) for h in hunks))
    return ''


def hunk_review(hunk: tuple[str, str]) -> str:
    """Why a change is harmless, from REVIEWED_HUNKS, or '' while it is not known to be."""
    for old, new, reason in REVIEWED_HUNKS:
        if old in hunk[0] and new in hunk[1]:
            return reason
    return ''


def changes(layout: str, new_layout: str) -> list[tuple[str, str]]:
    """What changed between two layouts: each change as the 3.4.3 snippet and the 4.4.2 snippet,
    both with two tokens of context on either side. The same change in several packets is a
    nested structure they share."""
    a, b = normalise(layout).split(), normalise(new_layout).split()
    out: list[tuple[str, str]] = []
    # Changes closer than four tokens are one hunk: a swapped pair reads as one change, not two.
    for group in SequenceMatcher(None, a, b, autojunk=False).get_grouped_opcodes(2):
        hunk = (' '.join(a[group[0][1]:group[-1][2]]), ' '.join(b[group[0][3]:group[-1][4]]))
        if hunk not in out:
            out.append(hunk)
    return out


def show(hunk: tuple[str, str]) -> str:
    return f'`{hunk[0]}` → `{hunk[1]}`'


def tc434_sent(tc: Path | None) -> set[int] | None:
    """The opcode values TrinityCore 4.3.4 writes somewhere (see server_sent)."""
    return server_sent(tc, NEVER_SENT_434)


# Read by hand: the writer is called, never with what it takes to send this one.
NEVER_SENT_434 = {
    'SMSG_SET_FACTION_NOT_VISIBLE': 'ReputationMgr::SendVisible picks it for visible = false, and every call passes true',
    'SMSG_CLEAR_TARGET': 'Spell::EffectForceDeselect builds it in notifierClear, then visits with notifierBreak again',
}


def unanswered_section(unanswered: dict, tc: Path | None) -> list[str]:
    """Requests the 4.4.2 client sent in a session that no handler took. The client holds some
    of its windows until a request is answered (the auction house froze on one), so each is a
    feature that may hang rather than merely be missing."""
    handled = set(cmsg_packet_types())
    tc_acts = native_handlers(tc)
    on_purpose = deliberately_unhandled()
    left_alone = sorted(name for name in unanswered if name in on_purpose and name not in handled)
    rows = [(name, seen) for name, seen in unanswered.items() if name not in handled and name not in on_purpose]
    if not rows:
        return []

    def rank(row):
        name, seen = row
        return (0 if tc_acts.get(name) else 1 if name not in tc_acts else 2, -seen.count, name)

    md = ['### Requests nothing answered', '',
          'What the 4.4.2 client sent in a 4.3.4 session and no handler took, most recent session per '
          'opcode; a request that has a handler by now is left out. The client holds some windows until a '
          'request is answered, so these can hang a feature, not just lack one. "TrinityCore" says '
          'whether the native 4.4.2 server acts on the request (cata_classic `Opcodes.cpp`): one it '
          'ignores too is not waited for. Those it acts on are first.', '',
          '| Opcode | TrinityCore cata_classic | Count | Session |', '|---|---|---:|---|']
    for name, seen in sorted(rows, key=rank):
        tc_says = '?' if name not in tc_acts else ('acts on it' if tc_acts[name] else 'ignores it')
        md.append(f'| `{name}` | {tc_says} | {seen.count} | {seen.session} |')
    md.append('')
    if left_alone:
        md += [f'{len(left_alone)} more that the client sent are left alone on purpose, each with its reason in '
               '`World/Logging/DeliberatelyUnhandledOpcodes.cs`: '
               + ', '.join(f'`{name}`' for name in left_alone) + '.', '']
    return md


def client_layout_section(log_dirs: list[Path], client_errors: dict) -> list[str]:
    old = doc_layouts(DOCS / '3.4.3.54261')
    new = doc_layouts(DOCS / '4.4.2.60895')
    if not old or not new:
        return []
    sources = glob.glob(str(HP / 'World' / '**' / '*.cs'), recursive=True)
    cache: dict[str, str] = {}
    seen = modern_counts(log_dirs)

    rows = []
    unresolved: list[str] = []
    cmsg_types = cmsg_packet_types()
    for name, (layout, hp_type) in old.items():
        if name not in new or not hp_type or hp_type == 'EmptyClientPacket':
            continue
        hp_type = cmsg_types.get(name, hp_type)  # the docs name the type HermesProxy had then
        new_layout = new[name][0]
        if not new_layout or normalise(new_layout) == normalise(layout):
            continue
        if 'struct' in layout or 'struct' in new_layout:
            unresolved.append(name)        # the RE left one side unresolved; nothing to compare
            continue
        has = class_has_442_variant(hp_type, sources, cache)
        rows.append((has, -seen.get(name, 0), name, hp_type, layout, new_layout, changes(layout, new_layout)))
    rows.sort()
    notes = {r[2]: reviewed(r[2], r[6]) for r in rows}
    # Open first, then ruled out, then done; the busiest first within each.
    rows.sort(key=lambda r: (r[0], bool(notes[r[2]]), r[1], r[2]))
    todo = sum(1 for r in rows if not r[0] and not notes[r[2]])

    def icon(has: bool, name: str) -> str:
        return '✅' if has else ('🟰' if notes[name] else '⬜')

    by_hunk: dict[tuple[str, str], list[tuple[bool, str]]] = defaultdict(list)
    for has, _, name, _, _, _, hunks in rows:
        for hunk in hunks:
            by_hunk[hunk].append((has, name))
    shared = sorted(((h, ops) for h, ops in by_hunk.items() if len(ops) > 1),
                    key=lambda x: (-sum(1 for has, _ in x[1] if not has), -len(x[1]), x[0]))
    # A shared change matters while some packet carrying it has neither a 4.4.2 shape nor a review.
    open_shared = [(h, ops) for h, ops in shared
                   if not hunk_review(h) and any(not has and not notes[n] for has, n in ops)]

    md = ['## 4.4.2 client side', '']
    if client_errors:
        md += ['### Errors in 4.3.4 sessions', '',
               'A CMSG reader that threw, or a translated SMSG with no 4.4.2 opcode.', '',
               '| Opcode | Problem | Count | Session |', '|---|---|---:|---|']
        for op, e in sorted(client_errors.items()):
            md.append(f'| `{op}` | {e.sample} | {e.errors + e.dropped} | {e.session} |')
        md.append('')
    md += ['Reading a change: `3.4.3 snippet` → `4.4.2 snippet`, in the docs/protocol layout notation '
           '(u8/u16/u32/u64/f32 fields, `guid` a packed GUID, `bytes` a string, `bits(n)` a bit field, '
           '`opt[ … ]` present only when a flag says so, `loop[ … ]` repeated, `alt[ … ]` one of two '
           'branches; grouping braces dropped). Two tokens of context either side.', '']
    if shared:
        md += ['### Changes several packets share', '',
               'The same change in more than one packet: most likely a nested structure they all write, '
               'so one writer fix covers them. Only a change that writes a non-empty part matters; an '
               'empty loop or an absent optional reads the same.', '']
        if open_shared:
            md += ['| Change | Packets |', '|---|---|']
            for hunk, ops in open_shared:
                md.append(f'| {show(hunk)} | ' + ', '.join(f'{icon(has, n)} `{n}`' for has, n in ops) + ' |')
            md.append('')
        else:
            md += ['None left open.', '']
        done = len(shared) - len(open_shared)
        if done:
            md += [f'{done} more shared change(s) are handled (✅) or ruled out (🟰) in every packet '
                   'that carries them.', '']
    old_links = doc_links(DOCS / '3.4.3.54261')
    new_links = doc_links(DOCS / '4.4.2.60895')
    shaped = [r for r in rows if r[0]]
    ruled = [r for r in rows if not r[0] and notes[r[2]]]
    md += ['### Layouts that changed from 3.4.3', '',
          f'Opcodes HermesProxy writes or reads whose 4.4.2 layout differs from 3.4.3 (docs/protocol, from the '
          f'clients\' own readers): {len(rows)}. {todo} open (⬜, no 4.4.2 shape yet and no review that rules '
          f'them out), {len(shaped)} with a 4.4.2 shape (✅), {len(ruled)} reviewed, no change needed (🟰, the '
          'reason under the change; REVIEWED_* in the script). "Change" is the difference, read as above; '
          '"Layouts" links each build\'s full layout. "Sent" counts the opcode in 4.4.2 client captures, '
          'either backend; the busiest are first.', '']

    def table(part: list) -> list[str]:
        out = ['| | Opcode | HP type | Change | Sent | Layouts |', '|---|---|---|---|---:|---|']
        for has, sent, name, hp_type, _, _, hunks in part:
            shown = '<br>'.join(show(h) for h in hunks[:4]) + (f'<br>… {len(hunks) - 4} more' if len(hunks) > 4 else '')
            if notes[name] and not has:
                shown += f'<br>🟰 {notes[name]}'
            links = ' · '.join(f'[{build}]({link})' for build, link in
                               (('3.4.3', old_links.get(name)), ('4.4.2', new_links.get(name))) if link)
            out.append(f'| {icon(has, name)} | `{name}` | `{hp_type}` | {shown} | {-sent or ""} | {links} |')
        return out + ['']

    if todo:
        md += table([r for r in rows if not r[0] and not notes[r[2]]])
    # GitHub renders markdown inside <details> only with a blank line after the summary.
    for part, title in ((shaped, 'with a 4.4.2 shape (✅)'), (ruled, 'reviewed, no change needed (🟰)')):
        if part:
            md += ['<details>', f'<summary>{len(part)} {title}</summary>', ''] + table(part) + ['</details>', '']
    if unresolved:
        md += [f'Not compared, one side unresolved in the docs ({len(unresolved)}): '
               + ', '.join(f'`{n}`' for n in sorted(unresolved)), '']
    return md


# Legacy opcodes the shared parser calls "changed" whose 4.3.4 layout was read in TrinityCore
# 4.3.4 and is what HermesProxy reads or writes already: the change is elsewhere in the family,
# or trails what HermesProxy takes. (direction, opcode) -> what was read. Shown as "reviewed".
LEGACY_REVIEWED = {
    ('C', 'CMSG_BATTLEMASTER_HELLO'): 'HandleBattlemasterHelloOpcode: the battlemaster, a plain GUID',
    ('C', 'CMSG_MAIL_MARK_AS_READ'): 'HandleMailMarkAsRead: mailbox, mail id; 12 bytes captured',
    ('C', 'CMSG_QUEST_GIVER_COMPLETE_QUEST'): 'QuestGiverCompleteQuest::Read: giver, quest, FromScript',
    ('C', 'CMSG_QUEST_POI_QUERY'): 'QuestPOIQuery::Read: count, quest ids',
    ('C', 'CMSG_TIME_SYNC_RESPONSE'): 'HandleTimeSyncResp: counter, client time',
    ('S', 'SMSG_ATTACKER_STATE_UPDATE'): 'captured swings, with and without a block, parse as 3.3.5a to the last byte',
    ('S', 'SMSG_BUY_FAILED'): 'Player::SendBuyError: vendor, item, reason',
    ('S', 'SMSG_FEATURE_SYSTEM_STATUS'): 'not read: HermesProxy builds its own',
    ('S', 'SMSG_GOSSIP_MESSAGE'): 'captured menus, one with an option and one with a quest, parse as 3.3.5a',
    ('S', 'SMSG_LOOT_MONEY_NOTIFY'): 'LootMoneyNotify::Write: money, sole looter',
    ('S', 'SMSG_PARTY_COMMAND_RESULT'): 'PartyCommandResult::Write: a GUID added after everything read',
    ('S', 'SMSG_QUEST_UPDATE_COMPLETE'): 'quest id alone; 4 bytes captured',
    ('S', 'SMSG_TRAINER_BUY_FAILED'): 'TrainerBuyFailed::Write: trainer, spell, reason',
    ('C', 'CMSG_BEGIN_TRADE'): 'empty, as in 3.3.5a; the trade window opened on both clients in play',
    ('S', 'SMSG_LEARNED_SPELL'): 'Player::LearnSpell: the spell id first, then a uint32 nothing reads',
    ('C', 'CMSG_CLEAR_TRADE_ITEM'): 'ClearTradeItem::Read: the trade slot, one byte',
    ('C', 'MSG_LIST_STABLED_PETS'): 'HandleListStabledPetsOpcode: the stable master, nothing else',
    ('C', 'CMSG_LEARN_PREVIEW_TALENTS_PET'): 'HandleLearnPreviewTalentsPet: pet GUID, count, pairs as in 3.3.5a; '
        "WPP's 4.x change is the tab of CMSG_LEARN_PREVIEW_TALENTS, which shares its parser; a pet learned a talent in play",
    ('C', 'CMSG_QUEST_GIVER_REQUEST_REWARD'): 'QuestGiverRequestReward::Read: the quest giver and the quest, as in 3.3.5a; '
        'a delivery quest was handed in through it',
    ('S', 'MSG_CHANNEL_START'): 'ChannelStart::Write: two flags and what they announce, after everything read',
    ('S', 'MSG_MOVE_TELEPORT_ACK'): 'client to server only on 4.3.4; the teleport itself is MSG_MOVE_TELEPORT, ported',
    ('S', 'SMSG_DUEL_WINNER'): 'Player::DuelComplete: fled byte, loser, winner',
    ('S', 'SMSG_LFG_DISABLED'): 'empty',
    ('S', 'SMSG_LFG_OFFER_CONTINUE'): 'LFGOfferContinue::Write: the slot',
    ('S', 'SMSG_LOOT_START_ROLL'): 'Group::SendLootStartRoll: a player count after everything read',
    ('S', 'SMSG_PLAY_MUSIC'): 'PlayMusic::Write: a source GUID after the sound, which the modern packet has no place for',
    ('S', 'SMSG_RESURRECT_REQUEST'): 'ResurrectRequest::Write: the spell id after everything read',
    ('S', 'SMSG_SPELL_DAMAGE_SHIELD'): 'SpellDamageShield::Write: the absorbed amount after everything read',
    ('C', 'CMSG_GUILD_BANK_QUERY_TAB'): 'GuildBankQueryTab::Read: vault, tab, full update, as in 3.3.5a; a bank opened in play',
    ('C', 'CMSG_GUILD_DECLINE_INVITATION'): 'HandleGuildDeclineOpcode: empty, as in 3.3.5a',
    ('C', 'CMSG_GUILD_GET_ROSTER'): 'HandleGuildRosterOpcode: empty, as in 3.3.5a; the roster opened on both clients in play',
    ('C', 'CMSG_GUILD_LEAVE'): 'HandleGuildLeaveOpcode: empty, as in 3.3.5a; a member left in play',
    ('S', 'SMSG_GUILD_COMMAND_RESULT'): 'GuildCommandResult::Write: command, name, result, as in 3.3.5a; '
                                        '4.3.4 sends it as SMSG_GUILD_COMMAND_RESULT_2, which the same handler takes',
    ('C', 'CMSG_AREA_SPIRIT_HEALER_QUERY'): 'HandleAreaSpiritHealerQueryOpcode: a plain 8-byte GUID, as in 3.3.5a '
                                            '(WowPacketParser has the 4.3.4 client mask it; the server does not read it so)',
    ('C', 'CMSG_AREA_SPIRIT_HEALER_QUEUE'): 'HandleAreaSpiritHealerQueueOpcode: a plain 8-byte GUID, as in 3.3.5a',
    ('C', 'CMSG_QUERY_QUEST_COMPLETION_NPCS'): 'HandleQuestNPCQuery: 24 bits of count, then the quest ids; '
                                               'written so by its own 4.3.4 branch (QuestSystem)',
    ('C', 'CMSG_CALENDAR_COMPLAIN'): 'HandleCalendarComplain: the inviter\'s 8-byte GUID, the event, the invitation; '
                                     'written in that order (CalendarSystem, a server of 4.3.4 only)',
}


def status(direction: str, mapped: bool, is_ported: bool, layout: str, seen: Seen | None, changed_since: bool,
           never_sent: bool) -> str:
    if not mapped:
        return 'unmapped'
    if never_sent and not (seen and seen.count):
        return 'not sent'
    if seen and changed_since:
        return 'retest'
    if seen and (seen.errors or seen.dropped):
        return 'failing'
    changed = layout == 'changed'
    if changed and not is_ported:
        return 'suspect' if seen and seen.count else 'todo'
    if seen and seen.count:
        return 'seen ok'
    return 'ported, unseen' if is_ported else 'unseen'


ORDER = ['failing', 'unmapped', 'todo', 'suspect', 'retest', 'ported, unseen', 'unseen', 'seen ok', 'not sent']
ICON = {'failing': '❌', 'unmapped': '🚫', 'todo': '⬜', 'suspect': '⚠️', 'retest': '🔁', 'ported, unseen': '🔧',
        'unseen': '·', 'seen ok': '✅', 'not sent': '∅'}
# The statuses a play session or a look at the code still has to clear, in the order shown.
WORK = ['failing', 'todo', 'suspect', 'ported, unseen', 'unseen', 'retest']


def work_section(graded: list) -> list[str]:
    """The rows that still ask for work, by feature, the most open first."""
    # A MSG_ opcode has a row for each direction; it is named once.
    per: dict[str, dict[str, set[str]]] = defaultdict(lambda: defaultdict(set))
    dropped_on: dict[str, str] = {}
    for st, d, op, po, _, seen, where in graded:
        if st in WORK:
            per[feature(where, op)][st].add(op)
        # A drop counts only while the sender still sends it to a 4.3.4 server: one that has a
        # 4.3.4 branch of its own by now was dropped before that branch was written.
        elif st == 'unmapped' and seen and (seen.errors or seen.dropped) and not po:
            per[feature(where, op)]['dropped'].add(op)
            dropped_on[op] = f'{seen.session[:4]}-{seen.session[4:6]}-{seen.session[6:8]}'

    def named_op(op: str) -> str:
        return f'`{op}`' + (f' (last {dropped_on[op]})' if op in dropped_on else '')
    if not per:
        return ['## What\'s left', '', 'Nothing: every row is seen ok, unmapped without a drop, or never sent.', '']
    md = ['## What\'s left', '',
          'The rows of the legacy table below that still ask for work, by feature. A session that plays a '
          f'feature clears its {ICON["ported, unseen"]}, {ICON["unseen"]} and {ICON["retest"]}; '
          f'{ICON["suspect"]} and {ICON["todo"]} ask for its 4.3.4 layout to be read; '
          f'{ICON["unmapped"]} here is an opcode HermesProxy sent, and still sends, that the 4.3.4 table lacks. '
          f'{ICON["retest"]} goes by file, so one edit marks every opcode the file handles: it is '
          'counted, not named.', '',
          f'| Feature | {ICON["failing"]} | {ICON["unmapped"]} | {ICON["todo"]} | {ICON["suspect"]} '
          f'| {ICON["ported, unseen"]} | {ICON["unseen"]} | {ICON["retest"]} | To play or read |',
          '|---|---:|---:|---:|---:|---:|---:|---:|---|']

    def weight(item: tuple[str, dict[str, set[str]]]) -> tuple:
        name, by = item
        named = sum(len(v) for k, v in by.items() if k != 'retest')
        return -named, -len(by.get('retest', ())), name

    for name, by in sorted(per.items(), key=weight):
        cells = [str(len(by[k])) if by.get(k) else '' for k in ('failing', 'dropped', 'todo', 'suspect',
                                                               'ported, unseen', 'unseen', 'retest')]
        named = '<br>'.join(f'{ICON["unmapped" if k == "dropped" else k]} ' + ', '.join(named_op(op) for op in sorted(by[k]))
                            for k in ('failing', 'dropped', 'todo', 'suspect', 'ported, unseen', 'unseen')
                            if by.get(k))
        md.append(f'| {name} | ' + ' | '.join(cells) + f' | {named} |')
    return md + ['']


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--wpp', type=Path, default=os.environ.get('HERMES_WPP_DIR'))
    ap.add_argument('--tc-cata', type=Path, default=os.environ.get('HERMES_TC_CATA_DIR'),
                    help='TrinityCore cata_classic checkout, for the "Requests nothing answered" column')
    ap.add_argument('--tc-434', type=Path, default=os.environ.get('HERMES_TC_434_DIR'),
                    help='TrinityCore 4.3.4 git checkout, for the server packets it never sends')
    ap.add_argument('--logs', type=Path, action='append')
    ap.add_argument('--out', type=Path, default=REPO / 'docs' / 'cata-progress.md')
    args = ap.parse_args()
    logs = args.logs or [p for p in (HP / 'bin' / 'Release' / 'Logs', HP / 'bin' / 'Debug' / 'Logs') if p.is_dir()]

    v335 = enum_names(HP / 'World' / 'Enums' / 'V3_3_5a_12340' / 'Opcode.cs')
    v434 = enum_names(HP / 'World' / 'Enums' / 'V4_3_4_15595' / 'Opcode.cs')
    sources = glob.glob(str(HP / 'World' / '**' / '*.cs'), recursive=True)
    methods = method_bodies(sources)
    wpp = wpp_signals(Path(args.wpp) if args.wpp else None)
    names: dict[int, list[str]] = defaultdict(list)
    for name, value in v434.items():
        names[value].append(name)
    ev = read_logs(logs, names)

    rows = []   # (dir, opcode, mapped, ported, layout, seen, where)
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
            # a forwarder that builds the packet from the opcode it is handed sends the ones it handles
            if 'new WorldPacket(opcode)' in body:
                for m in re.finditer(r'HandlesCmsg\(Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)', body):
                    forwarded[m.group(1)].append((rel, body))

    # An opcode only the 4.3.4 table has is 4.3.4 code by construction, and has no 3.3.5a layout to
    # have changed from.
    def layout_of(op: str) -> str:
        return wpp.get(op, '?') if op in v335 else 'new in 4.x'

    for op, defs in smsg_handlers.items():
        if op not in v335 and op not in v434:
            continue
        is_ported = op not in v335 or any(ported(b, methods) for _, b in defs)
        files = set().union(*(touched_files(r, b, methods) for r, b in defs))
        rows.append(('S', op, op in v434, is_ported, layout_of(op), ev.smsg.get(op), defs[0][0], files))
    # Movement CMSGs are built from a variable opcode, so no literal site names them; the ones
    # with a generated 4.3.4 sequence are written through LegacyMovementCata.
    sequences_file = HP / 'World' / 'Objects' / 'LegacyMovementSequencesCata.cs'
    sequenced = set(re.findall(r'\[Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)\]', sequences_file.read_text(encoding='utf-8-sig'))) \
        if sequences_file.exists() else set()
    movement_writer = os.path.relpath(HP / 'World' / 'Objects' / 'LegacyMovementCata.cs', REPO).replace(os.sep, '/')

    for op in set(cmsg_sites) | set(forwarded) | {o for o in ev.cmsg if o.startswith(('CMSG', 'MSG'))} | sequenced:
        if op not in v335 and op not in v434:
            continue
        defs = cmsg_sites.get(op, []) or ([] if op in sequenced else forwarded.get(op, []))
        # A sender inside a method named *Cata is the 4.3.4 version by construction.
        in_cata_method = any(re.search(r'\b\w+Cata\s*\(', b[:b.find('{')]) for _, b in defs)
        # An opcode the 4.3.4 table lacks is ported once a 4.3.4 server is sent something else.
        is_ported = (op not in v335 or op in sequenced or in_cata_method
                     or any(ported(send_segment(b, op), methods) for _, b in defs)
                     or op not in v434 and (op in RETIRED_ON_434 or any(cata_returns_before(b, op) for _, b in defs)))
        where = defs[0][0] if defs else (movement_writer if op in sequenced else '(variable opcode)')
        files = set().union(*(touched_files(r, send_segment(b, op), methods) for r, b in defs)) if defs else set()
        if op in sequenced:
            files |= {movement_writer, os.path.relpath(sequences_file, REPO).replace(os.sep, '/')}
        rows.append(('C', op, op in v434, is_ported, layout_of(op), ev.cmsg.get(op), where, files))

    all_files = sorted(set().union(*(r[7] for r in rows)))
    current = {f: content_hash((REPO / f).read_bytes()) for f in all_files}
    previous_rows, previous_sources = previous_page(args.out, ICON)
    if previous_sources is None:
        previous_sources = sources_at_page_commit(args.out, all_files)

    # The time on a file says when it was last written, and a checkout or a rebase writes files
    # whose content it does not change. So the time only counts for a file that differs from what
    # the page was last generated from; for the others, what the page said then still holds.
    def changed_since(key: tuple[str, str], seen: Seen | None, files: set[str]) -> bool:
        if not seen or not files:
            return False
        changed = [f for f in files if current[f] != previous_sources.get(f)]
        if changed:
            return max((REPO / f).stat().st_mtime for f in changed) > session_time(seen.session)
        return previous_rows.get(key) == ('retest', seen.session)

    rows = [(d, op, mp, po, 'reviewed' if lay == 'changed' and not po and (d, op) in LEGACY_REVIEWED else lay,
             seen, where, files) for d, op, mp, po, lay, seen, where, files in rows]
    sent = tc434_sent(Path(args.tc_434) if args.tc_434 else None)

    def never_sent(d: str, op: str) -> bool:
        return d == 'S' and sent is not None and op in v434 and v434[op] not in sent

    graded = [(status(d, mp, po, lay, seen, changed_since((d, op), seen, files), never_sent(d, op)),
               d, op, po, lay, seen, where)
              for d, op, mp, po, lay, seen, where, files in rows]
    graded.sort(key=lambda r: (ORDER.index(r[0]), r[1], r[2]))
    counts = defaultdict(int)
    for g in graded:
        counts[g[0]] += 1

    md = ['# Cataclysm — progress',
          '',
          '<!-- Generated by scripts/cata-progress.py. Do not edit by hand; re-run it. -->',
          '',
          f'{len(graded)} opcodes HermesProxy reads from or writes to the legacy server. '
          f'Logs: {len(ev.sessions)} session(s) against {LEGACY_BUILD}'
          + (f', latest {ev.sessions[-1]}' if ev.sessions else '') + '. '
          + ('WPP layout data included.' if wpp else 'No WPP checkout given: layout column is "?".'),
          '',
          '| Status | Count | Meaning |',
          '|---|---:|---|',
          f'| {ICON["failing"]} failing | {counts["failing"]} | the latest session that touched it threw or dropped it |',
          f'| {ICON["unmapped"]} unmapped | {counts["unmapped"]} | not in the 4.3.4 table: renamed, split or removed in 4.x |',
          f'| {ICON["todo"]} todo | {counts["todo"]} | layout changed in 4.x, no 4.3.4 branch, not seen yet |',
          f'| {ICON["suspect"]} suspect | {counts["suspect"]} | layout changed, no 4.3.4 branch, but passed without an error — likely misread |',
          f'| {ICON["retest"]} retest | {counts["retest"]} | its file changed after the latest session that exercised it |',
          f'| {ICON["ported, unseen"]} ported, unseen | {counts["ported, unseen"]} | has a 4.3.4 branch, waiting for a session to exercise it |',
          f'| {ICON["unseen"]} unseen | {counts["unseen"]} | same layout as 3.3.5a per WPP, not exercised yet |',
          f'| {ICON["seen ok"]} seen ok | {counts["seen ok"]} | ported or unchanged, and the latest session passed it cleanly |',
          f'| {ICON["not sent"]} not sent | {counts["not sent"] if sent is not None else "?"} | a server packet '
          'TrinityCore 4.3.4 never sends: another opcode carries it there, or nothing calls its writer'
          + ('' if sent is not None else ' (needs --tc-434)') + ' |',
          '']
    md += work_section(graded)

    client = client_layout_section(logs, ev.client_errors)
    # After the session errors and before the layout tables: what hangs comes before what differs.
    cut = next((i for i, line in enumerate(client) if line.startswith('Reading a change')), len(client))
    md += client[:cut] + unanswered_section(ev.unanswered, Path(args.tc_cata) if args.tc_cata else None) + client[cut:]

    md += ['## Legacy side (4.3.4 server)', '',
           '"reviewed" in the layout column: the shared parser has a 4.x branch, and TrinityCore 4.3.4 '
           'was read to see that it does not touch what HermesProxy reads or writes (LEGACY_REVIEWED '
           'in the script, with what was read).', '',
           '| | Dir | Opcode | 4.3.4 branch | WPP 4.x layout | Last session | Seen | Errors | HP file |',
           '|---|---|---|---|---|---|---:|---:|---|']
    for st, d, op, po, lay, seen, where in graded:
        last = seen.session if seen else ''
        n = seen.count if seen else ''
        err = (seen.errors + seen.dropped) if seen and (seen.errors or seen.dropped) else ''
        md.append(f'| {ICON[st]} | {d} | `{op}` | {"yes" if po else ""} | {lay} | {last} | {n} | {err} | {where} |')
    # What the rows above were read from, for the next run to tell a changed file from a rewritten one.
    md += ['', SOURCES_MARK] + [f'{f} {current[f]}' for f in all_files] + ['-->', '']

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text('\n'.join(md), encoding='utf-8')
    print(f'wrote {args.out}: ' + ', '.join(f'{k} {counts[k]}' for k in ORDER))


if __name__ == '__main__':
    main()
