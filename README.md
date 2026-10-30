# Randm64

<img src="Assets/icon.png" width="64" height="64" alt="Randm64 icon">

A Windows editor for Super Mario 64 decompilation projects. Explore levels in 3D, edit objects and collision, work with music and sound, create behaviors, and build your project from one application.

Randm64 edits source projects. It does not import compiled ROM hacks as editable projects. Available tools depend on the files and build layout of the selected repository.

## Application Overview

![Randm64 main window with the level browser](docs/images/main-window.png)

## Editors and Tools

| Tool | What You Can Do |
| --- | --- |
| Level Editor | Explore visual geometry and collision, select actors, edit positions and rotations, and drop objects onto the ground. |
| Mesh Editor | Edit collision vertices, triangles, surface types, and water boxes alongside the level viewport. |
| Actor Editor | Inspect actor models, textures, and animation previews. |
| Music Editor | Edit M64 notes, instruments, and channels. Preview with SM64 samples or the explicit MIDI option. |
| Sound Editor | Browse extracted samples, preview audio, export WAV files, and replace samples. |
| Text Editor | Edit individual dialogs and preview pages while preserving source macros. |
| Warp and Painting Editors | Edit warp connections and supported painting settings. |
| Behavior Builder | Configure a custom object and review generated source before creating it. |
| Chaos Engine | Apply configurable effects to supported game assets and source code, with presets and run reports. |
| Build and Patch Tools | Configure repository builds, inspect build output, manage source patches, and translate supported ROM addresses. |
| Star and Objective Editor | Inspect stock objective candidates, verify star parameter encoding against project source, edit supported star indices and act visibility, and navigate to related objects. |
| Visual Warp Map | Browse declared level and area connections, inspect missing or ambiguous destinations, and open the existing warp editor. |
| Collision Diagnostics | Inspect invalid indices, degenerate/duplicate/small triangles, steep surfaces, possible gaps, and differences from visual geometry. |
| Sound Bank Inspector | Inspect regional instruments, percussion semitones, sample ranges, envelopes, tuning, release rates, and sample loops, including unresolved assets. |
| Project Object Search | Search normal, macro, and special placements by behavior, model, preset, and raw parameters, with level, area, and type filters. |
| Source Editor | Assemble draggable C blocks for Mario, Goddard, behaviors, camera, audio, menu, engine, and other project sources; preview and save scoped function hooks or manual source edits. |

### Visual source editor

Open **Source editor · code blocks** from the main tools panel or dashboard, **Code blocks** from the Level Editor, or **Code blocks** in the Behavior Builder. Choose a module, source file, and function. The module browser uses the selected project files, including Mario action files and `src/goddard/`; **all source** also includes project headers, level sources, and actor sources.

Click a palette block to add it or drag it into the stack. Drop on a control block to nest inside it, on a statement to insert after it, or on the empty workspace to append at the root. Use **Nest**, **Outdent**, **Up**, **Down**, **Duplicate**, and **Delete** to arrange the stack. Selecting a block exposes its editable fields. Supported blocks include assignments, increments, multiplication, calls, conditions with an optional final else child, repeat/while loops, and local variables. Mario starters support health, vertical speed, and invincibility; the Goddard starter calls an existing `gd_printf` declaration.

The default **Game code blocks** view displays and edits the selected existing function as nested colored blocks. Select a supported assignment, call, scalar declaration, condition, loop header, switch expression, or return to change its fields, then click **Update block**. Valid pending fields also apply when selecting another block or previewing. Edits replace the corresponding source span while preserving surrounding code, comments, other functions, and line endings.

In this view the palette adds code directly to the existing function. Choose **after selected**, **before selected**, **inside selected**, or **function start**; function-start insertion follows leading local declarations. Drag a palette block onto a control block to insert inside it, or onto a statement to insert at the chosen position. An else palette block attaches to a selected if without an else. **Delete** removes the selected supported block. Single-statement control bodies are wrapped or replaced with empty braces when needed to retain their original branch association. New executable blocks use a separate scope; new scalar locals go in a declaration section. Unsafe declaration placement is rejected for IDO compatibility.

