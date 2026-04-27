"""
crash_analyzer.py — Crash report generator for Dyalog APL aplcores.

Combines wsdump -i crash info with full workspace dump to produce
a comprehensive crash report: thread states, call stacks with locals,
variable values, and diagnosis.

Usage:
    python crash_analyzer.py aplcore_10.txt                    # full dump only
    python crash_analyzer.py aplcore_10.txt -i wsdump_i.txt    # with crash info
    python crash_analyzer.py aplcore_10.txt --json report.json  # JSON output
"""

import sys
import os
import json
import time
import argparse
from dataclasses import dataclass, field, asdict
from typing import Optional, Dict, List, Tuple, Any

# Add tools dir to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wsdump_parser import (
    WsDump, WsObject, CrashInfo, APLStackFrame,
    decode_symbol_name, decode_environ_functn
)


# ---------------------------------------------------------------------------
# Data model for crash report
# ---------------------------------------------------------------------------

@dataclass
class LocalVariable:
    """A local variable in a stack frame."""
    name: str
    symbol_addr: int
    value_addr: int = 0
    value_type: str = ""
    value_repr: str = ""   # human-readable value or '(was <type>)' for saved-value


@dataclass
class StackFrame:
    """One frame in the APL state indicator."""
    address: int
    frame_type: str       # DFN, LNS, NSR, FLO, SHADOW, GUI, etc.
    function_name: str = ""
    function_addr: int = 0
    namespace: str = "#"
    line: int = -1
    pc: int = 0
    locals: List[LocalVariable] = field(default_factory=list)
    # Raw frame fields for deep inspection
    raw_fields: Dict[str, int] = field(default_factory=dict)


@dataclass
class ThreadState:
    """State of one green thread at crash time."""
    tid: int
    address: int          # address of the per-thread descriptor
    pc: int = 0
    retn: int = 0
    rflags: int = 0
    modes: int = 0
    func_addr: int = 0
    func_name: str = ""
    code_addr: int = 0
    name_addr: int = 0
    dmx_addr: int = 0
    si_frames: List[StackFrame] = field(default_factory=list)
    # From trailer
    trailer_frames: List[APLStackFrame] = field(default_factory=list)


@dataclass 
class CrashReport:
    """Complete crash analysis report."""
    # Header
    ws_version: str = ""
    apl_version: str = ""
    wsid: str = ""
    timestamp: str = ""
    
    # Crash details (from -i if available)
    exception_code: int = 0
    exception_desc: str = ""
    exception_address: int = 0
    fault_address: int = 0
    syserror: str = ""
    
    # Threads
    threads: List[ThreadState] = field(default_factory=list)
    thread_count: int = 0
    
    # Object statistics
    object_counts: Dict[str, int] = field(default_factory=dict)
    
    # Diagnosis
    diagnosis: str = ""
    
    analysis_time: float = 0.0


# ---------------------------------------------------------------------------
# Crash Analyzer
# ---------------------------------------------------------------------------

