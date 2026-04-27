# Dyalog APL Aplcore Format Reference

This document describes the format of aplcore files (crash dumps) produced by the Dyalog APL v20 interpreter, and the text output produced by `wsdump.exe`.

## 1. Overview

When the Dyalog APL interpreter crashes (e.g., SYSERROR 999), it writes an **aplcore** file — a binary dump of the workspace memory plus metadata about the crash. The workspace is a contiguous memory region containing all APL objects: arrays, functions, namespaces, symbol tables, stack frames, and interpreter control structures.

Dyalog ships `wsdump.exe`, a closed-source tool that reads an aplcore and produces a human-readable text dump. This document covers both the binary and text formats.

## 2. Binary Aplcore Format

### 2.1 File Header

The aplcore starts with a fixed header:

| Offset | Size | Field | Example | Description |
|--------|------|-------|---------|-------------|
| 0x00 | 2 | Magic | `aa 00` | Always `0xAA00` |
| 0x02 | 2 | WSVersion | `14 0e` | Workspace format version (0x14=20, 0x0e=14 → v20.14) |
| 0x04 | 4 | APLVersion | `14 00 19 a4` | Interpreter version + build hash |
| 0x08 | 8 | BaseAddr | `00 00 cc 06 00 00 00 00` | LE workspace base address (0x06CC0000) |
| 0x10 | 8 | SizeInfo | `60 d4 da 25 00 00 00 00` | Workspace end address or size |
| 0x18 | 8 | Misc1 | | Additional metadata |
| 0x20-0x3F | 32 | Misc | | Version/config fields |
| 0x40 | ... | RootObj | | Root object starts here (negative offset from page boundary) |

The workspace memory follows the header as a contiguous block. Addresses in the dump are virtual addresses from the original process; the file offset is `(virtual_addr - BaseAddr) + header_size`.

### 2.2 Object Header (Binary)

Every object in the workspace has a 24-byte header:

```
[8 bytes: neg_size] [8 bytes: tally] [8 bytes: type_flags]
```

- **neg_size**: Twos-complement negative of the object size in 8-byte units. Object size = `-neg_size * 8` bytes (including header).
- **tally**: Number of elements (for arrays), reference count, or subfield count.
- **type_flags**: Packed word encoding the object type, data type, and various flags.

Object data follows immediately after the header.

### 2.3 Workspace Layout

The workspace is divided into a contiguous sequence of objects, each immediately following the previous. There are no gaps (freed objects become `defunct` type). Two `root` objects anchor the entire structure:

1. **Root #** — the user workspace root (namespace `#`)
2. **Root ⎕SE** — the session editor namespace

## 3. Wsdump Text Format

### 3.1 Header Lines

```
magic no. aa00 WSversion 20.14 APLversion 20.0.25
<filename> saved <timestamp>

svn revision 53273
not coded in wsdump 2777488621
```

### 3.2 Object Format

Every object is rendered as:

```
VADDR        NEG_SIZE         TALLY            TYPE_FLAGS
typename                 abs_size  SLOT_ADDR    [tag] [extra_flags]
             OFFSET  VAL1             VAL2             VAL3
                     *field_label1    *field_label2    *field_label3
```

