# Actual display-list call exclusion

The manager now edits nested `gsSPDisplayList(...)` sites in `actors/` and `levels/`. Selected calls emit `gsDPNoOp()`. Selected `gsSPBranchList(...)` tails emit `gsSPEndDisplayList()` so execution returns instead of falling through past a list. Other commands, addresses, vertex data and terminators stay intact. The target's GBI macros generate the actual command words; no generic F3DEX opcode table is assumed.

Levels 1–9 select approximately 10–90% of target hashes. Selection uses a deterministic seed and is consistent for the same target expression. Density is not an exact percentage of source sites or visible geometry: targets can have several callers, and skipping a parent can hide its children too. Material and restore lists can also be omitted, producing inherited graphics-state differences. This is source-level call exclusion, not random memory corruption or a hardware fault simulation.

The old HUD/Mario-body-part levels, movement trigger and global `gSPDisplayList` hook have been retired. Segment numbers and per-frame CPU call counts did not establish those anatomical promises; that hook also missed nested static calls and could remove entire dynamic wrappers. The new mode does not patch HUD or generated skybox code. It does not cover every CPU-generated display-list call. Changes require a ROM rebuild.

Each excluded site retains its original source in a managed comment. Reapplying first removes managed exclusions, then makes a new selection. Removing exclusions restores those exact expressions while preserving unrelated edits. First `.bak` files remain recovery copies and are never restored over current sources automatically. Recognized legacy hooks/config definitions are removed in place; unknown edited hooks or markers cause an error before writing. Files are staged and checked for intervening edits; failed batches attempt rollback without overwriting subsequent external edits.

Run from the project root:

```powershell
dotnet run --project .\Tests\DisplayListExclusion\DisplayListExclusion.csproj -c Release
dotnet run --project .\Tests\DisplayListExclusion\DisplayListExclusion.csproj -c Release -- '\\wsl.localhost\Ubuntu-20.04\root\sm64'
dotnet run --project .\Tests\DisplayListExclusion\DisplayListExclusion.csproj -c Release -- --io-root '\\wsl.localhost\Ubuntu-20.04\tmp'
dotnet build .\Randm64.csproj -c Release
```

The optional game path is read-only. Tests cover call/branch replacement, comments/literals/preprocessor directives, nested argument parentheses, density/seed determinism, exact restore, reapply, externally edited markers, BOM/newline preservation, first-backup preservation, rollback after a locked destination and legacy-hook removal. The harness also writes a small generated C fixture beside its executable for compilation with the target GBI headers.

`--io-root` runs the write/restore/rollback fixtures inside a uniquely named child of the supplied directory and removes that child afterward. Use a writable test location, such as WSL `/tmp`, rather than the game repository. Both commit and rollback use same-directory `File.Move(..., overwrite: true)`: `File.Replace` fails on the WSL UNC provider with `ERROR_NOT_SUPPORTED` (`0x80070032`). The fixture suite passes on both local Windows storage and the active WSL UNC path.

Validation used the active US `f3d_old` macro definitions and MIPS GCC in GNU89 mode. The compiled fixture emitted `C0000000 00000000` for its no-op and `B8000000 00000000` for its end commands. A full IDO ROM build and emulator rendering comparison have not been performed. Removing material/geometry calls can produce invalid inherited state; these checks do not establish crash-free playback on every renderer.
