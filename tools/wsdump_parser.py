"""
wsdump_parser.py — Streaming parser for Dyalog APL wsdump text output.

Parses both default (full dump) and -i (crash info) output from wsdump.exe.
Designed for large files (600+ MB): two-pass architecture with lazy access.

Usage:
    from wsdump_parser import WsDump
    dump = WsDump("aplcore_10.txt")           # full dump
    dump.build_index()                         # pass 1: build address→offset map
    obj = dump.get_object("0000000006cc0000")  # random access by address
    
    info = CrashInfo("wsdump_i_stdout.txt")    # -i output
    print(info.exception_code)
"""

import re
import os
import struct
import sys
from dataclasses import dataclass, field
from typing import Optional, Dict, List, Tuple, Iterator, Any


# ---------------------------------------------------------------------------
# Name decoding
# ---------------------------------------------------------------------------

def decode_symbol_name(hex_words: List[str]) -> str:
    """Decode symbol name from UTF-16LE hex words.
    
    Each 16-hex-digit word contains up to 4 UTF-16LE characters.
    E.g., '0041005300530065' → 'AsSe' (A=0x0041, s=0x0073... wait, 
    actually each pair is little-endian within the word).
    
    The layout is: word = AABBCCDD EEFFGGHH where chars are 
    0xBBAA, 0xDDCC, 0xGGFF, 0xHHEE... actually let's look at real data:
    
    '0000005400432395' for '⎕CT':
      bytes: 95 23 43 00 54 00 00 00
      chars: 0x2395=⎕, 0x0043=C, 0x0054=T, 0x0000=NUL
    
    So the word is read as little-endian bytes, then parsed as UTF-16LE pairs.
    """
    chars = []
    for word in hex_words:
        # Pad to 16 hex digits
        word = word.zfill(16)
        # Convert to bytes (big-endian hex string → bytes, then reverse for LE)
        raw = bytes.fromhex(word)
        # raw is 8 bytes in big-endian order of the hex string
        # but the word represents a 64-bit LE value, so the bytes in memory are reversed
        le_bytes = raw[::-1]  # reverse to get actual memory layout
        # Now parse as UTF-16LE pairs
        for i in range(0, 8, 2):
            code_point = le_bytes[i] | (le_bytes[i+1] << 8)
            if code_point == 0:
                return ''.join(chars)
            chars.append(chr(code_point))
    return ''.join(chars)


def decode_type_flags(type_flags: int) -> dict:
    """Decode the TYPE_FLAGS word into components."""
    return {
        'raw': type_flags,
        'hex': f'0x{type_flags:016x}',
    }


# Internal APL primitive opcode → display name for ENVIRON frames.
# Determined empirically: functn/fname fields in ENVIRON stack objects are small
# integers (not workspace pointers) identifying the suspended primitive.
# functn=0x20, fname=0xb0 observed for ⎕NA (threads blocked in native DLL call).
# functn=0x0,  fname=0x0  observed for ⍎  (execute, between function calls).
# NOTE: ⎕TGET does NOT create an ENVIRON frame — the thread blocks transparently
# inside the DFN/MODE frame where ⎕TGET was called. The thread descriptor's
# `wake` field holds the wakeup timestamp (non-zero = timed wait, 0 = indefinite).
ENVIRON_FUNCTN_NAMES: Dict[Tuple[int, int], str] = {
    (0x20, 0xb0): '⎕NA',
    (0x00, 0x00): '⍎',
}

def decode_environ_functn(functn: int, fname: int) -> str:
    """Return display name for an ENVIRON frame's primitive opcode pair."""
    name = ENVIRON_FUNCTN_NAMES.get((functn, fname))
    if name:
        return name
    if functn == 0 and fname == 0:
        return '⍎'
    return f'<sys:0x{functn:x}/0x{fname:x}>'


# ---------------------------------------------------------------------------
# Data classes for parsed objects
# ---------------------------------------------------------------------------

@dataclass
class WsHeader:
    """Parsed wsdump header."""
    magic: str = ""
    ws_version: str = ""
    apl_version: str = ""
    filename: str = ""
    timestamp: str = ""
    svn_revision: int = 0
    not_coded: str = ""


