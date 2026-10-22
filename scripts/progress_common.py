#!/usr/bin/env python3
"""What the progress pages share (scripts/cata-progress.py, scripts/wotlk-progress.py): HermesProxy's
methods and the packets they build, the page's own bookkeeping, the session logs and captures, the
docs/protocol layouts, and which server packets a TrinityCore checkout ever sends."""
from __future__ import annotations

import glob
import hashlib
import os
import re
import subprocess
import tempfile
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
HP = REPO / 'HermesProxy'

def enum_names(path: Path) -> dict[str, int]:
    text = path.read_text(encoding='utf-8-sig')
    return {m.group(1): int(m.group(2), 0)
            for m in re.finditer(r'^\s*([A-Z][A-Z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)', text, re.M)}


NOT_METHODS = {'if', 'for', 'foreach', 'while', 'switch', 'using', 'lock', 'catch', 'fixed', 'return',
               'new', 'await', 'nameof', 'typeof', 'sizeof', 'when', 'base', 'this'}
DECL = re.compile(r'^[ \t]+(?:(?:public|private|internal|protected|static|override|virtual|async|unsafe|readonly|'
                  r'sealed|extern|partial)\s+)*[\w<>\[\],?.]+\s+(\w+)\s*\(', re.M)


def _match(text: str, i: int, open_c: str, close_c: str) -> int:
    """Index of the bracket closing the one at i, skipping strings and comments."""
    depth = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '/' and text.startswith('//', i):
            i = text.find('\n', i)
            if i == -1:
                return n
            continue
        if c == '/' and text.startswith('/*', i):
            i = text.find('*/', i) + 2
            continue
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == '\\' else 1
        elif c == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == '\\' else 1
        elif c == open_c:
            depth += 1
        elif c == close_c:
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return n


def method_bodies(paths: list[str]) -> dict[str, list[tuple[str, str]]]:
    """Every method's name -> [(file, attribute lines + signature + body)]."""
    out: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for f in paths:
        text = Path(f).read_text(encoding='utf-8-sig', errors='ignore')
        rel = os.path.relpath(f, REPO).replace('\\', '/')
        pos = 0
        while (m := DECL.search(text, pos)):
            pos = m.end()
            name = m.group(1)
            if name in NOT_METHODS:
                continue
            close = _match(text, m.end() - 1, '(', ')')
            rest = text[close + 1:close + 400].lstrip()
            rest = re.sub(r'^where[^{=]*', '', rest).lstrip()
            if rest.startswith('{'):
                body_open = text.index('{', close)
                end = _match(text, body_open, '{', '}')
            elif rest.startswith('=>'):
                end = text.find(';', close)
            else:
                continue
            # attribute lines directly above the signature
            start = m.start()
            while True:
                prev = text.rfind('\n', 0, start - 1)
                line = text[prev + 1:start].strip()
                if line.startswith('['):
                    start = prev + 1
                else:
                    break
            out[name].append((rel, text[start:end + 1]))
            pos = end + 1
    return out


def touched_files(rel: str, body: str, methods: dict[str, list[tuple[str, str]]], depth: int = 2) -> set[str]:
    """The file holding this code and the files of every method it hands the packet to. A call on
    a type (`MaskedGuid.Write(packet, …)`) is the method of that name in the type's own file when
    there is one: every packet layout has a Write too."""
    files = {rel}
    if depth:
        for owner, callee in set(re.findall(r'(?:\b(\w+)\.)?\b([A-Z]\w+)\s*\(\s*(?:ref\s+)?packet\b', body)):
            candidates = methods.get(callee, [])
            if owner[:1].isupper():
                candidates = [c for c in candidates if Path(c[0]).stem == owner] or candidates
            for callee_rel, callee_body in candidates:
                files |= touched_files(callee_rel, callee_body, methods, depth - 1)
    return files


def session_time(session: str) -> float:
    return datetime.strptime(session, '%Y%m%d_%H%M%S').timestamp()


SOURCES_MARK = '<!-- sources'


def content_hash(data: bytes) -> str:
    """A file's content whatever its line endings, which a checkout may rewrite."""
    return hashlib.sha1(data.replace(b'\r\n', b'\n')).hexdigest()[:16]