class CrashAnalyzer:
    """Analyzes an aplcore workspace dump to produce a crash report."""
    
    def __init__(self, dump_path: str, info_path: str = None, verbose: bool = False):
        self.dump_path = dump_path
        self.info_path = info_path
        self.verbose = verbose
        self.ws: Optional[WsDump] = None
        self.crash_info: Optional[CrashInfo] = None
        self.report = CrashReport()
    
    def log(self, msg: str):
        if self.verbose:
            print(f"  [{time.time():.1f}] {msg}", file=sys.stderr)
    
    def analyze(self) -> CrashReport:
        """Run full analysis and return the report."""
        t0 = time.time()
        
        # Phase 1: Parse crash info if available
        if self.info_path:
            self.log("Parsing crash info...")
            self.crash_info = CrashInfo.from_file(self.info_path)
            self._extract_crash_info()
        
        # Phase 2: Build index
        self.log("Building workspace index...")
        self.ws = WsDump(self.dump_path)
        self.ws.build_index()
        self.report.object_counts = self.ws.stats()
        self.log(f"Indexed {len(self.ws.index)} objects")
        
        # Phase 3: Extract header info
        self._extract_header()
        
        # Phase 4: Walk thread chain
        self.log("Walking thread chain...")
        self._walk_threads()
        
        # Phase 5: Walk SI stacks
        self.log("Walking SI stacks...")
        self._walk_si_stacks()
        
        # Phase 6: Match with trailer
        self.log("Parsing trailer...")
        self._match_trailer()
        
        # Phase 6b: Correlate trailer function names onto MODE/DFN SI frames
        self._correlate_trailer_names()
        
        # Phase 7: Diagnose
        self._diagnose()
        
        self.report.analysis_time = time.time() - t0
        self.log(f"Analysis complete in {self.report.analysis_time:.1f}s")
        return self.report
    
    def _extract_crash_info(self):
        """Extract crash details from -i output."""
        ci = self.crash_info
        self.report.exception_code = ci.exception_code
        self.report.exception_desc = ci.exception_description()
        self.report.exception_address = ci.exception_address
        self.report.syserror = ci.syserror_msg
        
        # Extract fault address from exception params
        if len(ci.exception_params) >= 2:
            self.report.fault_address = ci.exception_params[1]
        
        # WSID from crash info
        self.report.wsid = ci.wsid or ci.ws_filename or ''
    
    def _extract_header(self):
        """Extract header info from the dump."""
        h = self.ws.header
        self.report.ws_version = h.ws_version
        self.report.apl_version = h.apl_version
        self.report.timestamp = h.timestamp
        if not self.report.wsid:
            self.report.wsid = h.filename
    
    def _walk_threads(self):
        """Follow the THREAD chain from root to enumerate all green threads."""
        root = self.ws.get_object(0x06cbfc00)
        if not root:
            # Try finding root from index
            root_addrs = self.ws.index.type_index.get('root', [])
            if root_addrs:
                root = self.ws.get_object(root_addrs[0])
        
        if not root:
            self.log("WARNING: Root object not found")
            return
        
        thread_head_addr = root.get_pointer('THREAD')
        if not thread_head_addr:
            self.log("WARNING: THREAD pointer not found in root")
            return
        
        thread_head = self.ws.get_object(thread_head_addr)
        if not thread_head:
            self.log("WARNING: THREAD head object not found")
            return
        
        self.log(f"THREAD head at {thread_head_addr:#x}, tag={thread_head.tag}")
        
        # The THREAD head has a BEANPOT pointer which leads to per-thread descriptors
        # Method 1: Find all stack THREAD objects with size=44 from the index
        thread_descriptors = []
        for addr in self.ws.index.type_index.get('stack', []):
            # Quick check: read from index — we need to peek at the file
            obj = self.ws.get_object(addr)
            if obj and obj.tag == 'THREAD' and obj.abs_size == 44:
                thread_descriptors.append(obj)
        
        self.log(f"Found {len(thread_descriptors)} thread descriptors")
        
        for desc in thread_descriptors:
            ts = self._parse_thread_descriptor(desc)
            if ts:
                self.report.threads.append(ts)
        
        self.report.thread_count = len(self.report.threads)
        self.report.threads.sort(key=lambda t: t.tid)
    
    def _parse_thread_descriptor(self, obj: WsObject) -> Optional[ThreadState]:
        """Parse a per-thread THREAD descriptor (size 44) into a ThreadState."""
        fields = obj.named_fields_dict
        
        tid = fields.get('tid', 0)
        ts = ThreadState(
            tid=tid,
            address=obj.address,
            pc=fields.get('pc', 0),
            retn=fields.get('retn', 0),
            rflags=fields.get('rflags', 0),
            modes=fields.get('modes', 0),
            func_addr=fields.get('*func', 0),
            code_addr=fields.get('*code', 0),
            name_addr=fields.get('*name', 0),
            dmx_addr=fields.get('*dmx', 0),
        )
        
        # Resolve function name
        if ts.func_addr:
            func_obj = self.ws.get_object(ts.func_addr)
            if func_obj and func_obj.typename == 'symbol':
                ts.func_name = func_obj.name or f'@{ts.func_addr:#x}'
        
        # Resolve name
        if ts.name_addr and not ts.func_name:
            name_obj = self.ws.get_object(ts.name_addr)
            if name_obj and name_obj.typename == 'symbol':
                ts.func_name = name_obj.name or ts.func_name
        
        return ts
    
    def _walk_si_stacks(self):
        """Walk the SI stack for each thread.
        
        TID:0 uses root.*SIstack. Other threads use their descriptor's *LINK.
        """
        root = self.ws.get_object(self.ws.index.type_index.get('root', [0])[0])
        if not root:
            return
        
        for ts in self.report.threads:
            if ts.tid == 0:
                # Main thread: SI from root
                si_addr = root.get_pointer('SIstack')
                if si_addr:
                    self.log(f"Root SIstack at {si_addr:#x}")
                    ts.si_frames = self._walk_si_chain(si_addr, limit=300)
                    self.log(f"  TID:0 has {len(ts.si_frames)} SI frames")
            else:
                # Other threads: SI from descriptor's *LINK
                desc = self.ws.get_object(ts.address)
                if desc:
                    link_addr = desc.get_pointer('LINK')
                    if link_addr:
                        ts.si_frames = self._walk_si_chain(link_addr, limit=200)
                        self.log(f"  TID:{ts.tid} has {len(ts.si_frames)} SI frames")
    
    def _walk_si_chain(self, start_addr: int, limit: int = 200) -> List[StackFrame]:
        """Walk a chain of stack objects following *LINK pointers."""
        frames = []
        addr = start_addr
        seen = set()
        
        while addr and addr not in seen and len(frames) < limit:
            seen.add(addr)
            obj = self.ws.get_object(addr)
            if not obj:
                break
            
            if obj.typename != 'stack':
                # Might be a pointer object; try following it
                if obj.typename == 'pointer':
                    # Follow pointer's target
                    for f in obj.fields:
                        for v in f.values:
                            if v and v in self.ws.index:
                                addr = v
                                continue
                break
            
            frame = StackFrame(
                address=obj.address,
                frame_type=obj.tag,
                raw_fields=obj.named_fields_dict
            )
            
            # Extract function name
            func_addr = obj.get_pointer('func')
            if func_addr:
                frame.function_addr = func_addr
                func_obj = self.ws.get_object(func_addr)
                if func_obj and func_obj.typename == 'symbol':
                    frame.function_name = func_obj.name or f'@{func_addr:#x}'
            
            # ENVIRON frames: *functn/*fname are small integer opcodes, not addresses.
            # Decode them to the APL primitive name (⎕NA, ⍎, ...).
            if obj.tag == 'ENVIRON' and not frame.function_name:
                fields_env = obj.named_fields_dict
                functn = fields_env.get('*functn', 0)
                fname  = fields_env.get('*fname',  0)
                frame.function_name = decode_environ_functn(functn, fname)
            
            # Extract name (for named frames)
            name_addr = obj.get_pointer('name')
            if name_addr and not frame.function_name:
                name_obj = self.ws.get_object(name_addr)
                if name_obj and name_obj.typename == 'symbol':
                    frame.function_name = name_obj.name or ''
            
            # Extract line/pc info
            fields = obj.named_fields_dict
            frame.line = fields.get('line', fields.get('pc', -1))
            frame.pc = fields.get('pc', 0)
            
            # Extract locals from SHADOW or FLO frames
            if obj.tag in ('SHADOW', 'FLO', 'DFN'):
                frame.locals = self._extract_locals(obj)
            
            frames.append(frame)
            
            # Follow LINK
            link_addr = obj.get_pointer('LINK')
            if link_addr and link_addr != addr:
                addr = link_addr
            else:
                break
        
        return frames
    
    def _extract_locals(self, obj: WsObject) -> List[LocalVariable]:
        """Extract local variables from a SHADOW frame.
        
        SHADOW frames store count entries, each with three consecutive fields:
          class...  *name...  *value..
        where *name... → symbol (the variable name) and *value.. → the saved
        value (what was there before localization, to be restored on return).
        The symbol's own *value field holds the current local value if set.
        """
        locals_list = []
        
        if obj.tag == 'SHADOW':
            for f in obj.fields:
                name_idx = next((i for i, l in enumerate(f.labels) if l == '*name...'), None)
                val_idx  = next((i for i, l in enumerate(f.labels) if l == '*value..'), None)
                if name_idx is None or name_idx >= len(f.values) or not f.values[name_idx]:
                    continue
                sym = self.ws.get_object(f.values[name_idx])
                if not (sym and sym.typename == 'symbol'):
                    continue
                name = sym.name or f'@{f.values[name_idx]:#x}'
                
                # Current value: from the symbol's own *value pointer
                cur_val_addr = sym.get_pointer('value') or 0
                # Saved value: from the SHADOW frame's *value.. field
                saved_addr = (f.values[val_idx]
                              if val_idx is not None and val_idx < len(f.values)
                              else 0)
                
                lv = LocalVariable(name=name, symbol_addr=f.values[name_idx],
                                   value_addr=cur_val_addr)
                if cur_val_addr:
                    cur_obj = self.ws.get_object(cur_val_addr)
                    if cur_obj:
                        lv.value_type = cur_obj.typename
                        lv.value_repr = self._decode_value(cur_obj)
                elif saved_addr:
                    # Current value not directly accessible; show saved value as context
                    saved_obj = self.ws.get_object(saved_addr)
                    if saved_obj and saved_obj.typename not in ('stack',):
                        lv.value_repr = f'(was {self._decode_value(saved_obj)})'
                    elif saved_obj:
                        lv.value_repr = f'(was <{saved_obj.typename} {saved_obj.tag}>)'
                locals_list.append(lv)
        
        return locals_list
    
    def _decode_value(self, obj: WsObject) -> str:
        """Return a compact human-readable representation of a workspace object."""
        if obj.typename == 'simple':
            if obj.tally == 0:
                return '⍬'
            # Check type_flags low byte for character vs numeric
            # Observed: tf=0x220f seems to be a numeric array type
            is_char = bool(obj.type_flags & 0x40)  # bit 6 = character flag (empirical)
            if obj.tally == 1:
                # Scalar: try to read the value from the first data field
                for f in obj.fields:
                    if f.values:
                        v = f.values[0]
                        if is_char:
                            try:
                                ch = chr(v & 0xFFFF)
                                return repr(ch)
                            except Exception:
                                pass
                        else:
                            # Treat as signed 64-bit int
                            if v > 0x7FFFFFFFFFFFFFFF:
                                v -= 0x10000000000000000
                            return str(v)
                return '(scalar)'
            else:
                kind = 'char' if is_char else 'num'
                return f'({obj.tally}-elem {kind})'
        elif obj.typename == 'pointer':
            return f'(nested {obj.tally})'
        elif obj.typename == 'stack':
            return f'<{obj.tag}>'
        elif obj.typename == 'symbol':
            return f'<ref:{obj.name or "?"}>'
        elif obj.typename in ('defunct', 'fptr'):
            return '<function>'
        elif obj.typename == 'derv':
            return '<derived fn>'
        else:
            return f'<{obj.typename}>'

    def _correlate_trailer_names(self):
        """Map trailer function names onto MODE/DFN SI frames.
        
        MODE and DFN frames in the SI chain correspond in order to the trailer's
        APLStack entries. ENVIRON frames do NOT appear in the trailer and are
        skipped in the correlation. SHADOW/LNS/DIAMOND frames are also skipped.
        """
        for ts in self.report.threads:
            if not ts.trailer_frames:
                continue
            trailer_idx = 0
            for frame in ts.si_frames:
                if frame.frame_type not in ('MODE', 'DFN'):
                    continue
                if trailer_idx < len(ts.trailer_frames) and not frame.function_name:
                    tf = ts.trailer_frames[trailer_idx]
                    frame.function_name = tf.function
                    frame.namespace = tf.namespace
                    if frame.line < 0:
                        frame.line = tf.line
                trailer_idx += 1
    
    def _match_trailer(self):
        """Match trailer APLStack frames to threads."""
        apl_stack, _ = self.ws.parse_trailer()
        
        # Group by thread
        by_tid = {}
        for frame in apl_stack:
            by_tid.setdefault(frame.tid, []).append(frame)
        
        for ts in self.report.threads:
            ts.trailer_frames = by_tid.get(ts.tid, [])
    
    def _diagnose(self):
        """Generate a human-readable diagnosis."""
        lines = []
        
        if self.report.exception_desc:
            lines.append(f"Exception: {self.report.exception_desc}")
        if self.report.fault_address:
            lines.append(f"Fault address: 0x{self.report.fault_address:x}")
            if self.report.fault_address < 0x10000:
                lines.append("  → Near-null dereference (likely NULL pointer + field offset)")
        
        if self.report.threads:
            active = [t for t in self.report.threads 
                      if t.trailer_frames or t.si_frames]
            lines.append(f"\n{self.report.thread_count} green threads, "
                        f"{len(active)} with call stacks")
            
            for ts in self.report.threads:
                status = ""
                if ts.trailer_frames:
                    top = ts.trailer_frames[0]
                    status = f"{top.namespace}.{top.function}[{top.line}]"
                elif ts.func_name:
                    status = ts.func_name
                lines.append(f"  TID:{ts.tid} — {status or '(idle)'}")
        
        self.report.diagnosis = '\n'.join(lines)