@dataclass  
class WsField:
    """A single field in an object."""
    offset: int           # offset within the object (from OFFSET column)
    values: List[int]     # raw 64-bit values
    labels: List[str]     # field labels (with * prefix for pointers)
    
    def is_pointer(self, idx: int = 0) -> bool:
        return idx < len(self.labels) and self.labels[idx].startswith('*')
    
    def pointer_target(self, idx: int = 0) -> Optional[int]:
        if idx < len(self.values) and self.values[idx] != 0:
            return self.values[idx]
        return None


@dataclass
class WsObject:
    """Base class for all workspace objects."""
    address: int           # virtual address
    neg_size: int          # raw neg_size (negative)
    tally: int             # element count
    type_flags: int        # packed type word
    typename: str          # decoded type name
    abs_size: int          # positive size in 8-byte units
    slot_addr: int         # slot address
    tag: str = ""          # type-specific tag (THREAD, LNS, etc.)
    extra: str = ""        # extra flags (S, C, F, V, etc. for symbols)
    fields: List[WsField] = field(default_factory=list)
    # For symbols: decoded name
    _name: Optional[str] = None
    # File offset where this object starts in the wsdump text
    file_offset: int = 0
    
    @property
    def size_bytes(self) -> int:
        """Object size in bytes."""
        return self.abs_size * 8
    
    @property
    def name(self) -> Optional[str]:
        """Decoded name (for symbols)."""
        if self._name is not None:
            return self._name
        if self.typename == 'symbol':
            self._name = self._decode_name()
        return self._name
    
    def _decode_name(self) -> Optional[str]:
        """Decode symbol name from symflo fields.
        
        Symbol name data follows the *symflo pointer in the field values.
        For multi-word names, continuation fields hold additional words.
        """
        found_symflo = False
        hex_words = []
        for f in self.fields:
            if not found_symflo:
                if '*symflo' in f.labels:
                    symflo_idx = f.labels.index('*symflo')
                    for j in range(symflo_idx + 1, len(f.values)):
                        hex_words.append(f'{f.values[j]:016x}')
                    found_symflo = True
            else:
                # Continuation: stop at next pointer field (e.g., *<=)
                if any(l.startswith('*') for l in f.labels):
                    break
                for v in f.values:
                    hex_words.append(f'{v:016x}')
        
        if hex_words:
            return decode_symbol_name(hex_words)
        return None
    
    def get_field(self, label: str) -> Optional[int]:
        """Get the value of a named field (pointer or scalar)."""
        search = label if label.startswith('*') else label
        for f in self.fields:
            for i, l in enumerate(f.labels):
                if l == search and i < len(f.values):
                    return f.values[i]
        return None
    
    def get_pointer(self, label: str) -> Optional[int]:
        """Get a pointer field value (auto-prepends * if needed)."""
        if not label.startswith('*'):
            label = '*' + label
        val = self.get_field(label)
        return val if val and val != 0 else None
    
    @property
    def named_fields_dict(self) -> Dict[str, int]:
        """Return all named fields as {label: value} dict."""
        result = {}
        for f in self.fields:
            for i, label in enumerate(f.labels):
                if i < len(f.values) and (label.startswith('*') or label.isalnum() 
                                           or label in ('count', 'tid', 'pc', 'retn',
                                                       'rflags', 'temp', 'modes',
                                                       'shad', 'guis', 'sleep')):
                    result[label] = f.values[i]
        return result
    
    def __repr__(self):
        name_str = f' name={self.name!r}' if self.typename == 'symbol' and self.name else ''
        tag_str = f' {self.tag}' if self.tag else ''
        return (f'<WsObject {self.typename}{tag_str} @{self.address:016x} '
                f'size={self.abs_size}{name_str}>')


@dataclass
class APLStackFrame:
    """One frame from the !APLStack trailer."""
    tid: int
    namespace: str
    function: str
    line: int
    source: str


@dataclass
class AddressMapping:
    """One !AddressSpace entry."""
    base_addr: int
    alloc_base: int
    alloc_protect: int
    region_size: int
    state: int
    protect: int
    mem_type: int
    dll_name: str = ""


@dataclass
class CStackFrame:
    """One C stack frame from -i output."""
    address: int


