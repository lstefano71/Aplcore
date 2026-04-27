# Aplcore Analysis Toolkit — Usage Guide

Tools for analyzing Dyalog APL crash dumps (`aplcore` files and `wsdump.exe` output).

---

## Quick Start

```
# 1. Generate the text dump (takes ~5 min for large aplcores)
wsdump.exe aplcore_10 > aplcore_10.txt

# 2. Generate crash info (takes ~4 min, produces ~166KB)
wsdump.exe -i aplcore_10 > wsdump_i.txt

# 3. Run the crash analyzer
python tools/crash_analyzer.py aplcore_10.txt -i wsdump_i.txt

# 4. Start the interactive explorer
python tools/aplcore_explorer.py aplcore_10.txt -i wsdump_i.txt
```

---

## Tool 1: `crash_analyzer.py`

Automated crash report generator. Produces a text and/or JSON report
covering all threads, call stacks, local variables, and a diagnosis.

### Usage

```
python tools/crash_analyzer.py <dump.txt> [options]

Options:
  -i FILE, --info FILE    wsdump -i output file (adds exception/register details)
  -o FILE, --output FILE  Write text report to file (default: stdout)
  --json FILE             Also write JSON report to file
  -v, --verbose           Print progress to stderr
```

### Examples

```bash
# Basic: stdout text report
python tools/crash_analyzer.py aplcore_10.txt

# Full: with crash info, saved to files
python tools/crash_analyzer.py aplcore_10.txt -i wsdump_i.txt \
    -o reports/report.txt --json reports/report.json -v

# Pipe to pager
python tools/crash_analyzer.py aplcore_10.txt -i wsdump_i.txt | less
```

### What it outputs

```
========================================================================
  DYALOG APL CRASH ANALYSIS REPORT
========================================================================

Workspace:  <A> [Sviluppo Gen Svi - Gen8].dws
Version:    APL 20.0.25, WS format 20.14
Saved:      Wed Apr  1 11:23:37 2026

--- EXCEPTION ---
Type:       ACCESS_VIOLATION (read at 0x28)
Address:    0x00007ff7d5f9f74a
Fault:      0x28
Syserror:   Syserror: 999 code: 0

--- THREADS (7) ---
  ┌─ Thread 0 ─────────────────────────────────────────────────
  │ APL Call Stack (31 frames):
  │  → #.rDQ[8]
  │    #.WaitDel[18]
  │    ...
  │ SI Stack (137 frames):
  │   MODE     [8]
  │   SHADOW
  │     local: No_w_SGC
  │   ...
  └─────────────────────────────────────────────────────────────

--- DIAGNOSIS ---
Exception: ACCESS_VIOLATION (read at 0x28)
Fault address: 0x28
  → Near-null dereference (likely NULL pointer + field offset)
7 green threads, 7 with call stacks
  TID:0 — #.rDQ[8]
  TID:68 — #.ERM.ShowGUI[32]
  ...
```

### JSON schema

```json
{
  "workspace": { "wsid": "...", "ws_version": "...", "apl_version": "...", "timestamp": "..." },
  "exception": { "code": 0, "description": "...", "address": "0x...", "fault_address": "0x28", "syserror": "..." },
  "threads": [
    {
      "tid": 0,
      "address": "0x...",
      "pc": 0,
      "retn": 31,
      "func_name": "",
      "call_stack": [
        { "namespace": "#", "function": "rDQ", "line": 8 }
      ],
      "si_frames": [
        { "type": "MODE", "function": "", "line": 8,
          "locals": [{ "name": "No_w_SGC", "addr": "0x..." }] }
      ]
    }
  ],
  "object_counts": { "simple": 1336848, "symbol": 353397, ... },
  "diagnosis": "...",
  "analysis_time": 61.8
}
```

---

## Tool 2: `aplcore_explorer.py`

Interactive REPL for exploring the workspace object graph.
Load once, then issue commands — no need to re-parse between queries.

### Usage

```
python tools/aplcore_explorer.py <dump.txt> [options]

Options:
  -i FILE, --info FILE    wsdump -i output file
```

### Startup

```
Loading aplcore dump...
  Building index (this takes ~35s)...
  Indexed 1862062 objects in 44.2s
  Running crash analysis...
  Ready. 7 threads, 1862062 objects.

Type 'help' for commands, 'quit' to exit.

aplcore>
```

### Commands

#### `info` — Workspace and crash summary
```
aplcore> info
  WSID:       <A> [Sviluppo Gen Svi - Gen8].dws
  Version:    APL 20.0.25, WS 20.14
  Exception:  ACCESS_VIOLATION (read at 0x28)
  Threads:    7
  Objects:    1,862,062
```

#### `threads` — List all green threads
```
aplcore> threads
  TID:0     #.rDQ[8]                     SI:137 frames
  TID:67    #.Conga.LIB.Wait[8]          SI:17 frames
  TID:68    #.ERM.ShowGUI[32]            SI:6 frames
  ...
```

