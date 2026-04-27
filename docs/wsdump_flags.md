# wsdump.exe Flag Reference

`wsdump.exe` is a closed-source tool shipped with Dyalog APL for reading aplcore crash dumps.

## Usage

```
wsdump [-w width] [-c {8|16}] [-i] [-q] [-f] [-l] [-b] [-g] <aplcore_file>
```

## Flags

### `-c {8|16}` — Cell Width

Controls how many hex digits are used per memory word.

- **`-c 8`**: 32-bit display. Each word shown as 8 hex digits. More compact — fits ~8 words per line. Addresses also shown as 8 digits.
- **`-c 16`**: 64-bit display. Each word shown as 16 hex digits. 3 words per line. **Default for 64-bit aplcores.**

Example (`-c 8`):
```
06cbfc00 ffffff80 00000000 00020000 07634470 07634520 00000000 00000000 00000000
root          128 00000040          *Code                      *Larg
```

Example (`-c 16`, default):
```
0000000006cbfc00 ffffffffffffff80 0000000000000000 0000000000020000
root                          128 0000000000000040
            fc18 0000000007634470 0000000007634520 0000000000000000
                 *Code
```

### `-w <width>` — Output Width

Sets the output width in columns. Affects line wrapping of field labels and values.

### `-q` — Quiet Mode

Outputs only the 5-line header:

```
magic no. aa00 WSversion 20.14 APLversion 20.0.25
<filename> saved <timestamp>

svn revision 53273
not coded in wsdump 2777488621
```

No objects, no trailer. Useful for quickly checking aplcore version/date.

### `-i` — Info Mode (Crash Forensics)

**The most important flag for crash analysis.** Scans the entire aplcore binary and produces a compact crash report (~1,900 lines / 166 KB for a 614 MB aplcore).

**Processing time**: ~4 minutes for 614 MB (must read entire file). Produces no output until complete.

**Output sections**:

1. **Header** (same as `-q`)
2. **Interesting Information** block:
   - Serial number, DFLAGS, MAXWS, version info
   - Edition (Unicode/Classic), endianness, OS
   - Build timestamp
   - Full command line and parsed arguments
   - Workspace filename and WSID
3. **Crash details**:
   - `syserror` code and message
   - `ExceptionCode` (e.g., 0xC0000005 = ACCESS_VIOLATION)
   - `ExceptionAddress` (faulting instruction)
   - `ExceptionParameter` (read/write, faulting address)
   - Full x64 CPU register dump (RAX-R15, RIP, RSP, RBP, segment registers, flags)
4. **C Stack**: Native call stack (return addresses)
5. **LOCALPs**: Local parameters (often NONE)
6. **AddressSpace**: Full virtual memory map with loaded DLLs
7. **APLStack**: APL-level call stack per green thread:
   ```
   !APLStack: !TID:N!namespace.function[line] source_code!
   ```

**Does NOT include**: workspace objects, symbol tables, variable values.

### `-f`, `-l`, `-g`, `-b` — Unknown Flags

These flags produce **identical output to the default** in our testing with aplcore_10 (Dyalog v20, 64-bit Unicode, 614 MB). The first 5 seconds of output are byte-identical to default.

Possible explanations:
- They may affect specific object types not present in this aplcore
- They may require combination with other flags
- They may be deprecated or version-specific
- They may affect output only for swapped workspaces or other edge cases

Hypotheses based on flag letters:
- `-f`: functions? (show function source?)
- `-l`: labels? listing? (show line numbers?)
- `-g`: globals? (show global variables?)
- `-b`: bodies? bytecode? (show function bytecode?)

**Needs testing with other aplcores to determine actual behavior.**

### Default (no flags)

Full workspace dump: header + every object in the workspace + trailer.

- **Output size**: ~656 MB for a 614 MB aplcore (text is larger due to hex formatting and labels)
- **Processing time**: Output begins immediately and streams continuously
- **Content**: Every object (simple, symbol, stack, body, fptr, local, pointer, defunct, derv, xtrnfn, xtrnid, root) with full field details
- **Trailer**: AddressSpace map + APLStack (same as `-i` but without the crash forensics header)

## Recommended Workflow

1. **Quick check**: `wsdump -q aplcore` — verify version and date
2. **Crash analysis**: `wsdump -i aplcore` — get exception details, registers, call stacks
3. **Full dump**: `wsdump aplcore > dump.txt` — for deep object-level investigation
4. **Compact view**: `wsdump -c 8 aplcore` — denser output for 64-bit aplcores

## Version Information

- **Tested with**: wsdump.exe 383 KB, built from `D:\objects\APLTrunk\obj\apl\win\64\unicode\winapi\dev\opt\`
- **PDB path**: `wsdump.pdb` (not available)
- **Aplcore tested**: Dyalog APL v20.0.25, Unicode 64-bit, WSversion 20.14, SVN 53273