@dataclass
class CrashInfo:
    """Parsed wsdump -i output."""
    header: WsHeader = field(default_factory=WsHeader)
    serial: str = ""
    dflags: int = 0
    maxws: int = 0
    session: int = 0
    quit: int = 0
    major_version: int = 0
    minor_version: int = 0
    svn_revision: int = 0
    bits: int = 0
    edition: str = ""
    debug: int = 0
    endian: str = ""
    execution: str = ""
    created: str = ""
    command_line: str = ""
    args: List[str] = field(default_factory=list)
    opts: Dict[str, str] = field(default_factory=dict)
    ws_filename: str = ""
    wsid: str = ""
    syserror: int = 0
    syserror_msg: str = ""
    build_id: int = 0
    exception_code: int = 0
    exception_flags: int = 0
    exception_address: int = 0
    exception_params: List[int] = field(default_factory=list)
    registers: Dict[str, int] = field(default_factory=dict)
    apl_main: int = 0
    c_stack: List[CStackFrame] = field(default_factory=list)
    apl_stack: List[APLStackFrame] = field(default_factory=list)
    address_space: List[AddressMapping] = field(default_factory=list)
    
    @classmethod
    def from_file(cls, filepath: str) -> 'CrashInfo':
        """Parse a wsdump -i output file."""
        info = cls()
        with open(filepath, 'r', encoding='utf-8', errors='replace') as f:
            info._parse(f)
        return info
    
    def _parse(self, f):
        """Parse the -i output."""
        lines = f.readlines()
        i = 0
        
        # Parse header
        while i < len(lines):
            line = lines[i].rstrip()
            if line.startswith('magic no.'):
                parts = line.split()
                self.header.magic = parts[2] if len(parts) > 2 else ""
                for p in parts:
                    if p.startswith('WSversion'):
                        continue
                    if '.' in p and parts[parts.index(p)-1] == 'WSversion':
                        self.header.ws_version = p
                    if parts[parts.index(p)-1] == 'APLversion':
                        self.header.apl_version = p
                # Better parsing:
                m = re.match(r'magic no\. (\w+) WSversion ([\d.]+) APLversion ([\d.]+)', line)
                if m:
                    self.header.magic = m.group(1)
                    self.header.ws_version = m.group(2)
                    self.header.apl_version = m.group(3)
            elif ' saved ' in line and not line.startswith('!'):
                self.header.filename = line.split(' saved ')[0].strip()
                self.header.timestamp = line.split(' saved ')[1].strip()
            elif line.startswith('svn revision'):
                self.header.svn_revision = int(line.split()[-1])
            elif line.startswith('!'):
                break
            i += 1
        
        # Parse ! sections
        while i < len(lines):
            line = lines[i].rstrip()
            i += 1
            
            if not line.startswith('!'):
                continue
            
            # CStack — check BEFORE general key-value to avoid false match
            if line.startswith('!CStack:') and 'END' not in line:
                if 'top_of' in line.lower():
                    m2 = re.search(r'0x([0-9a-fA-F]+)', line)
                    if m2:
                        self.registers['top_of_Cstack'] = int(m2.group(1), 16)
                else:
                    m2 = re.search(r'0x([0-9a-fA-F]+)', line)
                    if m2:
                        self.c_stack.append(CStackFrame(int(m2.group(1), 16)))
                continue
            
            # APLStack — check BEFORE general key-value
            if line.startswith('!APLStack:') and 'END' not in line:
                m2 = re.match(r'!APLStack: !TID:(\d+)!([^[]+)\[(\d+)\]\s*(.*?)!?\s*$', line)
                if m2:
                    self.apl_stack.append(APLStackFrame(
                        tid=int(m2.group(1)),
                        namespace=m2.group(2).rsplit('.', 1)[0] if '.' in m2.group(2) else '#',
                        function=m2.group(2).rsplit('.', 1)[-1] if '.' in m2.group(2) else m2.group(2),
                        line=int(m2.group(3)),
                        source=m2.group(4).rstrip('!').strip()
                    ))
                continue
            
            # AddressSpace
            if line.startswith('!AddressSpace:0x'):
                parts = line.replace('!AddressSpace:', '').split()
                if len(parts) >= 7:
                    try:
                        am = AddressMapping(
                            base_addr=int(parts[0], 16),
                            alloc_base=int(parts[1], 16),
                            alloc_protect=int(parts[2], 16),
                            region_size=int(parts[3], 16),
                            state=int(parts[4], 16),
                            protect=int(parts[5], 16),
                            mem_type=int(parts[6], 16),
                            dll_name=parts[7] if len(parts) > 7 else ""
                        )
                        self.address_space.append(am)
                    except (ValueError, IndexError):
                        pass
                continue
            
            # Key-value pairs
            m = re.match(r'!(\w+):\s+0x([0-9a-fA-F]+)', line)
            if m:
                key, val = m.group(1), int(m.group(2), 16)
                key_lower = key.lower()
                if key == 'Serial':
                    # Special format: !Serial: !NNNNNN!
                    sm = re.search(r'!(\d+)!', line)
                    if sm:
                        self.serial = sm.group(1)
                elif key_lower == 'dflags': self.dflags = val
                elif key_lower == 'maxws': self.maxws = val
                elif key_lower == 'maxwp': pass
                elif key_lower == 'session': self.session = val
                elif key_lower == 'quit': self.quit = val
                elif key_lower == 'exitcode': pass
                elif key_lower == 'syserror': self.syserror = val
                elif key_lower == 'buildid': self.build_id = val
                elif key_lower == 'exceptioncode': self.exception_code = val
                elif key_lower == 'exceptionflags': self.exception_flags = val
                elif key_lower == 'exceptionaddress': self.exception_address = val
                elif key_lower == 'exceptionparameter':
                    self.exception_params.append(val)
                elif key_lower == 'apl_main': self.apl_main = val
                elif key_lower in ('rax','rbx','rcx','rdx','rsp','rbp','rsi','rdi',
                                   'r8','r9','r10','r11','r12','r13','r14','r15',
                                   'rip','segcs','segds','seges','segfs','seggs','segss',
                                   'eflags','contextflags',
                                   'p1home','p2home','p3home','p4home','p5home','p6home',
                                   'dr0','dr1','dr2','dr3','dr6','dr7'):
                    self.registers[key] = val
                continue
            
            # Integer values
            m = re.match(r'!(\w+):\s+(\d+)', line)
            if m:
                key, val = m.group(1), int(m.group(2))
                if key == 'MAJOR_VERSION': self.major_version = val
                elif key == 'MINOR_VERSION': self.minor_version = val
                elif key == 'SVN_REVISION': self.svn_revision = val
                elif key == 'BITS': self.bits = val
                elif key == 'DEBUG': self.debug = val
                continue
            
            # String values
            if line.startswith('!EDITION:'):
                self.edition = line.split(':', 1)[1]
            elif line.startswith('!ENDIAN:'):
                self.endian = line.split(':', 1)[1]
            elif line.startswith('!EXECUTION:'):
                self.execution = line.split(':', 1)[1]
            elif line.startswith('!Created:'):
                self.created = line.lstrip('!').replace('Created: ', '').rstrip('!')
            elif line.startswith('!CommandLine:'):
                self.command_line = line.split(':', 1)[1].strip().strip('!')
            elif line.startswith('!WSFileName:'):
                self.ws_filename = line.split(':', 1)[1].strip()
            elif line.startswith('!WSID:'):
                self.wsid = line.split(':', 1)[1].strip()
            elif line.startswith('!msg:'):
                self.syserror_msg = line.split(':', 1)[1].strip().strip('!')
            elif line.startswith('!Serial:'):
                sm = re.search(r'!(\d+)!', line)
                if sm:
                    self.serial = sm.group(1)
            
            # Options
            elif line.startswith('!opts:'):
                pass  # header for options
            elif '=' in line and line.startswith('!') and not line.startswith('!AddressSpace'):
                kv = line.strip('!').strip()
                if '=' in kv and not kv.startswith('+'):
                    k, v = kv.split('=', 1)
                    self.opts[k] = v
    
    def exception_description(self) -> str:
        """Human-readable exception description."""
        codes = {
            0xC0000005: "ACCESS_VIOLATION",
            0xC0000094: "INTEGER_DIVIDE_BY_ZERO",
            0xC00000FD: "STACK_OVERFLOW",
            0xC0000409: "STACK_BUFFER_OVERRUN",
        }
        desc = codes.get(self.exception_code, f"UNKNOWN(0x{self.exception_code:08x})")
        if self.exception_code == 0xC0000005 and len(self.exception_params) >= 2:
            rw = "read" if self.exception_params[0] == 0 else "write"
            addr = self.exception_params[1]
            desc += f" ({rw} at 0x{addr:x})"
        return desc
    
    def summary(self) -> str:
        """Produce a human-readable crash summary."""
        lines = []
        lines.append(f"=== Aplcore Crash Report ===")
        lines.append(f"Application:  {self.ws_filename}")
        lines.append(f"WSID:         {self.wsid}")
        lines.append(f"Interpreter:  Dyalog APL {self.major_version}.{self.minor_version} "
                     f"{self.edition} {self.bits}-bit, build {self.svn_revision}")
        lines.append(f"Serial:       {self.serial}")
        lines.append(f"Created:      {self.created}")
        lines.append(f"Saved:        {self.header.timestamp}")
        lines.append(f"")
        lines.append(f"Exception:    {self.exception_description()}")
        lines.append(f"Address:      0x{self.exception_address:016x}")
        lines.append(f"Syserror:     {self.syserror_msg}")
        lines.append(f"")
        
        # Registers
        key_regs = ['Rax','Rbx','Rcx','Rdx','Rsp','Rbp','Rsi','Rdi','Rip',
                     'R8','R9','R10','R11','R12','R13','R14','R15']
        lines.append("Registers:")
        for reg in key_regs:
            if reg in self.registers:
                lines.append(f"  {reg:4s} = 0x{self.registers[reg]:016x}")
        
        # C Stack
        lines.append(f"\nC Stack ({len(self.c_stack)} frames):")
        for frame in self.c_stack:
            lines.append(f"  0x{frame.address:016x}")
        
        # APL Stack by thread
        lines.append(f"\nAPL Stack ({len(self.apl_stack)} frames, "
                     f"{len(set(f.tid for f in self.apl_stack))} threads):")
        current_tid = None
        for frame in self.apl_stack:
            if frame.tid != current_tid:
                current_tid = frame.tid
                lines.append(f"\n  --- Thread {frame.tid} ---")
            lines.append(f"  {frame.namespace}.{frame.function}[{frame.line}]"
                        f"  {frame.source[:80]}")
        
        return '\n'.join(lines)