Where:
- **VADDR**: 16-hex-digit virtual address of the object
- **NEG_SIZE**: 16-hex-digit twos-complement negative size
- **TALLY**: 16-hex-digit element/ref count
- **TYPE_FLAGS**: 16-hex-digit packed type word
- **typename**: Decoded type name (see §4)
- **abs_size**: Positive size (= -neg_size, in 8-byte units)
- **SLOT_ADDR**: File slot offset
- **tag**: Type-specific tag (e.g., `THREAD`, `LNS`, `SHADOW` for stack objects)
- **OFFSET**: Low 16 bits of the field's address (offset within a 64K page)
- **VAL1..3**: Up to 3 field values per line (16-hex-digit each)
- **\*field_label**: Field name (prefixed with `*` if it's a pointer to another object)

### 3.3 Cell Width Modes

With `-c 8`: 32-bit display (8 hex digits per word, ~8 words per line)
With `-c 16`: 64-bit display (16 hex digits per word, ~3 words per line, **default for 64-bit aplcores**)

### 3.4 Simple Value Display

`simple` objects may show `...` after the slot address, meaning the raw data is present but not decoded by wsdump. The actual bytes are in the binary aplcore at the indicated address.

### 3.5 Trailer Sections

After all objects, the dump ends with `!`-prefixed lines:

```
!AddressSpace:BaseAddress AllocationBase AllocationProtect RegionSize State Protect Type
!AddressSpace:0x... 0x... 0x... 0x... 0x... 0x... 0x... [dll_name]
...
!APLStack: !TID:N!namespace.function[line] source_line_text!
...
!APLStack: END.
!
```

## 4. Object Types

### 4.1 `root` — Workspace Root

The workspace has exactly 2 root objects (# and ⎕SE). Each is 128 slots (1024 bytes) containing pointers to all major interpreter structures.

**Slot map** (offsets relative to object start, in 3-word groups):

| Offset | Field 1 | Field 2 | Field 3 |
|--------|---------|---------|---------|
| +0x18 | *Code | — | — |
| +0x30 | *Larg | — | — |
| +0x48 | *Rarg | — | — |
| +0x60 | *Rslt | — | — |
| +0x78 | *Temp | — | — |
| +0x90 | *(K5) | *(K6) | *(K7) |
| +0xC0 | *(K8) | *(K9) | — |
| +0xD8 | — | — | — |
| +0x108 | *Axis | — | — |
| +0x120 | *LOCSYMT | *SIstack | *0 |
| +0x138 | *1 | *Zilde | QEN |
| +0x150 | *DESP | *INDXASS | *ConstIn |
| +0x168 | FLAG-WD | *FUNCTN | *TOKEN |
| +0x180 | *SVLINK | *OBJPCK | *DLL_LINK |
| +0x198 | *Clip | *Symhead | *Qlocsym |
| +0x1B0 | *Conhead | *Hardwir | *Czilde |
| +0x1C8 | *Ws_head | *Ws_qvar | *Space |
| +0x1E0 | *Qvar[0] | *Qvar[1] | *Qvar[2] |
| +0x1F8 | *JWM_WS | *CONTEXT | *S_CHARS |
| +0x240 | *QSM[1] | *CUR_OBJ | *COPYDICT |
| +0x258 | *QDM | *SYM_WK0 | *SYM_WK1 |
| +0x2A0 | *OLE_LINK | *TLIBLINK | *THREAD |
| +0x2B8 | *TYPE_ID | *TYPE_LIB | *RSPACE |
| +0x300 | *CLASSDEF | *CLASSDEF | *LASTSPCE |
| +0x348 | *BUILDID | *QUADAN | *SPELLS |
| +0x390 | *PARENT_LIST | *BREADCRUMBS | *LINK_INFO |
| +0x3A8 | *WSFULL_RESERVE | *spare0 | — |

Key fields:
- **\*SIstack**: Pointer to the state indicator (call stack) — the chain of executing functions
- **\*THREAD**: Pointer to the thread table — linked list of green thread descriptors
- **\*Symhead**: Head of the global symbol hash table chain
- **\*Space**: Current namespace
- **\*LOCSYMT**: Local symbol table for current context
- **\*DLL_LINK**: Linked list of ⎕NA external function bindings

### 4.2 `symbol` — Symbol Table Entry

Symbols are the named references in the workspace — every variable, function, operator, namespace, system name, etc. Symbols are organized in a balanced BST (via `*<=`/`*=>` pointers) within the hash table.

```
ADDR  neg_size  tally  type_flags
symbol         abs_size  slot_addr    FLAG
         OFFSET  ptr1  ptr2  ptr3
                 *<=   *=>   *value
         OFFSET  ptr4  name_word1  name_word2...
                 *symflo  char1  char2  char3...
         OFFSET  name_word3  name_word4  0000000000000000
                   char4  char5
```

**Fields:**
- **\*<=**: Left child in balanced BST (or 0)
- **\*=>**: Right child in balanced BST (or 0)
- **\*value**: Pointer to the symbol's value object
- **\*symflo**: Pointer to the symbol's float/localized-value chain

**Object sizes:**
- Size 6: No name stored (constants C, system vars S) — identity from hash position
- Size 8+: Name stored after `*symflo`. Extra size = ceil(name_chars / 4) words
- Symbols with size 8 hold names up to 4 chars, size 9 up to 8, size 10 up to 12, etc.

**Flag** (trailing letter):
| Flag | Meaning | Count (this aplcore) |
|------|---------|---------------------|
| S | System variable (⎕IO, ⎕CT, ⎕TRAP, etc.) | 27 |
| C | Constant (literal in function body) | 131,962 |
| F | Function (tradfn, dfn, system fn) | 50,926 |
| V | Variable | 32,133 |
| P | Property | 29,384 |
| N | Namespace | 2,838 |
| O | Operator | 476 |
| I | Interface | 131 |
| M | Method | 64 |
| L | Label | 21 |
| ? | Unknown/unresolved | 1,007 |
| (none) | Internal/unnamed symbol (⍙⍙⍙-prefixed) | ~250,000 |

**Name encoding**: Names are stored as UTF-16LE code points packed into 64-bit words:
- Each 64-bit word holds up to 4 UTF-16LE characters
- The hex display is big-endian; reverse bytes to get memory (LE) order, then read UTF-16LE pairs
- `0x2395` = `⎕` (quad) — prefix for system names
- `0x235E` = `⍞` (quote-quad)
- `0x2206` = `∆` (delta) — prefix for user utility names
- `0x2359` = `⍙` (delta-stile) — prefix for internal names; `⍙⍙⍙` = deeply internal
- ASCII chars: `0x00XX` where XX is the ASCII code
- NUL (`0x0000`) terminates the name

**Decoding example**: `0070004f23592359` →
1. Reverse bytes: `59 23 59 23 4f 00 70 00`
2. Read UTF-16LE pairs: `0x2359`=⍙, `0x2359`=⍙, `0x004f`=O, `0x0070`=p
3. Result: `⍙⍙Op` (continues in next word)

### 4.3 `simple` — Data Arrays

All APL data values: numbers, characters, booleans. Scalars and arrays alike.

```
ADDR  neg_size  tally  type_flags
simple         abs_size  slot_addr  ...
```

The `...` tag on the type line means raw data is present in the binary aplcore but **not decoded by wsdump** in text mode. The wsdump text file shows only the 4-word header (32 bytes) for these "compact" objects — no data field rows will follow. To access the actual data, use the binary aplcore and the slot_addr.

The **tally** field gives the element count. For 4-word compact objects (abs_size=4), the tally is correct even though no data fields appear. For larger objects (abs_size>4), data fields may or may not appear depending on the object layout.

The **type_flags** word encodes the data type:

| type_flags pattern | Likely data type |
|-------------------|-----------------|
| `0x0000271f` | Integer array |
| `0x0000281f` | Character array |
| `0x0000221f` | Boolean array |
| `0x0000211f` | Float (64-bit) array |
| `0x0000220f` | Small integer / compact |
| `0x00002X1f` | General pattern: 2=array, X=element type, 1f=flags |

Note: the `is_char = bool(type_flags & 0x40)` heuristic gives a rough character/numeric distinction for the value decoder (bit 6 of the low byte).

### 4.4 `stack` — Stack Frames

Stack objects represent execution state. The `tag` field distinguishes subtypes:

#### THREAD (23 instances)
Green thread descriptor. Two variants:

**Head entry (size 8)**:
```
*LINK    → 0 (no sibling)
count    → number of threads
*TVEC    → token vector for thread scheduling
*BEANPOT → thread pool / scheduling state
NXT_TH   → next thread ID to allocate
```

**Per-thread descriptor (size 44)**:
```
*LINK      → next SI frame in this thread's chain
count      → frame count or thread-local counter
*code      → currently executing code object
*lst       → current line state (LNS frame)
*token     → token being processed
*space     → current namespace
*tparent   → parent thread (for thread hierarchy)
*sync      → synchronization token
*xvec      → exception vector (⎕TRAP)
*rslt      → result accumulator
*held      → held tokens (⎕TGET)
*events    → pending events queue
*func      → current function object
*larg      → left argument
*rarg      → right argument
*breq      → break request flag
*name      → thread name (Czilde if unnamed)
*class     → class context
*dmx       → ⎕DMX error state
*shadpar   → shadow parent (for localization)
*future    → future/promise object
*indxass   → indexed assignment state
*trigger   → trigger state
tid        → thread ID (integer: 0, 67, 68, 69, 80, 220, 241)
pc         → program counter
retn       → return address/line
rflags     → runtime flags
temp       → temporary value
modes      → execution mode flags
```

#### LNS — Line State (38,001 instances)
Represents a single function invocation at a specific line:
```
*LINK    → next SI frame (older frame in chain)
count    → number of local fields
*LOCSYMT → local symbol table for this frame
*BODY    → function body being executed
*MONITOR → monitor/profiling data
*USER    → user data
TIME     → execution timestamp
*CALLINF → calling information
*SHADBLK → shadow block (localized variables)
*OPTBODY → optional body (for dfns with guards?)
*FILINFO → file information
```

#### SHADOW (16,993 instances)
Localized variable block. One SHADOW frame is created when a function localizes variables via `;var` in its header or via `⎕SHADOW` during execution.

**A single SHADOW frame can hold multiple localized names** (one entry per `;varname`). The `count` field gives the number of entries.

**SHADOW frame memory layout** (after label-parsing fix — bare `.` tokens in the label line are visual separators and must be skipped):

```
Offset  Content
──────  ──────────────────────────────────────────
+0      *LINK      → next SI frame
+8      count      = number of entries (may be > 1)
+16     *SSPACE    → local namespace (NSR stack object)
+24     *SCLASSDEF → class definition pointer (or null)
── per entry i (0..count-1), 3 words each: ──────
+32+i*24  class...   = integer class tag (0x40000000 etc.)
+40+i*24  *name...   → symbol object (the variable name)
+48+i*24  *value..   → saved value (what was there before; restored on return)
```

Note: the first entry's class tag position coincides with the `*SCLASSDEF` field, so entry 0 uses offset +24 for class, +32 for *name..., +40 for *value.., and entries 1..N-1 start at offset +48.

**Key distinction**: `*value..` in the SHADOW frame is the **saved (old) value** to restore when the function returns. The **current value** of the local variable is stored in the local namespace (SSPACE), not in the symbol's `*value` pointer (which is often null for local variables in APL v20).

#### DFN (5,106 instances)
Direct function (dfn/dop `{...}`) execution frame.

#### NSR (4,565 instances)
Namespace reference frame — pushed when entering a namespace.

#### FLO (4,565 instances)
Function-local object — associated with NSR frames.

#### GUI (1,464 instances)
GUI event callback frame — pushed during `⎕DQ` processing.

#### DMX (9 instances)
Error state block — captures `⎕DMX` information when an error occurs.

#### Other subtypes
- **NAMETAG** (113): Named reference tag
- **MODE** (51): Execution mode marker
- **ENVIRON** (5): Environment frame — thread blocked in system primitive (⎕NA, ⍎, ...)
- **DIAMOND** (3): Diamond separator (⋄) frame
- **CTRL** (2): Control structure (:If/:While) frame
- **CSTACK** (1): C stack snapshot
- **MAPPED** (1): Memory-mapped region
- **NULL** (1): Null sentinel

### 4.5 `body` — Function Body

Compiled function code:

```
ADDR  neg_size  tally  type_flags
body           abs_size  slot_addr  <#>
         OFFSET  line_offsets...
                 [     1][     0]  [     3][     2]  ...
         ...bytecode_data...
```

The body contains:
- **Line offset table**: Maps line numbers to byte offsets in the bytecode. Displayed as `[N]` pairs.
- **Bytecode**: The compiled APL code (opcode format is undocumented / proprietary).
- **type_flags**: The low bits encode function characteristics (e.g., `0x000800ae`).

### 4.6 `fptr` — Function Pointer / Field

```
ADDR  neg_size  tally  type_flags
fptr           abs_size  slot_addr  field
         *fval    → function value
         *type    → type information
         *init    → initializer
         *trigger → trigger function
         flags    → property flags
         *name    → name symbol
```

Used for class fields, properties, and function references.

### 4.7 `local` — Local Symbol Table

An array of pointers to symbol objects, representing the local names visible in a stack frame:

```
ADDR  neg_size  tally  type_flags
local          abs_size  slot_addr  <#>
         *sym1  *sym2  *sym3
         *sym4  *sym5  *sym6
         ...
```

### 4.8 `pointer` — Pointer Object

Reference to one or more other objects. Tagged `VP` (value pointer):

```
ADDR  neg_size  tally  type_flags
pointer        abs_size  slot_addr  VP
         count  *target1  *target2  ...
```

### 4.9 `derv` — Derived Function

Result of operator application or function train:

```
ADDR  neg_size  tally  type_flags
derv           abs_size  slot_addr
         *lfun    → left function operand
         *opco    → operator code (primitive ID or function)
         *rfun    → right function operand
         *code    → compiled code
         *lst     → line state
         *src     → source text
         lfoff    → left function offset in source
         opoff    → operator offset in source
         rfoff    → right function offset in source
```

### 4.10 `xtrnfn` — External Function (⎕NA)

DLL function binding:

```
ADDR  neg_size  tally  type_flags
xtrnfn         abs_size  slot_addr  dllfn
         *name    → function name symbol
         *xtrnid  → external library ID (→ xtrnid object)
         *na_arg  → ⎕NA argument specification
         *na_tok  → tokenized argument spec
         na_proc  → native function address (from LoadLibrary/GetProcAddress)
         ncallb   → callback count
```

### 4.11 `defunct` — Freed Object

Garbage / freed memory. Retains the original header but may reference objects that existed before the most recent GC. Useful for forensics but unreliable for pointer following.

## 5. The `-i` (Info) Output

The `-i` flag produces a compact crash report without dumping workspace objects. It takes ~4 minutes for a 614 MB aplcore (must scan entire file).

### 5.1 Format

```
magic no. ...
<filename> saved <timestamp>
svn revision ...
not coded in wsdump ...

!
========================== Interesting Information
!Serial: !NNNNNN!
!DFLAGS:          0x...
!MAXWS:           0x...
!SESSION:         0x...
!QUIT:            0x...
!MAJOR_VERSION:   NN
!MINOR_VERSION:   NN
!SVN_REVISION:    NNNNN
!BITS:            64
!EDITION:Unicode
!EXECUTION:Windows WindowsNT
!Created: <date> at <time>!
!CommandLine: !full_command_line!
!args: !exe_path!
!+s!
!workspace_path!
!opts: !key=value!
...
!WSFileName: <path>
!WSID: <workspace_id>
!syserror:        0x...
!msg:!Syserror: NNN code: N!
!BuildID:         0x...
!ExceptionCode:   0x...
!ExceptionFlags:  0x...
!ExceptionAddress: 0x...
!ExceptionParameter: 0x...
!Dr0-Dr7:         0x...
!Rax-R15, Rip, Rsp, Rbp, Rsi, Rdi: 0x...
!SegCs-SegSs:     0x...
!EFlags:          0x...
!
!apl_main:        0x...
!CStack:          0x...  (repeated, one per frame)
!CStack: END.
!
!LOCALPs: NONE.   (or local parameter info)
!
!AddressSpace:... (full virtual memory map)
!
!APLStack: !TID:N!ns.func[line] source_code!
...
!APLStack: END.
!
```

### 5.2 Key Fields

- **ExceptionCode**: Windows exception code (0xC0000005 = ACCESS_VIOLATION)
- **ExceptionAddress**: Address of the faulting instruction
- **ExceptionParameter[0]**: 0=read, 1=write violation
- **ExceptionParameter[1]**: The address that was accessed (e.g., 0x28 = NULL+0x28)
- **CPU Registers**: Full x64 register state at crash time
- **CStack**: Native C call stack (return addresses in dyalog.exe and system DLLs)
- **APLStack**: APL-level call stack with function names, line numbers, and source code

## 6. Symbol Name Decoding

Symbol names are stored in the `*symflo` area of symbol objects as UTF-16LE code points packed into 64-bit words.

### Decoding algorithm:

1. Read the hex words after `*symflo`
2. Split each 16-hex-digit word into 4-byte (2-char) groups: `AABB CCDD` → chars at code points `0xBBAA`, `0xDDCC` (little-endian)
3. Continue reading subsequent words until a null (0x0000) terminator or end of fields
4. The first char often indicates the name class:
   - `0x2395` (⎕) → system name (⎕IO, ⎕CT, etc.)
   - `0x235E` (⍞) → quote-quad name
   - Other → user name (plain ASCII or Unicode)

### Example:
```
0041005300530065 00720076006900630065
```
→ `A(0x0041) s(0x0073) S(0x0053) e(0x0065)` `r(0x0072) v(0x0076) i(0x0069) c(0x0063) e(0x0065)`
→ `AsService` (but check byte order — may be `AsSe` `rvic` `e`)

## 7. Navigating the Object Graph

### 7.1 Finding all threads

1. Start at root's `*THREAD` pointer → THREAD head object (tag=THREAD, size=8)
2. The head has: `*LINK` (always 0), `count` (number of threads), `*TVEC` (thread vector), `*BEANPOT`, `NXT_TH`
3. Per-thread descriptors are separate stack objects with tag=THREAD and size=44
4. Find them by scanning stack objects in the index, or following BEANPOT → pointer chain

**Per-thread descriptor fields (size=44, ~30 named fields):**
```
*LINK      → first SI stack frame (or 0 for TID:0)
count      thread-internal counter
*code      → body object (current function bytecode)
*lst       → local symbol table
*token     → current token/DFN frame
*space     → namespace reference (NSR)
*tparent   → parent thread descriptor (TID:0 for spawned threads)
*sync      → synchronization object
*xvec      → exception vector
*rslt      → result
*held      → held thread reference
*events    → event queue
*func      → function symbol being executed
*larg      → left argument
*rarg      → right argument
*breq      → break request
*name      → name reference
*class     → class reference
*dmx       → DMX (⎕DMX) error state
*shadpar   → shadow parent
*future    → future reference
*indxass   → indexed assignment
*trigger   → trigger reference
tid        green thread ID (0 = main)
pc         program counter within current function
retn       return point
rflags     runtime flags
temp       temporary
modes      execution modes
shad       shadow count
guis       GUI state
sleep      sleep state
```

### 7.2 Walking the SI stack for a thread

The State Indicator (SI) is a linked list of stack frames representing the APL call stack.

**Entry point:**
- **TID:0 (main thread)**: Follow root's `*SIstack` pointer
- **Other threads**: Follow the per-thread descriptor's `*LINK` pointer

**Walking the chain:**
1. From the entry point, read the stack object
2. Follow its `*LINK` to the next (older) frame
3. Continue until `*LINK` = 0 or a THREAD frame (bottom of thread's stack)

**SI frame types and their roles:**
| Type | Role |
|------|------|
| LNS | Line state — execution point in a tradfn or evaluated expression |
| MODE | Execution mode marker — the `[N]` line number field matches the trailer's `func[N]` |
| SHADOW | Shadowed (localized) variable — one per localized name per function call |
| DFN | Dfn (anonymous function) frame |
| FLO | Function-local storage |
| NSR | Namespace reference — current ⎕THIS |
| GUI | GUI callback frame (⎕DQ context) |
| NAMETAG | Named tag for stack frame |
| THREAD | Thread boundary marker (at bottom of spawned thread's SI) |
| DMX | Error state (⎕DMX) |
| ENVIRON | Environment frame — thread blocked in APL system primitive |
| DIAMOND | Diamond (◇) separator |
| CTRL | Control structure frame |

**ENVIRON frame fields:**

ENVIRON frames are pushed when a green thread suspends inside a system-level APL primitive (e.g. `⎕NA` calling a DLL, `⍎` executing a string). The frame's `*functn` and `*fname` fields are small integer opcodes (pointer-labeled in the dump but containing small integers, not addresses):

| *functn | *fname | Primitive | Situation |
|---------|--------|-----------|-----------|
| 0x20 | 0xb0 | `⎕NA` | Thread blocked in a native DLL call |
| 0x00 | 0x00 | `⍎` | Execute primitive between function calls |

Note: this table will grow as more examples are observed.

**⎕TGET suspension — no ENVIRON frame:**

When a green thread calls `⎕TGET` (directly or from a dfn wrapper like `HBB.TGET`), the thread blocks *within* the APL execution model rather than at a foreign-function boundary. No ENVIRON frame is created. Instead:

- The top SI frame is the **DFN** or **MODE** frame at the point `⎕TGET` was called.
- The thread descriptor's `wake` field encodes the wait type:
  - `wake = 0` — indefinite wait (`⎕TGET token`)
  - `wake ≠ 0` — timed wait (`timeout ⎕TGET token`); value is the wakeup tick count.
- The `pc` field in the thread descriptor points to the bytecode offset of the `⎕TGET` instruction within the suspended function body.

Example — `HBB.TGET` dfn (`{⍺≠0:⍺ ⎕TGET ⍵ ⋄ ⍵∊|⎕TPOOL:⎕TGET ⍵ ⋄ ⍬}`) called with timeout≠0:
```
Top SI frame:  DFN  *Name→symbol 'TGET'  DefOs=44  pc=45 (⎕TGET instruction)
Thread desc:   tid=80  pc=45  wake=0x852d52 (timed)
Argument:      *Alpha→simple tally=44 (timeout value)
               *Omega→simple tally=2  (token handle)
```

**Typical SI pattern for a function call:**
```
LNS      ← current line state (execution paused here)
LNS      ← previous line state
MODE [N] ← function at line N (matches trailer's func[N])
SHADOW   ← all localized variables (one SHADOW frame may hold multiple names)
...
LNS      ← caller's line state
MODE [M] ← caller at line M
...
```

**SHADOW frame reading**: Each SHADOW frame holds all variables localized in the `;var1;var2;...` header clause of a single function call, plus any `⎕SHADOW` calls within that function. The `*name...` field → symbol (gives the variable name); the `*value..` field → the saved value (old binding to restore on return). See section 4.4 for the detailed memory layout.

**Correlating SI with trailer**: MODE frames and DFN frames appear in the SI chain in the same top-to-bottom order as the `!APLStack:` trailer entries for that thread. SHADOW, LNS, DIAMOND, and ENVIRON frames do NOT correspond to a trailer entry. This one-to-one correspondence is used to annotate MODE frames with function names.

### 7.3 Correlating SI with trailer APLStack

The `!APLStack:` trailer provides human-readable function names per thread:
```
!APLStack: !TID:68!ERM.ShowGUI[32] r←ShowGUI;READY!
```

The MODE frame's line number `[N]` corresponds to the trailer's `func[N]`. To correlate:
1. Walk SI frames counting MODE frames
2. Match MODE[N] with the N-th trailer frame for that TID (both ordered top-down)

### 7.4 Resolving a variable's value

1. Find the symbol in the local or global symbol table
2. Read `*value` pointer → follow to the target object
3. If target is `simple` → decode the data
4. If target is `pointer` → follow the VP chain
5. If target is another `symbol` → it's an alias; follow again