def previous_page(out: Path, icons: dict[str, str], skip: int = 2) -> tuple[dict[tuple[str, str], tuple[str, str]], dict[str, str] | None]:
    """What the page said when it was last generated, and the sources it said it about.

    Rows: (direction, opcode) -> (status, last session), the session `skip` cells after the opcode.
    Sources: file -> content hash, from the block the page ends with; None for a page written
    before it had one.
    """
    if not out.exists():
        return {}, {}
    text = out.read_text(encoding='utf-8')
    by_icon = {icon: name for name, icon in icons.items()}
    rows = {}
    row = re.compile(r'^\| (\S+) \| ([CS]) \| `([A-Z0-9_]+)` \|' + r'[^|]*\|' * skip + r' ([0-9_]*) \|', re.M)
    for m in row.finditer(text):
        if m.group(1) in by_icon:
            rows[(m.group(2), m.group(3))] = (by_icon[m.group(1)], m.group(4))
    block = re.search(re.escape(SOURCES_MARK) + r'(.*?)-->', text, re.S)
    if not block:
        return rows, None
    return rows, dict(line.split() for line in block.group(1).strip().splitlines())


def sources_at_page_commit(out: Path, files: list[str]) -> dict[str, str]:
    """For a page without a sources block: the files as of the commit that last touched it."""
    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(['git', '-C', str(REPO), *args], capture_output=True)

    try:
        rel = out.resolve().relative_to(REPO).as_posix()
    except ValueError:
        return {}
    commit = git('log', '-1', '--format=%H', '--', rel).stdout.decode().strip()
    if not commit:
        return {}
    hashes = {}
    for f in files:
        blob = git('show', f'{commit}:{f}')
        if blob.returncode == 0:
            hashes[f] = content_hash(blob.stdout)
    return hashes


def packet_sites(body: str) -> list[tuple[int, set[str]]]:
    """Each `new WorldPacket(...)` in the body and the opcodes it can be built with: those its
    argument names (`packet.Player ? Opcode.A : Opcode.B`), or those given to the local it is
    handed (`Opcode mgrOpcode = queue ? A : B;`)."""
    sites = []
    for m in re.finditer(r'new WorldPacket\(', body):
        arg = body[m.end():_match(body, m.end() - 1, '(', ')')].strip()
        ops = set(re.findall(r'Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)', arg))
        if re.fullmatch(r'\w+', arg) and arg != 'opcode':
            for value in re.findall(rf'\b{arg}\s*=(?!=)([^;]*);', body):
                ops |= set(re.findall(r'Opcode\.((?:CMSG|MSG)_[A-Z0-9_]+)', value))
        sites.append((m.start(), ops))
    return sites


def packet_site(body: str, opcode: str) -> int:
    return next((start for start, ops in packet_sites(body) if opcode in ops), -1)


def send_segment(body: str, opcode: str) -> str:
    """The code from the `new WorldPacket` that builds this opcode to the call that sends it."""
    start = packet_site(body, opcode)
    if start == -1:
        start = body.find('new WorldPacket(opcode)')
    if start == -1:
        return ''
    end = body.find('SendPacketToServer(', start)
    return body[start:end if end != -1 else len(body)]


@dataclass
class Seen:
    session: str = ''
    count: int = 0
    errors: int = 0
    dropped: int = 0
    unhandled: int = 0
    sample: str = ''


@dataclass
class LogEvidence:
    sessions: list[str] = field(default_factory=list)
    smsg: dict[str, Seen] = field(default_factory=dict)       # legacy server -> proxy
    cmsg: dict[str, Seen] = field(default_factory=dict)       # proxy -> legacy server
    client_errors: dict[str, Seen] = field(default_factory=dict)  # 4.4.2 client side
    unanswered: dict[str, Seen] = field(default_factory=dict)     # client requests with no handler


RECV = re.compile(r'\| C P<S \| Received opcode "([A-Z0-9_]+)"')
SENT = re.compile(r'\| C P>S \| Sending opcode "([A-Z0-9_]+)"')
SMSG_EXC = re.compile(r'Unhandled exception in handler for ((?:SMSG|MSG)_[A-Z0-9_]+)')
CMSG_EXC = re.compile(r'C>P S \| Unhandled exception in handler for ((?:CMSG|MSG)_[A-Z0-9_]+)')
NO_MODERN_MAP = re.compile(r'No V\d_\d_\d_\d+ opcode mapping for ([A-Z0-9_]+)')
NO_HANDLER = re.compile(r'C P<S \| No handler for opcode ([A-Z0-9_]+)')
CLIENT_NO_HANDLER = re.compile(r'C>P S \| No handler for opcode ([A-Z0-9_]+) \((\d+)\)')
CLIENT_RECV = re.compile(r'\| C>P S \| Received opcode "([A-Z0-9_]+)"')
CLIENT_SENT = re.compile(r'\| C<P S \| Sending opcode "([A-Z0-9_]+)"')