# ---------------------------------------------------------------------------
# Wsdump text parser
# ---------------------------------------------------------------------------

# Regex patterns for parsing wsdump output
RE_OBJECT_HEADER = re.compile(
    r'^([0-9a-f]{16})\s+([0-9a-f]{16})\s+([0-9a-f]{16})\s+([0-9a-f]{16})\s*$'
)
RE_TYPE_LINE = re.compile(
    r'^(\w+)\s+(\d+)\s+([0-9a-f]{16})\s*(.*?)\s*$'
)
RE_FIELD_LINE = re.compile(
    r'^\s+([0-9a-f]{4})\s+([0-9a-f]{16}(?:\s+[0-9a-f]{16})*)\s*$'
)
RE_LABEL_LINE = re.compile(
    r'^\s+(\S.*?)\s*$'
)


class ObjectIndex:
    """Maps virtual addresses to file offsets for random access."""
    
    def __init__(self):
        self.entries: Dict[int, int] = {}  # vaddr → file_offset
        self.type_index: Dict[str, List[int]] = {}  # typename → [vaddr, ...]
        self.symbol_names: Dict[int, str] = {}  # vaddr → decoded name (populated lazily)
    
    def add(self, vaddr: int, file_offset: int, typename: str):
        self.entries[vaddr] = file_offset
        self.type_index.setdefault(typename, []).append(vaddr)
    
    def __len__(self):
        return len(self.entries)
    
    def __contains__(self, vaddr: int):
        return vaddr in self.entries


