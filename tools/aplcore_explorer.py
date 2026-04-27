"""
aplcore_explorer.py — Interactive REPL for exploring Dyalog APL crash dumps.

Provides commands to navigate the workspace object graph, inspect threads,
walk call stacks, decode symbol names, and examine variable values.

Usage:
    python aplcore_explorer.py aplcore_10.txt
    python aplcore_explorer.py aplcore_10.txt -i wsdump_i_stdout.txt
"""

import sys
import os
import re
import time
import argparse
try:
    import readline
except ImportError:
    pass  # readline not available on Windows
from typing import Optional, List

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wsdump_parser import (
    WsDump, WsObject, CrashInfo, APLStackFrame,
    decode_symbol_name, ObjectIndex, decode_environ_functn
)
from crash_analyzer import CrashAnalyzer, format_text_report, _format_si_frames


class AplcoreExplorer:
    """Interactive REPL for aplcore exploration."""
    
    HELP = """
Commands:
  info                  Show workspace header and crash summary
  threads               List all green threads with status
  si [TID]              Show SI (state indicator) stack for thread
  locals [TID]          Show local variables for each frame
  inspect ADDR          Inspect object at hex address
  follow ADDR.field     Follow pointer chain (e.g. 06cbfc00.*THREAD.*LINK)
  symbols [PATTERN]     Search symbol names (regex)
  functions [PATTERN]   Search function symbols (regex)
  value ADDR            Show decoded value of a simple object
  stats                 Object count statistics
  root                  Show root object fields
  trailer               Show trailer APL stack
  find TYPE [TAG]       Find objects by type and optional tag
  raw ADDR              Show raw field dump for object
  xrefs ADDR            Find objects pointing to address
  help                  Show this help
  quit                  Exit
"""
    
    def __init__(self, dump_path: str, info_path: str = None):
        self.dump_path = dump_path
        self.info_path = info_path
        self.ws: Optional[WsDump] = None
        self.crash_info: Optional[CrashInfo] = None
        self.report = None
        self._symbol_cache = {}  # addr -> name
    
    def start(self):
        """Initialize and run the REPL."""
        print("Loading aplcore dump...")
        
        if self.info_path:
            print("  Parsing crash info...")
            self.crash_info = CrashInfo.from_file(self.info_path)
        
        print("  Building index (this takes ~35s)...")
        t0 = time.time()
        self.ws = WsDump(self.dump_path)
        self.ws.build_index()
        print(f"  Indexed {len(self.ws.index)} objects in {time.time()-t0:.1f}s")
        
        # Run crash analysis
        print("  Running crash analysis...")
        analyzer = CrashAnalyzer(self.dump_path, self.info_path)
        analyzer.ws = self.ws  # reuse already-built index
        analyzer.crash_info = self.crash_info
        analyzer.report.object_counts = self.ws.stats()
        analyzer._extract_header()
        if self.crash_info:
            analyzer._extract_crash_info()
        analyzer._walk_threads()
        analyzer._walk_si_stacks()
        analyzer._match_trailer()
        analyzer._diagnose()
        self.report = analyzer.report
        
        print(f"  Ready. {self.report.thread_count} threads, "
              f"{len(self.ws.index)} objects.\n")
        print("Type 'help' for commands, 'quit' to exit.\n")
        
        self._repl()
    
    def _repl(self):
        """Main REPL loop."""
        # Setup readline history
        try:
            readline.read_history_file('.aplcore_history')
        except (FileNotFoundError, OSError, NameError):
            pass
        
        while True:
            try:
                line = input("aplcore> ").strip()
            except (EOFError, KeyboardInterrupt):
                print()
                break
            
            if not line:
                continue
            
            # Save history
            try:
                readline.write_history_file('.aplcore_history')
            except (OSError, NameError):
                pass
            
            parts = line.split(None, 1)
            cmd = parts[0].lower()
            arg = parts[1] if len(parts) > 1 else ""
            
            try:
                if cmd in ('quit', 'exit', 'q'):
                    break
                elif cmd == 'help':
                    print(self.HELP)
                elif cmd == 'info':
                    self._cmd_info()
                elif cmd == 'threads':
                    self._cmd_threads()
                elif cmd == 'si':
                    self._cmd_si(arg)
                elif cmd == 'locals':
                    self._cmd_locals(arg)
                elif cmd == 'inspect':
                    self._cmd_inspect(arg)
                elif cmd == 'follow':
                    self._cmd_follow(arg)
                elif cmd == 'symbols':
                    self._cmd_symbols(arg)
                elif cmd == 'functions':
                    self._cmd_functions(arg)
                elif cmd == 'value':
                    self._cmd_value(arg)
                elif cmd == 'stats':
                    self._cmd_stats()
                elif cmd == 'root':
                    self._cmd_root()
                elif cmd == 'trailer':
                    self._cmd_trailer()
                elif cmd == 'find':
                    self._cmd_find(arg)
                elif cmd == 'raw':
                    self._cmd_raw(arg)
                elif cmd == 'xrefs':
                    self._cmd_xrefs(arg)
                else:
                    print(f"Unknown command: {cmd}. Type 'help' for available commands.")
            except Exception as e:
                print(f"Error: {e}")
    
    # --- Commands ---
    
    def _cmd_info(self):
        """Show workspace header and crash summary."""
        r = self.report
        print(f"  WSID:       {r.wsid}")
        print(f"  Version:    APL {r.apl_version}, WS {r.ws_version}")
        print(f"  Saved:      {r.timestamp}")
        if r.exception_desc:
            print(f"  Exception:  {r.exception_desc}")
            print(f"  Address:    0x{r.exception_address:016x}")
            if r.fault_address:
                print(f"  Fault:      0x{r.fault_address:x}")
            print(f"  Syserror:   {r.syserror}")
        print(f"  Threads:    {r.thread_count}")
        print(f"  Objects:    {sum(r.object_counts.values()):,}")
    
    def _cmd_threads(self):
        """List all green threads."""
        for ts in self.report.threads:
            top = ""
            if ts.trailer_frames:
                f = ts.trailer_frames[0]
                top = f"{f.namespace}.{f.function}[{f.line}]"
            elif ts.func_name:
                top = ts.func_name
            si_count = len(ts.si_frames)
            # Show wake status for threads blocked on timed ⎕TGET
            wake_info = ""
            desc = self.ws.get_object(ts.address)
            if desc:
                wake = desc.named_fields_dict.get('wake', 0)
                if wake:
                    wake_info = f" [wake=0x{wake:x}]"
            print(f"  TID:{ts.tid:<5d} {top:<50s} SI:{si_count} frames{wake_info}")
    
    def _cmd_si(self, arg: str):
        """Show SI stack for a thread."""
        tid = int(arg) if arg else 0
        ts = next((t for t in self.report.threads if t.tid == tid), None)
        if not ts:
            print(f"Thread {tid} not found. Available: {[t.tid for t in self.report.threads]}")
            return
        
        # Show thread descriptor wait state
        desc = self.ws.get_object(ts.address)
        if desc:
            fd = desc.named_fields_dict
            wake = fd.get('wake', 0)
            if wake:
                print(f"  [TID:{tid} waiting on ⎕TGET, wake=0x{wake:x} (timed)]")
        
        print(f"  SI Stack for TID:{tid} ({len(ts.si_frames)} frames):")
        if ts.trailer_frames:
            print(f"  APL Call Stack:")
            for i, f in enumerate(ts.trailer_frames):
                marker = "→" if i == 0 else " "
                print(f"    {marker} {f.namespace}.{f.function}[{f.line}]")
            print()
        
        # Render grouped SI using the shared formatter
        print(f"  SI Frames (grouped):")
        for line in _format_si_frames(ts.si_frames, prefix="   "):
            print(line)
    
    def _cmd_locals(self, arg: str):
        """Show locals per frame for a thread."""
        tid = int(arg) if arg else 0
        ts = next((t for t in self.report.threads if t.tid == tid), None)
        if not ts:
            print(f"Thread {tid} not found.")
            return
        
        for frame in ts.si_frames:
            if frame.locals:
                fn = frame.function_name or frame.frame_type
                print(f"  {fn}:")
                for loc in frame.locals:
                    val_info = ""
                    if loc.value_addr:
                        val_obj = self.ws.get_object(loc.value_addr)
                        if val_obj:
                            val_info = f"  ({val_obj.typename} size={val_obj.abs_size})"
                    print(f"    {loc.name:<30s} @{loc.symbol_addr:x}{val_info}")
    
    def _cmd_inspect(self, arg: str):
        """Inspect object at address."""
        if not arg:
            print("Usage: inspect ADDR (hex address)")
            return
        
        addr = int(arg.replace('0x', ''), 16)
        obj = self.ws.get_object(addr)
        if not obj:
            print(f"Object not found at 0x{addr:x}")
            return
        
        print(f"  Address:    0x{obj.address:016x}")
        print(f"  Type:       {obj.typename}")
        print(f"  Tag:        {obj.tag}" if obj.tag else "")
        print(f"  Size:       {obj.abs_size} words ({obj.size_bytes} bytes)")
        print(f"  Tally:      {obj.tally}")
        print(f"  Type/Flags: 0x{obj.type_flags:016x}")
        if obj.typename == 'symbol' and obj.name:
            print(f"  Name:       {obj.name}")
        
        print(f"\n  Fields ({len(obj.fields)}):")
        for f in obj.fields:
            labels_str = ' '.join(f.labels) if f.labels else ''
            for i, v in enumerate(f.values):
                lbl = f.labels[i] if i < len(f.labels) else ''
                ptr_info = ""
                if lbl.startswith('*') and v:
                    target = self.ws.get_object(v)
                    if target:
                        name = ''
                        if target.typename == 'symbol' and target.name:
                            name = f' name={target.name!r}'
                        ptr_info = f"  → {target.typename}"
                        if target.tag:
                            ptr_info += f" {target.tag}"
                        ptr_info += name
                print(f"    {f.offset:04x}+{i*8:02x}  {lbl:16s}  0x{v:016x}{ptr_info}")
    
    def _cmd_follow(self, arg: str):
        """Follow a pointer chain like ADDR.*field1.*field2"""
        if not arg:
            print("Usage: follow ADDR.field1.field2...")
            return
        
        parts = arg.split('.')
        addr = int(parts[0].replace('0x', ''), 16)
        obj = self.ws.get_object(addr)
        if not obj:
            print(f"Object not found at 0x{addr:x}")
            return
        
        path = [f"0x{addr:x} ({obj.typename} {obj.tag})"]
        
        for field_name in parts[1:]:
            if not field_name.startswith('*'):
                field_name = '*' + field_name
            next_addr = obj.get_pointer(field_name[1:])
            if not next_addr:
                print(f"  Field {field_name} is null or not found")
                break
            obj = self.ws.get_object(next_addr)
            if not obj:
                print(f"  {field_name} -> 0x{next_addr:x} (not in index)")
                break
            name = ''
            if obj.typename == 'symbol' and obj.name:
                name = f' name={obj.name!r}'
            path.append(f"{field_name} -> 0x{next_addr:x} ({obj.typename} {obj.tag}{name})")
        
        for p in path:
            print(f"  {p}")
        
        # Show final object
        if obj:
            print(f"\n  Final object:")
            self._cmd_inspect(f"{obj.address:x}")
    
    def _cmd_symbols(self, pattern: str):
        """Search symbol names matching regex pattern."""
        if not pattern:
            pattern = '.'  # match all
        
        regex = re.compile(pattern, re.IGNORECASE)
        matches = []
        
        # Scan symbol objects for names
        count = 0
        for addr in self.ws.index.type_index.get('symbol', []):
            if addr in self._symbol_cache:
                name = self._symbol_cache[addr]
            else:
                obj = self.ws.get_object(addr)
                if obj:
                    name = obj.name
                    if name:
                        self._symbol_cache[addr] = name
                else:
                    continue
            
            if name and regex.search(name):
                obj = self.ws.get_object(addr)
                matches.append((name, addr, obj.tag if obj else ''))
                count += 1
                if count >= 50:
                    break
            
            # Progress for large scans
            count_scanned = len(self._symbol_cache)
            if count_scanned % 10000 == 0 and count_scanned > 0:
                print(f"  ... scanned {count_scanned} symbols", end='\r')
        
        if matches:
            print(f"  Found {len(matches)} matches:")
            for name, addr, tag in sorted(matches):
                print(f"    {name:<40s}  {tag:2s}  @{addr:x}")
        else:
            print("  No matches found.")
    
    def _cmd_functions(self, pattern: str):
        """Search function symbols."""
        if not pattern:
            pattern = '.'
        
        regex = re.compile(pattern, re.IGNORECASE)
        matches = []
        
        for addr in self.ws.index.type_index.get('symbol', []):
            obj = self.ws.get_object(addr)
            if obj and obj.tag == 'F':
                name = obj.name
                if name and regex.search(name):
                    matches.append((name, addr))
                    if len(matches) >= 50:
                        break
        
        if matches:
            print(f"  Found {len(matches)} functions:")
            for name, addr in sorted(matches):
                print(f"    {name:<40s}  @{addr:x}")
        else:
            print("  No matching functions found.")
    
    def _cmd_value(self, arg: str):
        """Show decoded value of a simple object."""
        if not arg:
            print("Usage: value ADDR")
            return
        
        addr = int(arg.replace('0x', ''), 16)
        obj = self.ws.get_object(addr)
        if not obj:
            print(f"Object not found at 0x{addr:x}")
            return
        
        if obj.typename != 'simple':
            print(f"Object is {obj.typename}, not simple. Use 'inspect' instead.")
            return
        
        print(f"  Type:   simple")
        print(f"  Size:   {obj.abs_size} words")
        print(f"  Tally:  {obj.tally}")
        print(f"  Flags:  0x{obj.type_flags:016x}")
        
        # Decode based on type_flags
        tf = obj.type_flags & 0xFFFF
        type_name = {
            0x220f: 'boolean',
            0x210f: 'float64',
            0x271f: 'int64',
            0x280f: 'char8',
            0x281f: 'char16',
            0x2b0f: 'complex',
            0x230f: 'float32',
            0x260f: 'int32',
            0x250f: 'int16',
            0x240f: 'int8',
        }.get(tf, f'unknown(0x{tf:04x})')
        
        print(f"  Dtype:  {type_name}")
        
        # Show raw data
        if obj.fields:
            print(f"  Data:")
            for fld in obj.fields[:10]:
                for v in fld.values:
                    if type_name == 'char16':
                        # Decode as UTF-16LE characters
                        chars = decode_symbol_name([f'{v:016x}'])
                        print(f"    0x{v:016x}  {chars!r}")
                    elif type_name == 'int64':
                        signed = v if v < 0x8000000000000000 else v - 0x10000000000000000
                        print(f"    0x{v:016x}  = {signed}")
                    elif type_name == 'float64':
                        import struct
                        fval = struct.unpack('d', struct.pack('Q', v))[0]
                        print(f"    0x{v:016x}  = {fval}")
                    elif type_name == 'boolean':
                        print(f"    0x{v:016x}  bits")
                    else:
                        print(f"    0x{v:016x}")
    
    def _cmd_stats(self):
        """Show object statistics."""
        total = 0
        for typename, count in sorted(self.report.object_counts.items(),
                                       key=lambda x: -x[1]):
            print(f"  {typename:12s}  {count:>10,}")
            total += count
        print(f"  {'TOTAL':12s}  {total:>10,}")
    
    def _cmd_root(self):
        """Show root object fields."""
        root_addrs = self.ws.index.type_index.get('root', [])
        if not root_addrs:
            print("No root object found.")
            return
        self._cmd_inspect(f"{root_addrs[0]:x}")
    
    def _cmd_trailer(self):
        """Show trailer APL stack."""
        for ts in self.report.threads:
            if ts.trailer_frames:
                print(f"  TID:{ts.tid}")
                for f in ts.trailer_frames:
                    print(f"    {f.namespace}.{f.function}[{f.line}]")
                print()
    
    def _cmd_find(self, arg: str):
        """Find objects by type and optional tag."""
        parts = arg.split()
        typename = parts[0] if parts else ''
        tag = parts[1] if len(parts) > 1 else None
        
        if not typename:
            print("Usage: find TYPE [TAG]")
            print("Types:", ', '.join(sorted(self.ws.index.type_index.keys())))
            return
        
        addrs = self.ws.index.type_index.get(typename, [])
        if not addrs:
            print(f"No objects of type '{typename}'")
            return
        
        matches = []
        for addr in addrs[:500]:
            if tag:
                obj = self.ws.get_object(addr)
                if obj and obj.tag == tag:
                    matches.append((addr, obj))
            else:
                matches.append((addr, None))
        
        print(f"  Found {len(addrs)} {typename} objects" + 
              (f" ({len(matches)} with tag='{tag}')" if tag else "") +
              (f" (showing first {len(matches)})" if len(matches) < len(addrs) else ""))
        
        for addr, obj in matches[:30]:
            if obj:
                name = obj.name if obj.typename == 'symbol' else ''
                print(f"    @{addr:x}  {obj.tag:8s} size={obj.abs_size}  {name}")
            else:
                print(f"    @{addr:x}")
    
    def _cmd_raw(self, arg: str):
        """Show raw field dump for object."""
        if not arg:
            print("Usage: raw ADDR")
            return
        
        addr = int(arg.replace('0x', ''), 16)
        obj = self.ws.get_object(addr)
        if not obj:
            print(f"Object not found at 0x{addr:x}")
            return
        
        # Read and display raw text from file
        with open(self.ws.filepath, 'rb') as f:
            f.seek(obj.file_offset)
            for i in range(50):
                raw = f.readline()
                if not raw:
                    break
                line = raw.decode('utf-8', errors='replace').rstrip()
                if i > 1 and (re.match(r'^[0-9a-f]{16}\s+[0-9a-f]{16}', line) 
                             or line.startswith('!')):
                    break
                print(f"  {line}")
    
    def _cmd_xrefs(self, arg: str):
        """Find objects pointing to address (slow — scans all objects)."""
        if not arg:
            print("Usage: xrefs ADDR")
            return
        
        target = int(arg.replace('0x', ''), 16)
        print(f"  Scanning for references to 0x{target:x}...")
        print("  (This may take a while for large workspaces)")
        
        refs = []
        count = 0
        for typename, addrs in self.ws.index.type_index.items():
            for addr in addrs:
                obj = self.ws.get_object(addr)
                if obj:
                    for f in obj.fields:
                        if target in f.values:
                            refs.append((addr, obj.typename, obj.tag))
                            break
                count += 1
                if count % 50000 == 0:
                    print(f"  ... scanned {count} objects, {len(refs)} refs found", end='\r')
                if len(refs) >= 50:
                    break
            if len(refs) >= 50:
                break
        
        print(f"  Found {len(refs)} references to 0x{target:x}:")
        for addr, tn, tag in refs:
            print(f"    @{addr:x}  {tn} {tag}")


def main():
    parser = argparse.ArgumentParser(
        description='Interactive explorer for Dyalog APL crash dumps')
    parser.add_argument('dump', help='Wsdump text output file (.txt)')
    parser.add_argument('-i', '--info', metavar='FILE',
                       help='Wsdump -i crash info output file')
    
    args = parser.parse_args()
    
    explorer = AplcoreExplorer(args.dump, args.info)
    explorer.start()


if __name__ == '__main__':
    main()