Use **Undo game edit** and **Redo** before saving. Changes are staged in **Source text**, with no file write until **Preview block changes**, diff review, and **Save previewed changes**. Source-text edits also refresh the block view. Unknown C, regional/preprocessor sections, case labels, complex declarations, and generated-hook sections remain protected from direct block edits. Use **Source text** for unsupported C, or **My block program** to edit generated hooks. The hook program retains its own drag/reorder/nesting controls; **Save blocks** exports that reusable hook design rather than the game-source edits.

Visual blocks create a managed section before the original function body, with the original body retained in its own declaration scope. Blocks can reference function parameters and globals visible at the insertion point; original function locals are inside the retained body. Loops execute in a single function call, rather than waiting across game frames. Generated declarations precede statements for C89/IDO compatibility, and generated uses of `sins` or `coss` add the math utility header.

Use **Preview block changes**, review the diff, then **Save previewed changes**. **Source text** and **Preview source changes** support manual C and header editing. Saves keep first backups and reject external changes. Names, types, function declarations, and runtime behavior still require a game build and testing. The visual function browser uses the existing US/N64 source view when conditional brace layouts require it; ambiguous definitions and conditionals crossing function boundaries require the source-text view.

Block metadata is stored with the generated section so saved hook programs reopen visually. Manually changed managed sections are protected from visual replacement. **Save blocks** and **Load blocks** use reusable `.randblocks.json` projects with relative target paths; they can be loaded in another compatible decomp tree. Direct game-code editing uses source spans rather than converting or regenerating the entire function as a hook program.

```powershell
dotnet run --project .\Tests\SourceBlocks\SourceBlocks.csproj -c Release
dotnet run --project .\Tests\SourceBlocks\SourceBlocks.csproj -c Release -- '\\wsl.localhost\Ubuntu-20.04\root\sm64'
```

### Project inspection tools

Open these tools from the Tools Dashboard. Objectives and object search are also available in the Level Editor; the warp map is available in the Warp Editor; sound banks are available in the Music and Sound Editors. Collision diagnostics appear in the mesh editor Diagnostics tab. When the mesh editor is embedded in the Level Editor, selecting a valid diagnostic frames and highlights its triangle in the viewport.

Objective edits preserve the other behavior-parameter bytes and source expressions. Structured star fields require a recognized stock reward path and numeric parameters; custom or unverified behavior requirements remain source references. Star indices are zero-based (0 through 6). Shared source scripts can place the same object in several areas; the details panel identifies these shared placements. Close level editors using the same source before saving objective changes so pending object edits remain protected.

The warp graph describes source connections rather than gameplay reachability. Select a connection to inspect it, use the existing warp editor to change it, and refresh after external edits. Middle-drag pans; ctrl-wheel and the zoom buttons change scale.

Collision analysis is read-only. Defaults are one square world unit for small triangles, 38 degrees for steep upward-facing surfaces, and five world units for visual differences. These are editable inspection thresholds. Boundary edges and invisible collision can be intentional. Findings are discarded after table changes. Save pending visual vertex edits before comparing against saved visual geometry.

Sound bank inspection is read-only and uses the existing regional conditional resolver. A sequence may list several banks; the first is its default. Raw sample preview plays at the file rate, without sequence pitch, envelopes, or effects. Missing samples remain visible with their bank metadata. Use All banks to return from a sequence mapping to the full bank list.

Object search runs in the background and supports cancellation and refresh. Source versions are checked before navigation or objective saving; refresh stale results after editing source externally. Objects with unresolved coordinates open their source instead of framing a guessed location. Unavailable level geometry also falls back to the source viewer.

Feature regression checks, including fixture windows and source-preserving objective saves:

```powershell
dotnet run --project .\Tests\ProjectFeatures\ProjectFeatures.csproj -c Release
dotnet run --project .\Tests\ProjectFeatures\ProjectFeatures.csproj -c Release -- '\\wsl.localhost\Ubuntu-20.04\root\sm64'
```

### Editor Gallery

The main window, Level Editor, Actor Editor, and Music Editor below are desktop captures of Randm64. The Actor Editor is shown before selecting an actor so its directory field stays empty. Directory paths are not included in these screenshots. The remaining images are earlier UI renders with demonstration data.