#### `si [TID]` — SI stack for a thread (default: TID 0)
```
aplcore> si 68
  APL Call Stack:
    → #.ERM.ShowGUI[32]

  SI Frames:
      0  LNS        @b654788
      1  LNS        @b654750
      2  MODE     [32]  @b64eda8
      3  SHADOW     @b64ecf8
           local: READY
      4  LNS        @b64f5e8
      5  THREAD     @b64ea88
```

#### `locals [TID]` — Show all locals grouped by frame
```
aplcore> locals 80
  HBB.TGET:
    TGET                          @b635b80
  HBB.Wait:
    sobj                          @b635c20   (simple size=4)
  ...
```

#### `inspect ADDR` — Inspect any object by hex address
```
aplcore> inspect 6cbfc00
  Address:    0x0000000006cbfc00
  Type:       root
  Size:       128 words (1024 bytes)
  Fields (42):
    fc18+00  *Code             0x0000000007634470  → body <#>
    fd20+08  *SIstack          0x000000001e0babb0  → stack LNS
    fea0+10  *THREAD           0x00000000070425c0  → stack THREAD
    ...
```

#### `follow ADDR.field1.field2` — Walk a pointer chain
```
aplcore> follow 6cbfc00.*THREAD.*LINK
  0x6cbfc00 (root )
  *THREAD -> 0x70425c0 (stack THREAD)
  *LINK -> 0x1e0babb0 (stack LNS)

  Final object:
  Address:    0x000000001e0babb0
  Type:       stack  Tag: LNS
  ...
```

#### `symbols [PATTERN]` — Search symbol names (regex)
```
aplcore> symbols ⎕CT
  Found 1 matches:
    ⎕CT       S  @6cc0508

aplcore> symbols ^ERM\.
  Found 12 matches:
    ERM.ShowGUI     F  @b1d3fd0
    ERM.HideGUI     F  @b1d4200
    ...
```

#### `functions [PATTERN]` — Search function symbols only
```
aplcore> functions ShowGUI
  Found 1 functions:
    ShowGUI       @b1d3fd0

aplcore> functions ^Wait
  Found 3 functions:
    Wait          @77f2c48
    WaitDel       @7634538
    WaitMain      @763c2a0
```

#### `value ADDR` — Decode a simple (data) object
```
aplcore> value 6cc07e8
  Type:   simple
  Size:   4 words
  Tally:  2
  Dtype:  int64
  Data:
    0x0000000000000000  = 0
    0x0000000000000001  = 1
```

#### `stats` — Object type counts
```
aplcore> stats
  simple         1,336,848
  symbol           353,397
  stack             70,904
  ...
  TOTAL          1,862,062
```

#### `find TYPE [TAG]` — Find objects by type and optional tag
```
aplcore> find stack THREAD
  Found 70904 stack objects (16 with tag='THREAD')
    @70425c0  THREAD   size=8
    @7042660  THREAD   size=44
    ...

aplcore> find root
  Found 1 root objects
    @6cbfc00
```

#### `raw ADDR` — Show raw wsdump text for an object
```
aplcore> raw b1d3fd0
  000000000b1d3fd0 ffffffffffffffb8 0000000000000048 0000000088003801
  symbol                         72 0000000000b1f418         F
              3fe8 0000000000000000 000000000b1d3f80 000000000748b0b0
                   *<=              *=>              *value
              4000 0000000006cc5e88 00490047776f6853 000000000000
                   *symflo            S   h   o   w   G   I
```

#### `trailer` — APL stack from the dump trailer
```
aplcore> trailer
  TID:0
    #.rDQ[8]
    #.WaitDel[18]
    ...
```

#### `xrefs ADDR` — Find all objects pointing to an address (slow)
```
aplcore> xrefs 6cbfc00
  Scanning for references to 0x6cbfc00...
  Found 3 references:
    @6cbfc40  pointer VP
    @7042660  stack THREAD
    ...
```

#### `root` — Inspect the root object
```
aplcore> root
  (same as: inspect <root_address>)
```

---

## Library: `wsdump_parser.py`

The underlying parser library. Import it in your own scripts.

### Parsing the full dump

```python
from wsdump_parser import WsDump

# Load and index
ws = WsDump("aplcore_10.txt")
ws.build_index()           # ~35s for 614MB aplcore

# Stats
print(ws.stats())
# {'simple': 1336848, 'symbol': 353397, 'stack': 70904, ...}

# Get an object by address (int or hex string)
root = ws.get_object(0x06cbfc00)
print(root.typename)       # 'root'
print(root.abs_size)       # 128

# Navigate fields
si_addr = root.get_pointer('SIstack')    # returns int vaddr or None
thread_addr = root.get_pointer('THREAD')

si = ws.get_object(si_addr)
print(si.tag)              # 'LNS'

# Walk a pointer chain manually
addr = si_addr
seen = set()
while addr and addr not in seen:
    seen.add(addr)
    obj = ws.get_object(addr)
    if not obj or obj.typename != 'stack':
        break
    print(f"  {obj.tag:8s} @{obj.address:x}")
    addr = obj.get_pointer('LINK')

# Decode a symbol name
sym = ws.get_object(some_symbol_addr)
print(sym.name)            # '⎕CT', 'ShowGUI', 'mdiSofia_def', etc.

# Get all objects of a type
for addr in ws.index.type_index.get('symbol', []):
    sym = ws.get_object(addr)
    if sym and sym.tag == 'F' and sym.name:
        print(sym.name)    # all function names

# Parse the trailer (APL stack + address space)
apl_stack, addr_space = ws.parse_trailer()
for frame in apl_stack:
    print(f"TID:{frame.tid}  {frame.namespace}.{frame.function}[{frame.line}]")
```