# ---------------------------------------------------------------------------
# Report formatters
# ---------------------------------------------------------------------------

def _format_si_frames(frames: list, prefix: str = "  │") -> list:
    """Format SI frames as lines, grouping SHADOW locals under their MODE/DFN.
    
    Returns a list of strings (without trailing newlines).
    SHADOW frames are merged into their preceding MODE/DFN as a local variable
    list rather than shown as separate SI entries.
    
    Legend in output:
      ∇  = tradfn (MODE frame)
      ⊢  = dfn (DFN frame)
      ⎕  = system primitive (ENVIRON frame: ⎕NA, ⍎, ...)
      ⌸  = mid-execution ⎕SHADOW (SHADOW with no preceding MODE/DFN)
      ═  = thread boundary (THREAD frame)
    """
    lines = []
    fn_line_holder = [None]
    nonlocal_pending = []

    def make_fn_line(frame):
        fn = frame.function_name or ''
        ns = frame.namespace or '#'
        ln = f"[{frame.line}]" if frame.line >= 0 else ""
        ftype = frame.frame_type
        if ftype == 'ENVIRON':
            return f"{prefix}   ⎕  {fn}"
        elif ftype == 'DFN':
            label = f"{ns}.{fn}" if fn else 'DFN'
            return f"{prefix}   ⊢  {label}{ln}"
        else:  # MODE
            label = f"{ns}.{fn}" if fn else 'MODE'
            return f"{prefix}   ∇  {label}{ln}"

    def do_flush():
        if fn_line_holder[0] is not None:
            lines.append(fn_line_holder[0])
            for loc in nonlocal_pending:
                val = f"  {loc.value_repr}" if loc.value_repr else ""
                lines.append(f"{prefix}       {loc.name:<22}{val}")
            nonlocal_pending.clear()
            fn_line_holder[0] = None

    for frame in frames:
        if frame.frame_type == 'SHADOW':
            if fn_line_holder[0] is not None:
                nonlocal_pending.extend(frame.locals)
            else:
                # Mid-execution ⎕SHADOW (no parent MODE/DFN yet in chain)
                for loc in frame.locals:
                    val = f"  {loc.value_repr}" if loc.value_repr else ""
                    lines.append(f"{prefix}   ⌸  {loc.name:<22}{val}")
        elif frame.frame_type in ('MODE', 'DFN', 'ENVIRON'):
            do_flush()
            fn_line_holder[0] = make_fn_line(frame)
        elif frame.frame_type == 'THREAD':
            do_flush()
            lines.append(f"{prefix}   ═  (thread boundary)")
            break
        elif frame.frame_type in ('LNS', 'DIAMOND'):
            pass  # Suppress execution-state frames to keep output readable
        else:
            do_flush()
            lines.append(f"{prefix}   {frame.frame_type:6s} {frame.function_name or ''}")

    do_flush()
    return lines