def native_handlers(tc: Path | None) -> dict[str, bool]:
    """Client opcode -> does a native TrinityCore server (its Opcodes.cpp) act on it. A native
    server that ignores a request is the evidence that the client does not wait for an answer."""
    table = tc / 'src' / 'server' / 'game' / 'Server' / 'Protocol' / 'Opcodes.cpp' if tc else None
    if not table or not table.exists():
        return {}
    out: dict[str, bool] = {}
    for m in re.finditer(r'DEFINE_HANDLER\((CMSG_\w+),\s*(STATUS_\w+),\s*\w+,\s*&WorldSession::(\w+)\)',
                         table.read_text(encoding='utf-8', errors='ignore')):
        out[m.group(1)] = m.group(2) != 'STATUS_UNHANDLED' and m.group(3) != 'Handle_NULL'
    return out


def read_capture(path: Path, names: dict[int, list[str]]) -> tuple[dict[str, int], dict[str, int]]:
    """Opcode counts from a legacy .pkt capture: (from the server, to the server). The log only
    names the opcodes it logs, and it leaves out the busy ones; the capture has every packet."""
    data = path.read_bytes()
    smsg: dict[str, int] = defaultdict(int)
    cmsg: dict[str, int] = defaultdict(int)
    off = 47
    while off + 13 <= len(data):
        direction = data[off]
        size = int.from_bytes(data[off + 9:off + 13], 'little')
        body = data[off + 13:off + 13 + size]
        off += 13 + size
        if direction == 0xff and len(body) >= 2:
            target, op = smsg, int.from_bytes(body[:2], 'little')
        elif len(body) >= 4:
            target, op = cmsg, int.from_bytes(body[:4], 'little')
        else:
            continue
        for name in names.get(op, []):
            target[name] += 1
    return smsg, cmsg


def client_opcode_names(build: str) -> dict[int, str]:
    """A client's opcode -> name, from its docs/protocol build: the log prints MSG_NULL_ACTION for a
    request HermesProxy's table has no name for."""
    out: dict[int, str] = {}
    table = REPO / 'docs' / 'protocol' / build / 'cmsg-opcodes.md'
    if table.exists():
        for line in table.read_text(encoding='utf-8').splitlines():
            cells = [c.strip().strip('`') for c in line.strip().strip('|').split('|')]
            if len(cells) >= 2 and cells[1].startswith('0x') and cells[0] not in ('—', '-'):
                out[int(cells[1], 16)] = cells[0]
    return out


DOCS = REPO / 'docs' / 'protocol'


def doc_layouts(build_dir: Path) -> dict[str, tuple[str, str]]:
    """Opcode -> (layout, HermesProxy type) from one build's docs/protocol structures."""
    out: dict[str, tuple[str, str]] = {}
    files = list((build_dir / 'smsg-structures').glob('*.md')) + [build_dir / 'cmsg-structures.md']
    for f in files:
        if not f.exists():
            continue
        for block in re.split(r'^### ', f.read_text(encoding='utf-8'), flags=re.M)[1:]:
            name = block.split()[0]
            layout = re.search(r'^- Layout[^`]*`([^`]*)`', block, re.M)
            hp = re.search(r'^- HermesProxy: `(\w+)`', block, re.M)
            out[name] = (layout.group(1) if layout else '', hp.group(1) if hp else '')
    return out


def doc_links(build_dir: Path) -> dict[str, str]:
    """Opcode -> its heading in one build's docs/protocol structures, as a link from docs/."""
    out: dict[str, str] = {}
    files = list((build_dir / 'smsg-structures').glob('*.md')) + [build_dir / 'cmsg-structures.md']
    for f in files:
        if not f.exists():
            continue
        used: dict[str, int] = defaultdict(int)
        for heading in re.findall(r'^### (.+?)\s*$', f.read_text(encoding='utf-8'), flags=re.M):
            # GitHub's anchor: lower case, punctuation but '-' and '_' dropped, spaces as '-',
            # and '-1', '-2' on a heading that repeats in the file.
            slug = re.sub(r'[^\w\- ]', '', heading.lower()).replace(' ', '-')
            anchor = slug + (f'-{used[slug]}' if used[slug] else '')
            used[slug] += 1
            out.setdefault(heading.split()[0], f'{f.relative_to(REPO / "docs").as_posix()}#{anchor}')
    return out