### Parsing wsdump -i output

```python
from wsdump_parser import CrashInfo

info = CrashInfo.from_file("wsdump_i.txt")

print(info.exception_description())   # 'ACCESS_VIOLATION (read at 0x28)'
print(info.exception_code)            # 0xC0000005
print(f"0x{info.exception_address:x}")  # crash address
print(info.syserror_msg)              # 'Syserror: 999 code: 0'
print(info.wsid)                      # workspace ID string
print(info.command_line)              # full command line

# CPU registers at crash
print(f"RCX = 0x{info.registers['Rcx']:x}")   # 0x0 — null pointer
print(f"RDX = 0x{info.registers['Rdx']:x}")   # root.*SIstack

# C stack frames
for frame in info.c_stack:
    print(f"  0x{frame.address:016x}")

# APL stack from -i
for frame in info.apl_stack:
    print(f"TID:{frame.tid}  {frame.namespace}.{frame.function}[{frame.line}]")

# All DLLs loaded
for dll in info.address_space:
    if dll.dll_name:
        print(f"  0x{dll.base_addr:x}  {dll.dll_name}")
```

### Key classes

| Class | Description |
|-------|-------------|
| `WsDump` | Full dump parser. Call `build_index()` first. |
| `WsDump.get_object(addr)` | Random-access object fetch by virtual address. |
| `WsDump.parse_trailer()` | Parse APL stack + address space from file tail. |
| `WsDump.stats()` | Dict of `{typename: count}`. |
| `WsObject` | One workspace object. Fields: `typename`, `tag`, `abs_size`, `tally`, `type_flags`, `fields`. |
| `WsObject.name` | Decoded symbol name (for `symbol` objects only). |
| `WsObject.get_pointer(label)` | Get value of a named pointer field. |
| `WsObject.get_field(label)` | Get value of any named field. |
| `WsObject.named_fields_dict` | All labeled fields as `{label: value}`. |
| `WsField` | One field row: `offset`, `values` (list of ints), `labels` (list of strings). |
| `CrashInfo` | Parsed `-i` output. See fields above. |
| `APLStackFrame` | `tid`, `namespace`, `function`, `line`, `source`. |
| `AddressMapping` | `base_addr`, `region_size`, `dll_name`, etc. |
| `decode_symbol_name(hex_words)` | Decode UTF-16LE symbol name from a list of hex word strings. |

### WsObject field access

```python
obj = ws.get_object(addr)

# Named pointer fields — use *-prefixed labels
si_addr = obj.get_pointer('SIstack')    # same as get_pointer('*SIstack')
link_addr = obj.get_pointer('LINK')

# Named scalar fields
fields = obj.named_fields_dict
tid = fields.get('tid', 0)
pc  = fields.get('pc', 0)

# Raw field iteration
for f in obj.fields:
    for i, label in enumerate(f.labels):
        value = f.values[i]
        if label.startswith('*') and value:
            target = ws.get_object(value)
            # ...
```

---

## Performance Notes

| Operation | Time | Notes |
|-----------|------|-------|
| `build_index()` | ~35s | One-time; required before all other ops |
| `get_object(addr)` | ~5ms | File seek + parse ~20 lines |
| `parse_trailer()` | ~0.1s | Reads last ~200KB of file |
| `CrashInfo.from_file()` | <1s | 166KB in-memory parse |
| `crash_analyzer.analyze()` | ~60s | Index + 7 threads + 208 SI frames |
| `functions` search | ~30s | Scans all 353K symbol objects |

The 35s index build is unavoidable (must scan 656MB). All subsequent queries are fast (single file seek). For repeated analysis, consider caching the index — the `ObjectIndex.entries` dict (`{vaddr: file_offset}`) is pickle-serializable.

---

## Files

```
wsdump/
├── aplcore_10            Raw binary aplcore (614MB)
├── aplcore_10.txt        wsdump default output (656MB)
├── wsdump_i_stdout.txt   wsdump -i output (166KB)
├── docs/
│   ├── aplcore_format.md   Object format reference
│   └── wsdump_flags.md     wsdump.exe flag reference
├── tools/
│   ├── wsdump_parser.py    Core parser library
│   ├── crash_analyzer.py   Automated crash report
│   └── aplcore_explorer.py Interactive REPL
└── reports/
    ├── crash_report.txt    Generated report (text)
    └── crash_report.json   Generated report (JSON)
```