def format_text_report(report: CrashReport) -> str:
    """Format a crash report as human-readable text."""
    lines = []
    lines.append("=" * 72)
    lines.append("  DYALOG APL CRASH ANALYSIS REPORT")
    lines.append("=" * 72)
    
    # Header
    lines.append(f"\nWorkspace:  {report.wsid}")
    lines.append(f"Version:    APL {report.apl_version}, WS format {report.ws_version}")
    lines.append(f"Saved:      {report.timestamp}")
    
    # Exception
    if report.exception_desc:
        lines.append(f"\n--- EXCEPTION ---")
        lines.append(f"Type:       {report.exception_desc}")
        lines.append(f"Address:    0x{report.exception_address:016x}")
        if report.fault_address:
            lines.append(f"Fault:      0x{report.fault_address:x}")
        lines.append(f"Syserror:   {report.syserror}")
    
    # Threads
    lines.append(f"\n--- THREADS ({report.thread_count}) ---")
    for ts in report.threads:
        lines.append(f"\n  ┌─ Thread {ts.tid} {'─' * 50}")
        lines.append(f"  │ Address:  0x{ts.address:016x}")
        if ts.func_name:
            lines.append(f"  │ Function: {ts.func_name}")
        lines.append(f"  │ PC: {ts.pc}  Retn: {ts.retn}  "
                     f"Rflags: 0x{ts.rflags:x}  Modes: 0x{ts.modes:x}")
        
        # Trailer frames (human-readable APL stack)
        if ts.trailer_frames:
            lines.append(f"  │")
            lines.append(f"  │ APL Call Stack ({len(ts.trailer_frames)} frames):")
            for i, f in enumerate(ts.trailer_frames):
                marker = "→" if i == 0 else " "
                lines.append(f"  │  {marker} {f.namespace}.{f.function}[{f.line}]")
        
        # SI frames (detailed workspace objects)
        if ts.si_frames:
            lines.append(f"  │")
            lines.append(f"  │ SI Stack ({len(ts.si_frames)} frames):")
            lines.extend(_format_si_frames(ts.si_frames))
        
        lines.append(f"  └{'─' * 60}")
    
    # Object stats
    lines.append(f"\n--- WORKSPACE STATISTICS ---")
    for typename, count in sorted(report.object_counts.items(),
                                   key=lambda x: -x[1]):
        lines.append(f"  {typename:12s}  {count:>10,}")
    
    # Diagnosis
    if report.diagnosis:
        lines.append(f"\n--- DIAGNOSIS ---")
        lines.append(report.diagnosis)
    
    lines.append(f"\n--- Analysis completed in {report.analysis_time:.1f}s ---")
    return '\n'.join(lines)