<details open>
<summary><strong>Level Editor</strong></summary>

![Level Editor displaying the haunted house, objects, and viewport controls](docs/images/level-editor.png)

</details>

<details open>
<summary><strong>Actor Editor</strong></summary>

![Actor Editor with the actor browser and no actor selected](docs/images/actor-editor.png)

</details>

<details open>
<summary><strong>Music Editor</strong></summary>

![Music Editor displaying the Metal Cap sequence in the piano roll](docs/images/music-editor.png)

</details>

<details open>
<summary><strong>Behavior Builder</strong></summary>

![Behavior Builder with actor selection and source generation controls](docs/images/behavior-builder.png)

</details>

<details>
<summary><strong>Sound Editor</strong></summary>

![Sound Editor with sample browsing and replacement controls](docs/images/sound-editor.png)

</details>

<details>
<summary><strong>Warp Editor</strong></summary>

![Warp Editor with demonstration area connections](docs/images/warp-editor.png)

</details>

## System Requirements

These hardware tiers are estimates, not tested minimums or performance guarantees. Larger projects, high-resolution assets, Chaos operations, and game builds can require more memory and storage. Performance also depends on the selected decomp repository and build settings.

| Component | Minimum | Recommended | High End |
| --- | --- | --- | --- |
| Operating system | Windows 10 or 11, 64-bit | Windows 10 or 11, 64-bit | Windows 10 or 11, 64-bit |
| Processor | 2-core CPU, e.g. [Intel Core i3-6100](https://www.intel.com/content/www/us/en/products/sku/90729/intel-core-i36100-processor-3m-cache-3-70-ghz/specifications.html) | 4-core or faster CPU, e.g. [Intel Core i5-8400](https://www.intel.com/content/www/us/en/products/sku/126687/intel-core-i58400-processor-9m-cache-up-to-4-00-ghz/specifications.html) | 8-core or faster CPU, e.g. [AMD Ryzen 7 5700X](https://www.amd.com/en/support/downloads/drivers.html/processors/ryzen/ryzen-5000-series/amd-ryzen-7-5700x.html) |
| Memory | 4 GB RAM | 8 GB RAM | 16 GB RAM or more |
| Graphics | OpenGL 3.3 core-capable GPU, e.g. Intel HD Graphics 530 | OpenGL 3.3-capable GPU, e.g. Intel UHD Graphics 630 | OpenGL 3.3-capable dedicated GPU, e.g. [NVIDIA GeForce RTX 3060](https://www.nvidia.com/en-us/geforce/graphics-cards/30-series/rtx-3060-3060ti/) |
| Storage | 10 GB free | 20 GB free on an SSD | 40 GB or more free on an SSD |

The storage estimates include room for a decomp project, extracted assets, build output, and backups; actual needs vary by project. A working graphics driver is required for the 3D editors. Game builds need the toolchain required by the selected repository, generally through WSL or a supported MSYS2 setup. Framework-dependent releases also need the .NET 8 Desktop Runtime; building Randm64 from source needs the .NET 8 SDK or a compatible newer SDK.

## Getting Started

1. Install the .NET 8 Desktop Runtime to run Randm64, or the .NET 8 SDK to build it.
2. Prepare a compatible SM64 decompilation source project and its required assets. Follow that repository for extraction and build dependencies.
3. Build the SM64 project at least once so compiled assets are available to the level viewer.
4. Open Randm64 and select the project folder.
5. Choose a level and area, open an editor, and save your changes.
6. Rebuild the game to test source edits in your emulator or supported game target.

The 3D viewer requires working OpenGL support. Game compilation requires the toolchain expected by the selected repository, using WSL or a supported MSYS2 setup. Compatibility with modified repositories depends on their structure and features.

### Build Randm64 From Source

Run from the checkout root with the .NET 8 SDK or a compatible newer SDK:

```powershell
dotnet restore .\Randm64.csproj
dotnet build .\Randm64.csproj -c Release
.\bin\Release\net8.0-windows\Randm64.exe
```

You can also open `Randm64.csproj` in Visual Studio with the .NET desktop development workload.

## Live Editing With KernelN64

The initial bridge supports position, pitch, yaw, and roll edits for existing normal spawned objects and position/yaw edits for existing macro objects, including wooden signposts, in the active SM64 decomp build. Existing special objects, including castle grounds trees, support position edits; presets with a yaw field also support yaw. It requires a KernelN64 build that includes live editing protocol version 3. Update both applications together and restart KernelN64 after installing the new build.

1. Build the SM64 project and load that exact `.z64` ROM in KernelN64.
2. Enter the same level and area that you opened in Randm64.
3. Select an existing normal, macro, or supported special object in the Level Editor.
4. Click **Connect Live Editing** and choose the corresponding build ROM. Its `.map` file must be beside it.
5. Edit the transform. Click **Disconnect Live Editing** to end the session.

If KernelN64 is not running, live editing shows a waiting status and retries automatically. Open the updated KernelN64 build, load the selected ROM, and enter the selected level and area. Use **Stop Live Editing** to cancel waiting.

The bridge checks the running ROM hash, level, area, and original spawn record. Missing or ambiguous objects stop the session. Communication stays on the local machine through port `64640`, and memory writes run on the emulated CPU thread.

Macro objects use their original packed spawn record, preset, behavior, and parameters for matching. Dialog IDs remain unchanged. Pitch and roll for macro and special objects, custom special layouts with unknown extra fields, Mario, new objects, and live geometry or collision editing are not supported. Unresolved macro parameter expressions are shown as unavailable instead of guessed. Special objects are matched by preset behavior, model, area, parent, parameters, and expected transform. Each successful update advances the expected transform only after acknowledgment. Ambiguous or externally moved objects are rejected; reload the area and reconnect to restore a source-based match. Behaviors can overwrite transforms during gameplay. Save source edits and rebuild for persistent changes. Offline bridge checks pass; gameplay validation is still outstanding.

## Project Builds and Recovery

The build dialog detects supported targets and options from the selected repository. Compatible incremental builds retain existing output; configuration changes may require rebuilding. Build output includes command results and timing information.

For Windows-hosted projects, Randm64 can offer to import a copy into WSL for faster builds. The original folder remains available, and the two copies do not synchronize automatically.

Keep your decomp project under version control. Editors retain backups where supported, and Chaos runs record recovery information. Review failures before retrying or restoring files so later changes are preserved.

## Settings and Local Files

User configuration is stored under `Documents/Randm64/`:

- `Settings`: Application settings and Chaos presets.
- `Data/Plugins`: Local plugin assemblies.
- `Data/Patchs`: Source patch files.
- `Data/Custom Objects`: Behavior templates.

## Troubleshooting

- **Missing level textures:** Build the selected decomp project first and verify its extracted assets are available.
- **A tool is unavailable:** Check that the selected level or repository contains the source files that tool requires.
- **Live editing rejects the build:** Select the exact ROM loaded in KernelN64 and its matching map, then reconnect after rebuilding.
- **A source edit does not appear in game:** Save the edit, rebuild, and load the new game output.

Report reproducible problems through [GitHub Issues](https://github.com/denukernel/Randm64/issues). Include the tool, steps, repository variant, and relevant error text. Remove personal paths or other private data from logs before sharing them.

## License

See [LICENSE](LICENSE) for the MIT license and copyright notice. Third-party notices are retained under `ThirdParty/`.
# Corruptor ROM build

In Build SM64 ROM, select **Also build a 24 MiB ROM for corruptors**. After
the normal N64 build, Randm64 builds a separate ROM with literal MIO0 actor and
level payloads and places `sm64.<version>.corruptor.z64` and
`sm64.<version>.corruptor.ranges.txt` beside the normal ROM. The report uses
inclusive hexadecimal file offsets for Vinesauce ROM Corruptor, identifies
actors by their source group, and excludes behavior scripts. Select one range
at a time; actor groups may contain several models. The ranges can still
contain display lists, collision, and other data that may cause crashes when
corrupted. These literal streams require more temporary memory while loading
than the original compressed streams, so runtime compatibility can vary by
build and emulator. If the ROM does not fit in 24 MiB or the build cannot be
validated, the corruptor build fails and the normal ROM remains available.