class WsDump:
    """Parser for wsdump text output files."""
    
    def __init__(self, filepath: str):
        self.filepath = filepath
        self.filesize = os.path.getsize(filepath)
        self.header = WsHeader()
        self.index = ObjectIndex()
        self.trailer_offset: int = 0  # file offset where trailer (! lines) begins
        self._header_parsed = False
        self._indexed = False
        
        # Parse header immediately (it's always at the start)
        self._parse_header()
    
    def _parse_header(self):
        """Parse the file header (first few lines)."""
        with open(self.filepath, 'r', encoding='utf-8', errors='replace') as f:
            for _ in range(10):
                line = f.readline().rstrip()
                if line.startswith('magic no.'):
                    m = re.match(r'magic no\. (\w+) WSversion ([\d.]+) APLversion ([\d.]+)', line)
                    if m:
                        self.header.magic = m.group(1)
                        self.header.ws_version = m.group(2)
                        self.header.apl_version = m.group(3)
                elif ' saved ' in line:
                    parts = line.split(' saved ', 1)
                    self.header.filename = parts[0].strip()
                    self.header.timestamp = parts[1].strip()
                elif line.startswith('svn revision'):
                    self.header.svn_revision = int(line.split()[-1])
                elif line.startswith('not coded'):
                    self.header.not_coded = line.split()[-1]
        self._header_parsed = True
    
    def build_index(self, progress_callback=None):
        """Pass 1: Scan the entire file to build the address→offset index.
        
        Uses binary mode for accurate byte offsets (text mode on Windows
        translates \\r\\n → \\n, making len(line) undercount bytes).
        
        Args:
            progress_callback: Optional callable(bytes_read, total_bytes) for progress.
        """
        count = 0
        with open(self.filepath, 'rb') as f:
            while True:
                offset = f.tell()
                raw = f.readline()
                if not raw:
                    break
                
                line = raw.decode('utf-8', errors='replace').rstrip()
                
                # Check for object header line
                m = RE_OBJECT_HEADER.match(line)
                if m:
                    vaddr = int(m.group(1), 16)
                    # Read next line for type info
                    next_raw = f.readline()
                    next_line = next_raw.decode('utf-8', errors='replace').rstrip()
                    
                    tm = RE_TYPE_LINE.match(next_line)
                    if tm:
                        typename = tm.group(1)
                        self.index.add(vaddr, offset, typename)
                        count += 1
                    continue
                
                # Check for trailer start
                if line.startswith('!') and self.trailer_offset == 0 and offset > 1000:
                    self.trailer_offset = offset
                
                if progress_callback and count % 100000 == 0:
                    progress_callback(offset, self.filesize)
        
        self._indexed = True
        return count
    
    def get_object(self, address) -> Optional[WsObject]:
        """Retrieve and parse a single object by virtual address.
        
        Args:
            address: Virtual address as int or hex string.
        """
        if isinstance(address, str):
            address = int(address, 16)
        
        if address not in self.index.entries:
            return None
        
        file_offset = self.index.entries[address]
        return self._parse_object_at(file_offset)
    
    def _parse_object_at(self, file_offset: int) -> Optional[WsObject]:
        """Parse a single object starting at the given file offset."""
        with open(self.filepath, 'rb') as f:
            f.seek(file_offset)
            
            # Read header line
            header_line = f.readline().decode('utf-8', errors='replace').rstrip()
            m = RE_OBJECT_HEADER.match(header_line)
            if not m:
                return None
            
            vaddr = int(m.group(1), 16)
            neg_size = int(m.group(2), 16)
            tally = int(m.group(3), 16)
            type_flags = int(m.group(4), 16)
            
            # Convert neg_size to signed
            if neg_size > 0x7FFFFFFFFFFFFFFF:
                neg_size = neg_size - 0x10000000000000000
            
            # Read type line
            type_line = f.readline().decode('utf-8', errors='replace').rstrip()
            tm = RE_TYPE_LINE.match(type_line)
            if not tm:
                return None
            
            typename = tm.group(1)
            abs_size = int(tm.group(2))
            slot_addr = int(tm.group(3), 16)
            rest = tm.group(4).strip()
            
            # Parse tag and extra from rest
            tag = ""
            extra = ""
            if rest:
                parts = rest.split()
                if parts:
                    tag = parts[0]
                if len(parts) > 1:
                    extra = ' '.join(parts[1:])
            
            obj = WsObject(
                address=vaddr,
                neg_size=neg_size,
                tally=tally,
                type_flags=type_flags,
                typename=typename,
                abs_size=abs_size,
                slot_addr=slot_addr,
                tag=tag,
                extra=extra,
                file_offset=file_offset
            )
            
            # Read field lines until we hit the next object or EOF
            pending_values = None
            pending_offset = 0
            
            while True:
                raw = f.readline()
                if not raw:
                    break
                stripped = raw.decode('utf-8', errors='replace').rstrip()
                
                # Next object header? Stop.
                if RE_OBJECT_HEADER.match(stripped):
                    break
                
                # Trailer? Stop.
                if stripped.startswith('!'):
                    break
                
                # Field value line (starts with whitespace + 4-hex offset + hex values)
                fm = RE_FIELD_LINE.match(stripped)
                if fm:
                    if pending_values is not None:
                        # Save previous field without labels
                        obj.fields.append(WsField(
                            offset=pending_offset,
                            values=pending_values,
                            labels=[]
                        ))
                    
                    pending_offset = int(fm.group(1), 16)
                    hex_vals = fm.group(2).split()
                    pending_values = [int(v, 16) for v in hex_vals]
                    continue
                
                # Label line (starts with whitespace, contains *labels or names)
                if stripped and stripped[0] == ' ' and pending_values is not None:
                    # Parse labels — space-separated, may contain *prefix.
                    # Single '.' tokens are visual column separators in the wsdump
                    # format (e.g. '. *value..') and must NOT be counted as labels;
                    # they would shift all subsequent label→value index mappings.
                    labels = [t for t in stripped.split() if t != '.']
                    obj.fields.append(WsField(
                        offset=pending_offset,
                        values=pending_values,
                        labels=labels
                    ))
                    pending_values = None
                    continue
            
            # Flush any pending field
            if pending_values is not None:
                obj.fields.append(WsField(
                    offset=pending_offset,
                    values=pending_values,
                    labels=[]
                ))
            
            return obj
    
    def iter_objects(self, typename: str = None) -> Iterator[WsObject]:
        """Iterate over all objects of a given type (or all types).
        
        Warning: This reads the entire file sequentially. For large files,
        prefer using the index + get_object() for targeted access.
        """
        if not self._indexed:
            raise RuntimeError("Call build_index() first")
        
        addrs = (self.index.type_index.get(typename, []) if typename 
                 else sorted(self.index.entries.keys()))
        
        for addr in addrs:
            obj = self.get_object(addr)
            if obj:
                yield obj
    
    def get_threads(self) -> List[WsObject]:
        """Get all THREAD stack objects."""
        if not self._indexed:
            raise RuntimeError("Call build_index() first")
        
        threads = []
        for addr in self.index.type_index.get('stack', []):
            obj = self.get_object(addr)
            if obj and obj.tag == 'THREAD':
                threads.append(obj)
        return threads
    
    def parse_trailer(self) -> Tuple[List[APLStackFrame], List[AddressMapping]]:
        """Parse the trailer section (! lines at end of file)."""
        apl_stack = []
        addr_space = []
        
        if self.trailer_offset == 0:
            # Scan from the end
            read_size = min(200000, self.filesize)
            with open(self.filepath, 'rb') as f:
                f.seek(max(0, self.filesize - read_size))
                f.readline()  # skip partial line
                for raw in f:
                    stripped = raw.decode('utf-8', errors='replace').rstrip()
                    if stripped.startswith('!') and self.trailer_offset == 0:
                        self.trailer_offset = f.tell() - len(raw)
                    self._parse_trailer_line(stripped, apl_stack, addr_space)
        else:
            with open(self.filepath, 'rb') as f:
                f.seek(self.trailer_offset)
                for raw in f:
                    stripped = raw.decode('utf-8', errors='replace').rstrip()
                    self._parse_trailer_line(stripped, apl_stack, addr_space)
        
        return apl_stack, addr_space
    
    def _parse_trailer_line(self, stripped, apl_stack, addr_space):
        """Parse a single trailer line."""
        if stripped.startswith('!APLStack:') and 'END' not in stripped:
            m = re.match(r'!APLStack: !TID:(\d+)!([^[]+)\[(\d+)\]\s*(.*?)!?\s*$', stripped)
            if m:
                ns_func = m.group(2)
                ns = ns_func.rsplit('.', 1)[0] if '.' in ns_func else '#'
                func = ns_func.rsplit('.', 1)[-1] if '.' in ns_func else ns_func
                apl_stack.append(APLStackFrame(
                    tid=int(m.group(1)), namespace=ns, function=func,
                    line=int(m.group(3)), source=m.group(4).rstrip('!').strip()
                ))
        elif stripped.startswith('!AddressSpace:0x'):
            parts = stripped.replace('!AddressSpace:', '').split()
            if len(parts) >= 7:
                try:
                    addr_space.append(AddressMapping(
                        base_addr=int(parts[0], 16),
                        alloc_base=int(parts[1], 16),
                        alloc_protect=int(parts[2], 16),
                        region_size=int(parts[3], 16),
                        state=int(parts[4], 16),
                        protect=int(parts[5], 16),
                        mem_type=int(parts[6], 16),
                        dll_name=parts[7].strip() if len(parts) > 7 else ""
                    ))
                except (ValueError, IndexError):
                    pass
    
    def stats(self) -> Dict[str, int]:
        """Return object count by type."""
        return {typename: len(addrs) 
                for typename, addrs in self.index.type_index.items()}


