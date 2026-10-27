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

The initial bridge supports position, pitch, yaw, and roll edits for existing normal spawned objects in the active SM64 decomp build. It requires a KernelN64 build that includes the live editing bridge.

1. Build the SM64 project and load that exact `.z64` ROM in KernelN64.
2. Enter the same level and area that you opened in Randm64.
3. Select an existing normal object in the Level Editor.
4. Click **Connect Live Editing** and choose the corresponding build ROM. Its `.map` file must be beside it.
5. Edit the transform. Click **Disconnect Live Editing** to end the session.

The bridge checks the running ROM hash, level, area, and original spawn record. Missing or ambiguous objects stop the session. Communication stays on the local machine through port `64640`, and memory writes run on the emulated CPU thread.

Macro objects, special objects, Mario, new objects, and live geometry or collision editing are not supported by this version. Behaviors can overwrite transforms during gameplay. Save source edits and rebuild for persistent changes. Offline bridge checks pass; gameplay validation is still outstanding.

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

The GitHub checkout excludes local test harnesses, build output, diagnostic runs, IDE state, and user settings. Documentation images are kept under `docs/images/`. Local validation harnesses can remain in the development workspace without being tracked.

## Troubleshooting

- **Missing level textures:** Build the selected decomp project first and verify its extracted assets are available.
- **A tool is unavailable:** Check that the selected level or repository contains the source files that tool requires.
- **Live editing rejects the build:** Select the exact ROM loaded in KernelN64 and its matching map, then reconnect after rebuilding.
- **A source edit does not appear in game:** Save the edit, rebuild, and load the new game output.

Report reproducible problems through [GitHub Issues](https://github.com/denukernel/Randm64/issues). Include the tool, steps, repository variant, and relevant error text. Remove personal paths or other private data from logs before sharing them.

## License

See [LICENSE](LICENSE) for the MIT license and copyright notice. Third-party notices are retained under `ThirdParty/`.