def cmsg_packet_types() -> dict[str, str]:
    """The packet type each CMSG handler takes today, from [HandlesCmsg] and the handler's
    `in T` parameter."""
    out: dict[str, str] = {}
    for f in glob.glob(str(HP / 'World' / 'Server' / 'Systems' / '*.cs')):
        text = Path(f).read_text(encoding='utf-8-sig', errors='ignore')
        for m in re.finditer(r'((?:\[HandlesCmsg\(Opcode\.\w+\)\]\s*)+)public static void \w+\(([^)]*)\)', text):
            param = re.search(r'\bin (\w+) \w+\s*,\s*in SessionContext', m.group(2))
            if param:
                for opcode in re.findall(r'Opcode\.(\w+)', m.group(1)):
                    out[opcode] = param.group(1)
    return out


def grep_with_context(git, *args: str) -> list[tuple[str | None, str]]:
    """git grep's hits, comments cut off, each with the name of the function around it (the line
    git -p prints above them): None outside a function, or where that line is a class."""
    out: list[tuple[str | None, str]] = []
    path, context = None, None
    for line in git('grep', '-p', '-n', *args).splitlines():
        m = re.match(r'HEAD:([^:=]+)([=:])\d+\2(.*)', line)
        if not m:
            continue
        if m.group(1) != path:
            path, context = m.group(1), None
        if m.group(2) == '=':
            callable_ = re.search(r'(\w+)\s*\(', m.group(3))
            context = callable_.group(1) if callable_ else None
            continue
        text = m.group(3).split('//')[0]
        if not text.lstrip().startswith('*'):
            out.append((context, text))
    return out


CPP_KEYWORDS = {'return', 'else', 'new', 'delete', 'case', 'throw', 'do', 'sizeof', 'co_return'}


# Called from outside the tree: CMake writes the script loader that calls every AddSC_ function.
ENTRY_POINTS = re.compile(r'AddSC_\w+|main')


def live_callables(git, names: set[str], depth: int = 4) -> set[str]:
    """The names something reachable uses. A use inside a function counts when that function is
    used in turn, followed up to `depth` functions; a use outside any function, in an entry point
    or in a function not followed counts at once. Unit::SendPlaySpellVisual builds the
    PlaySpellVisual packet, and nothing calls it. Only a name used in one or two functions has them
    followed: each step is a grep of the whole server, and a name used more widely is not dead."""
    users: dict[str, set[str | None]] = {}
    frontier = set(names)
    for _ in range(depth):
        found = users_of(git, frontier)
        users.update(found)
        frontier = {c for cs in found.values() if None not in cs and len(cs) <= 2 for c in cs
                    if c not in users and not ENTRY_POINTS.fullmatch(c)}
        if not frontier:
            break
    live: set[str] = set()
    grew = True
    while grew:
        grew = False
        for name, contexts in users.items():
            if name not in live and any(c is None or c not in users or ENTRY_POINTS.fullmatch(c) or c in live
                                        for c in contexts):
                live.add(name)
                grew = True
    return live


def users_of(git, names: set[str]) -> dict[str, set[str | None]]:
    """For each name, the functions whose lines use it (see `uses`); an empty set when none do."""
    out: dict[str, set[str | None]] = {name: set() for name in names}
    if not names:
        return out
    # One name a line through a file: thousands of them on the command line pass Windows' limit.
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as f:
        f.write('\n'.join(sorted(names)))
    try:
        hits = grep_with_context(git, '-w', '-F', '-f', f.name, 'HEAD', '--', 'src/server')
    finally:
        os.unlink(f.name)
    for context, text in hits:
        for m in re.finditer(r'\w+', text):
            if m.group() in out and context not in out[m.group()] and uses(text, m.start(), m.end()):
                out[m.group()].add(context)
    return out


def uses(text: str, start: int, end: int) -> bool:
    """Whether the name at text[start:end] is used there rather than declared or defined."""
    before = re.sub(r'(\w+::)+$', '', text[:start]).rstrip()
    after = text[end:]
    if not before:
        # `Name(...) : ServerPacket(...)` or `Name(...) {` opens a definition; `Name(...);` calls.
        # No parentheses inside: `Name(a ? b->Get() : c);` is a call, not an initializer list.
        return not re.match(r'\s*\([^;()]*\)\s*(const\s*)?(override\s*)?(final\s*)?(:|\{|$)', after)
    if before.endswith(('->', '.')):
        return True
    if before.endswith('&'):
        # An address: `&Spell::EffectDispel,` opens a line of the spell effect table.
        return bool(re.search(r'(^|[(,={])\s*&$', before))
    word = re.search(r'(\w+)$', before)
    if word or before.endswith(('*', '>')):
        # A type in front: `void Player::Name(` or `class Name`, unless that word is a keyword.
        return bool(word) and word.group(1) in CPP_KEYWORDS
    return True