# ---------------------------------------------------------------------------
# CLI interface
# ---------------------------------------------------------------------------

def main():
    """Command-line interface for the parser."""
    import argparse
    import time
    
    parser = argparse.ArgumentParser(description='Parse wsdump output files')
    parser.add_argument('file', help='Wsdump output file (.txt) or -i output')
    parser.add_argument('--info', '-i', action='store_true',
                       help='Parse as -i (crash info) output')
    parser.add_argument('--index', action='store_true',
                       help='Build index and show stats')
    parser.add_argument('--object', '-o', metavar='ADDR',
                       help='Show object at address (hex)')
    parser.add_argument('--threads', action='store_true',
                       help='List all THREAD objects')
    parser.add_argument('--trailer', action='store_true',
                       help='Parse and show trailer (APLStack)')
    
    args = parser.parse_args()
    
    if args.info:
        info = CrashInfo.from_file(args.file)
        print(info.summary())
        return
    
    dump = WsDump(args.file)
    print(f"Header: magic={dump.header.magic} WS={dump.header.ws_version} "
          f"APL={dump.header.apl_version}")
    print(f"File: {dump.header.filename}")
    print(f"Date: {dump.header.timestamp}")
    print(f"SVN:  {dump.header.svn_revision}")
    
    if args.index or args.object or args.threads:
        print(f"\nBuilding index ({dump.filesize / 1024 / 1024:.0f} MB)...")
        t0 = time.time()
        
        def progress(read, total):
            pct = read * 100 / total
            print(f"\r  {pct:.1f}% ({read/1024/1024:.0f}/{total/1024/1024:.0f} MB)", 
                  end='', flush=True)
        
        count = dump.build_index(progress_callback=progress)
        elapsed = time.time() - t0
        print(f"\r  Done: {count:,} objects indexed in {elapsed:.1f}s")
        
        print("\nObject counts:")
        for typename, cnt in sorted(dump.stats().items(), key=lambda x: -x[1]):
            print(f"  {typename:12s}: {cnt:>10,}")
    
    if args.object:
        addr = int(args.object, 16)
        obj = dump.get_object(addr)
        if obj:
            print(f"\n=== Object at 0x{addr:016x} ===")
            print(f"Type:       {obj.typename}")
            print(f"Size:       {obj.abs_size} ({obj.size_bytes} bytes)")
            print(f"Tally:      {obj.tally}")
            print(f"TypeFlags:  0x{obj.type_flags:016x}")
            if obj.tag:
                print(f"Tag:        {obj.tag}")
            if obj.extra:
                print(f"Extra:      {obj.extra}")
            if obj.name:
                print(f"Name:       {obj.name}")
            print(f"Fields ({len(obj.fields)}):")
            for f in obj.fields:
                vals = ' '.join(f'0x{v:016x}' for v in f.values)
                labels = ' '.join(f.labels) if f.labels else ''
                print(f"  +0x{f.offset:04x}: {vals}")
                if labels:
                    print(f"           {labels}")
        else:
            print(f"Object not found at 0x{addr:016x}")
    
    if args.threads:
        threads = dump.get_threads()
        print(f"\nTHREAD objects ({len(threads)}):")
        for t in threads:
            tid = t.get_field('tid')
            tid_str = f"TID:{tid}" if tid is not None else f"size:{t.abs_size}"
            link = t.get_pointer('LINK')
            print(f"  0x{t.address:016x} {tid_str} link→0x{link:016x}" if link else
                  f"  0x{t.address:016x} {tid_str} (head)")
    
    if args.trailer:
        apl_stack, addr_space = dump.parse_trailer()
        print(f"\nAPL Stack ({len(apl_stack)} frames):")
        current_tid = None
        for frame in apl_stack:
            if frame.tid != current_tid:
                current_tid = frame.tid
                print(f"\n  --- Thread {frame.tid} ---")
            print(f"  {frame.namespace}.{frame.function}[{frame.line}] {frame.source[:70]}")
        
        # DLLs
        dlls = [a for a in addr_space if a.dll_name]
        print(f"\nLoaded DLLs ({len(dlls)}):")
        for a in dlls:
            print(f"  0x{a.base_addr:016x} {a.dll_name}")


if __name__ == '__main__':
    main()