def report_to_json(report: CrashReport) -> dict:
    """Convert report to JSON-serializable dict."""
    d = {
        'workspace': {
            'wsid': report.wsid,
            'ws_version': report.ws_version,
            'apl_version': report.apl_version,
            'timestamp': report.timestamp,
        },
        'exception': {
            'code': report.exception_code,
            'description': report.exception_desc,
            'address': f'0x{report.exception_address:016x}',
            'fault_address': f'0x{report.fault_address:x}' if report.fault_address else None,
            'syserror': report.syserror,
        },
        'threads': [],
        'object_counts': report.object_counts,
        'diagnosis': report.diagnosis,
        'analysis_time': report.analysis_time,
    }
    
    for ts in report.threads:
        td = {
            'tid': ts.tid,
            'address': f'0x{ts.address:016x}',
            'pc': ts.pc,
            'retn': ts.retn,
            'func_name': ts.func_name,
            'call_stack': [
                {'namespace': f.namespace, 'function': f.function, 'line': f.line}
                for f in ts.trailer_frames
            ],
            'si_frames': [
                {
                    'type': f.frame_type,
                    'function': f.function_name,
                    'line': f.line,
                    'locals': [
                        {'name': l.name, 'addr': f'0x{l.symbol_addr:x}'}
                        for l in f.locals
                    ]
                }
                for f in ts.si_frames
            ],
        }
        d['threads'].append(td)
    
    return d


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description='Analyze Dyalog APL crash dumps (aplcores)')
    parser.add_argument('dump', help='Wsdump text output file (.txt)')
    parser.add_argument('-i', '--info', metavar='FILE',
                       help='Wsdump -i crash info output file')
    parser.add_argument('--json', metavar='FILE',
                       help='Write JSON report to file')
    parser.add_argument('-v', '--verbose', action='store_true',
                       help='Verbose progress output')
    parser.add_argument('-o', '--output', metavar='FILE',
                       help='Write text report to file (default: stdout)')
    
    args = parser.parse_args()
    
    analyzer = CrashAnalyzer(
        dump_path=args.dump,
        info_path=args.info,
        verbose=args.verbose,
    )
    
    report = analyzer.analyze()
    
    # Text report
    text = format_text_report(report)
    if args.output:
        with open(args.output, 'w', encoding='utf-8') as f:
            f.write(text)
        print(f"Report written to {args.output}", file=sys.stderr)
    else:
        print(text)
    
    # JSON report
    if args.json:
        with open(args.json, 'w', encoding='utf-8') as f:
            json.dump(report_to_json(report), f, indent=2)
        print(f"JSON report written to {args.json}", file=sys.stderr)


if __name__ == '__main__':
    main()