# A feature's name from the file that handles its packets; the aliases join the files of one.
FEATURE_ALIASES = {'BattleGround': 'Battleground', 'BattlefieldMgr': 'Battlefield', 'Lfg': 'LFG', 'Npc': 'NPC',
                   'LegacyMovementCata': 'Movement', 'GuildClub': 'Guild', 'Update': 'Objects',
                   'MeleeAttackOrder': 'Combat', 'WorldClient': 'Connection'}
# Forwarded opcodes have no file of their own: their name says the feature.
FORWARDED_FEATURES = [('BF_MGR', 'Battlefield'), ('LFG', 'LFG'), ('CALENDAR', 'Calendar'), ('VEHICLE', 'Movement'),
                      ('LOOT', 'Loot'), ('BANK', 'Item')]


def feature(where: str, opcode: str) -> str:
    if where.endswith('.cs'):
        stem = re.sub(r'(Handler|System)$', '', Path(where).stem)
        return FEATURE_ALIASES.get(stem, stem)
    return next((name for token, name in FORWARDED_FEATURES if token in opcode), 'Other')


def deliberately_unhandled() -> set[str]:
    """Opcodes the proxy leaves alone on purpose (World/Logging/DeliberatelyUnhandledOpcodes.cs):
    a request of that list is not waiting for a handler."""
    path = HP / 'World' / 'Logging' / 'DeliberatelyUnhandledOpcodes.cs'
    if not path.exists():
        return set()
    return set(re.findall(r'Opcode\.((?:CMSG|SMSG)_\w+)', path.read_text(encoding='utf-8-sig')))


def server_sent(tc: Path | None, never_sent: dict[str, str]) -> set[int] | None:
    """The opcode values a TrinityCore checkout writes somewhere, read from its HEAD tree.

    An opcode name counts when the server code names it, except as a `case` label or in a
    comparison with an opcode: that is the server reading what a client sent. A log string or a
    comment that names it does not count. Nor does a function or packet class nothing reachable
    uses (see live_callables): Player::SendAttackSwingDeadTarget writes SMSG_ATTACKSWING_DEADTARGET,
    and no line calls it. A name that only appears in a table counts as sent, so a doubt keeps a
    row in the work."""
    if tc is None:
        return None

    def git(*args: str) -> str:
        return subprocess.run(['git', '-C', str(tc), *args], capture_output=True, text=True,
                              encoding='utf-8', errors='ignore').stdout

    header = git('show', 'HEAD:src/server/game/Server/Protocol/Opcodes.h')
    if not header:
        return None
    values: dict[str, int] = {m.group(1): int(m.group(2), 0) for m in re.finditer(
        r'^\s*((?:SMSG|MSG)_[A-Z0-9_]+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)', header, re.M)}
    reading = re.compile(r'case\s+\w+\s*:|\b\w+(?:\(\))?\s*[!=]=\s*(?:SMSG|MSG)_\w+|(?:SMSG|MSG)_\w+\s*[!=]=')
    # Each opcode's places, by the function or packet class around them; None where neither shows.
    places: dict[int, list[str | None]] = {}
    for context, text in grep_with_context(git, '-E', r'\b(SMSG|MSG)_[A-Z0-9_]+', 'HEAD', '--', 'src/server',
                                           ':(exclude)src/server/game/Server/Protocol/Opcodes.*'):
        ctor = re.match(r'\s*(\w+)\s*\([^)]*\)\s*:\s*ServerPacket\(', text)
        # Code names an opcode after an operator or `return`; a log string or a comment's prose
        # ("These movement packets are usually found in SMSG_COMPRESSED_MOVES") does not send it.
        for name in re.findall(r'(?:[(,={?:|]|\breturn)\s*((?:SMSG|MSG)_[A-Z0-9_]+)\b', reading.sub('', text)):
            if name in values:
                places.setdefault(values[name], []).append(ctor.group(1) if ctor else context)

    callables = {c for cs in places.values() for c in cs if c}
    live = live_callables(git, callables)
    return {value for value, cs in places.items() if any(c is None or c in live for c in cs)
            and value not in {values.get(name) for name in never_sent}}


    # never_sent: opcodes read by hand whose writer is called, never with what it takes to send them.
