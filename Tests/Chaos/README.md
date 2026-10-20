# Chaos music regression checks

Run from the project root:

```powershell
dotnet run --project .\Tests\Chaos\Chaos.csproj -c Release
dotnet run --project .\Tests\Chaos\Chaos.csproj -c Release -- '\\wsl.localhost\Ubuntu-20.04\root\sm64'
dotnet run --project .\Tests\Chaos\Chaos.csproj -c Release -- --io-root '\\wsl.localhost\Ubuntu-20.04\tmp'
dotnet run --project .\Tests\AudioPlayback\AudioPlayback.csproj -c Release
dotnet build .\Randm64.csproj -c Release
```

The optional game path exercises all available M64 files in memory. It never changes game assets or opens an audio device. Synthetic fixtures check compressed 15-bit delays, short and large notes, channel state retained between sections, instruments configured after layer creation, actual channel identity, gate direction, rejected pointers/control flow, reproducible seeds and staged file replacement. Each supported mode runs with eight seeds per sequence, then combined modes and alienation are checked.

`--io-root` instead runs the file fixtures in a unique child of the specified writable test directory, then removes it. M64 commits use same-directory overwrite rename so they also work on the WSL UNC provider, which does not support `File.Replace`. The write/backup/failure checks pass on both local Windows storage and WSL `/tmp`.

`Services/Chaos/M64CommandScanner.cs` follows the layouts in the target game's `src/audio/seqplayer.c`. It keeps audio operands separate from instructions, delays, pointers, short-note tables and envelopes. Unknown regions, malformed/ambiguous scripts, dynamic calls and self-modifying commands are refused. The scan has instruction and stack limits. This is a conservative structural check, not a timing-accurate execution of concurrent channels/layers or an emulator. Custom scripts using unsupported features must remain unchanged.

`Services/Chaos/M64Corruptor.cs` changes recognized values in memory, rescans the result, then stages and replaces the target. It preserves the first backup and checks for intervening external changes. An unsuccessful encoding does not restore an old backup over the live file. Explicit non-accumulating runs use the existing backup as their baseline; accumulating queue entries use the current file. M64 intensity is capped at 100% by the window.

Preset IDs remain stable. Mode 27 (Sequence Pointer Slider) is disabled because it mixed incompatible pointer targets and could produce audio-thread faults. Old presets containing it are refused before mutation. Modes 19 and 25 now accurately describe alternate-note octave/minor-third shifts, rather than promising duplicate voices. Mode 20 accents existing percussion rather than claiming to insert drum rolls. Mode 28 sets full-duration gates and preserves release rates; 255 is a nearly silent gate, not infinite sustain.

ChaosEngineWindow remains the UI and existing asset/patch orchestration. Music parsing/mutation and preset models have been removed from it; the other corruption paths have not all been rewritten. Failures that propagate from a file operation are reported as failures, and duplicate targets or overlapping runs are prevented. A batch can have successful edits before another file fails; it is not an all-assets transaction.

Structural tests reduce known sources of malformed music. They do not prove the absence of SM64 Thread 4 faults: corrupted ROMs still require emulator/game testing, particularly when music corruption is combined with sound-bank, DMA, ADSR or game-code corruption.
