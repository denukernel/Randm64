# Randm64 
<img width="64" height="64" alt="image" src="https://github.com/user-attachments/assets/11263792-9552-410c-80df-3d89a3d593d0" />


Randm64 is an advanced, professional modding platform and chaos orchestrator for Super Mario 64 decompilation projects. It extends beyond simple level editing, providing a complete suite of low-level ROM corruption, sequence translation, custom entity builders, and code patching engines.

<img width="979" height="588" alt="image" src="https://github.com/user-attachments/assets/3cb3ef9e-282e-4af6-b871-e38d07b346b0" />
<img width="1003" height="761" alt="image" src="https://github.com/user-attachments/assets/1ea9c546-fd8c-4f69-9aa6-1e470b694efc" />


## Key Features
<img width="128" height="128" alt="level_editor_icon" src="https://github.com/user-attachments/assets/7a0e20ea-42b7-49ff-bfd7-524dbefbb09a" />

### 📐 3D Geometry and Level Editor
- **Real-Time Render**: High-performance visualization of level geometry, visual meshes, and collision triangles using OpenTK.
- **Object Manipulation**: Move, rotate, scale, or snap actors in 3D.
- **Drop to Ground**: Automatically drop selected entities directly onto collision surfaces below.
- **Integrated Level Mesh Editor**: Edit collision vertices, collision triangles, and water boxes directly inside a sidebar tab of the main level editor window.
- **Viewport Selection Sync**: Ctrl+Clicking a collision triangle or vertex in the 3D viewport instantly switches the tab and highlights/selects the element in the list.
- **Keyboard Translation Tools**: Translate vertices, triangles, or water boxes in real-time along absolute axes using Arrow keys (`Left/Right/Up/Down`) and `PageUp/PageDown` (hold `Shift` for precision steps), with thread-safe live-redrawing in the viewport.
- **Splines and Warp Editor**: Build custom camera/movement paths and configure level connection warp portals.
- **Painting Editor**: Edit castle painting behavior scripts and texture assignments.

### 🔥 Chaos Engine and Goddard Corruptor
- **10 Goddard Face Modes**: Deform Mario's start screen face using safe algorithms (Face Meltdown, Face Stretch, Face Squash, Tremors, Melt, and Vertex Shuttering).
- **8 Animation Glitcher Modes**: Scramble coordinates, joint scaling, angle negations, and frame stuttering, or detach limbs.
- **8 Display List Randomizers**: Transform geometry rendering including Rainbow World rendering, UV texture mapping removals, and scaling distortions.
- **Display List Exclusion Mode**: Exclude specific display lists dynamically at runtime from 10 different complexity levels based on customizable triggers (Always Active, when Mario is Moving, or when Mario is Standing), matching Glide64 exclusion mechanics.
- **5 HUD Glitcher Modes**: Warp health meters, format tags, and glyph coordinates.
- **Start Level Chaos (File Select Warp)**: Warp directly to a custom starting level on a new save file, with automated cutscene bypassing for stable playability.
- **Preset Manager**: Save and load custom corruption profiles to the settings folder for quick selection.

<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/4657a59c-ea44-4dc1-8c82-7de8488d1bba" />
<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/c4515938-c9a9-4ca2-b6e5-8e28736f592a" />
<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/67009b2d-cd3d-4505-85b0-778a4a3d58ac" />
<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/93797155-a3fa-4bb5-8495-76767541a589" />
<img width="1280" height="720" alt="image" src="https://github.com/user-attachments/assets/51ecfcae-cb73-407a-8948-441cb1d3e4bc" />


### 🔀 Sound and Instrument Replacer
- **Independent Filtering**: Swap sounds across separate directories with custom rule filters.
- **Audition Preview**: Transcode AIFF audio samples to WAV in-memory and preview them inside the config dialog before compiling.
- **Search Filtering**: Filter targets and replacements on-the-fly with integrated text searches.
- **SFX Randomizer Pitching & Modes**: Apply custom pitch variations, shuffle sound identities, or select from 6 new sound randomization modes (Reverse playback, Glitched truncation, High/Low/Random pitch shifting, Swap-only).

### 🖼️ Dialogue-Based Texture Replacer
- **Unified Rule Selector**: Dedicated window to map targets recursively from both the `textures/` and `actors/` folders.
- **Image Previews**: View original and replacement texture images along with their pixel dimensions directly within the mapping interface.
- **Custom Image Mapping**: Browse and select external PNG files to automatically scale and overwrite target texture assets.

### 🧩 Custom Behavior Builder
- **Visual AI Constructor**: Design custom actor code patterns (Patrol, Chase Mario, Jump Repeatedly, Spin, Hover, or Stand Still) and configure properties (Solid, Damage, Grabbable, Collectible, Climbable).
- **Automated C Registration**: Generates clean C code functions, appends them to `data/behavior_data.c`, and registers declarations inside `include/behavior_data.h` automatically so they show up inside level editor selectors.

### 🔌 Extensibility Plugins API
- **Dynamic Plugin Loading**: Create C# plugin libraries that implement the `IPlugin` interface and drop them into the plugins folder.
- **Plugins Dashboard**: Load, view descriptions, execute, or hot-reload custom assemblies from the main sidebar.

### 🩹 Modding and System Utilities
- **WSL & Git Patching**: Revert source code changes or apply Git patch sets directly from the program.
- **Sequence Music Editor**: Edit track notes, instruments, and channels.
- **Unified Tools Dashboard**: 10-button grid layout to launch individual mod tools.

## Folder Configuration

Randm64 stores user preferences and mod assets inside the user's Documents folder:
`My Documents/Randm64/`

- **Settings**: Configuration profiles (`settings.json`) and saved Chaos presets.
- **Data/Plugins**: Extensibility dll files.
- **Data/Patchs**: Source patch scripts.
- **Data/Custom Objects**: Saved templates for the behavior builder.

## System Requirements

### Minimum Requirements
- **Operating System**: Windows 10 (64-bit)
- **Processor**: Intel Core 2 Duo / AMD Athlon 64 X2 or better (2.0 GHz)
- **Memory**: 4 GB RAM
- **Graphics**: Intel HD Graphics 4000 or any GPU supporting OpenGL 3.3
- **Storage**: 500 MB free disk space (plus space for SM64 repositories)
- **Software**: .NET 8.0 Runtime, WSL with Ubuntu 20.04 (if compiling ROMs)

### Recommended Requirements
- **Operating System**: Windows 10 or 11 (64-bit)
- **Processor**: Intel Core i5 / AMD Ryzen 5 or better
- **Memory**: 8 GB RAM
- **Graphics**: NVIDIA GeForce GTX 960 / AMD Radeon R9 280 or better (supporting OpenGL 4.3 or higher)
- **Storage**: 2 GB free SSD space
- **Software**: .NET 8.0 SDK, WSL 2 with Ubuntu 22.04 LTS

### High Performance / Maximum Setup
- **Operating System**: Windows 11 (64-bit)
- **Processor**: Intel Core i7 or i9 / AMD Ryzen 7 or 9 (enables fast parallel compiling via `make -j16`)
- **Memory**: 16 GB RAM or more
- **Graphics**: NVIDIA RTX series / AMD Radeon RX series (enables smooth high render distance settings up to 100k units and maximum FOV viewport detail)
- **Storage**: NVMe M.2 SSD (for optimal file scanning and asset search speed)
- **Software**: .NET 8.0 SDK, WSL 2 with custom compiled compilation tools

## Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A Super Mario 64 Decompilation project folder (like sm64, pc-port, coop). **Note**: The target repository must have been compiled/built at least once prior to opening the level editor. This ensures all compressed texture assets (such as MIO0 segments) are generated and available for extraction by the editor's renderer.

### Build and Run
1. Open the solution file `Randm64.csproj` in Visual Studio.
2. Select **Build -> Rebuild Solution**.
3. Run the project to configure your settings and start editing.

## WSL & Compilation Setup

### Installing WSL
If you plan to compile SM64 ROMs using the built-in Build menu:
1. Open PowerShell or Command Prompt as Administrator.
2. Run: `wsl --install`
3. Restart your computer. This installs Ubuntu by default. If you need a specific distro (such as Ubuntu 20.04), you can download it from the Microsoft Store or install it via command line.
4. Once Ubuntu starts, set up your username and password, then run the compilation dependency setup command inside the WSL terminal:
   ```bash
   sudo apt update && sudo apt install -y build-essential git binutils-mips-linux-gnu zlib1g-dev libaudiofile-dev pkg-config
   ```

## FAQ

### Does it work with the Super Mario 64 PC Port?
Yes! The 3D Level Editor, Custom Behavior Builder, and Sound Replacer work seamlessly with PC Port repositories (like `sm64ex`, `sm64-port`, and `sm64ex-coop` / `coop`). However, low-level ROM memory injections (such as Goddard face deforming) are designed specifically for N64 emulators/ROM compilation and may not apply to PC-native executables.

### Does it work on custom decompilation repositories (like Render96, Shindou, or custom hacks)?
Yes! As long as the repository maintains the standard folder structure of the SM64 decompilation (such as `levels/`, `actors/`, `textures/`, `sound/`), Randm64 can successfully parse, edit, and build your assets.

### Can it edit pre-compiled ROM Hacks (traditional `.z64` / `.n64` files)?
No. Randm64 is designed to operate on **source code repositories** (decompilation projects). This allows you to edit levels, behaviors, and sounds at the source level and build clean ROMs or native PC executables, which is much more stable and extensible than modifying compiled binaries.

### Why are textures not loading or showing up as black/missing in the 3D Level Editor?
You must compile/build your SM64 repository at least once before opening a level inside the editor. Decompilation repos store textures in custom formats that are processed and extracted into build binaries (such as `.mio0` compressed files) during compilation. The editor parses these compiled assets to render level textures; if the project has never been compiled, these assets will be missing.

### Does it work on Windows drives (C:, D:, etc.)?
Yes! You do not need to clone your repository inside the internal WSL filesystem (for example, `/home/username/`).
WSL automatically mounts your Windows drives under `/mnt/` (for example, `C:\` is mounted at `/mnt/c/`).
1. Place your SM64 decomp repository folder anywhere on your computer (like your Downloads folder: `C:\Users\...\Downloads\sm64`).
2. Place a valid `baserom.us.z64` (or target region) inside your repository root folder.
3. Open Randm64, select the repository folder, and compile. The build engine runs commands directly on the Windows host folder via the WSL mounts, and output files will compile directly on your host drive!

### Where are my settings and presets saved?
All settings, custom layouts, themes, and presets are saved to your user documents folder under `My Documents/Randm64/`. This makes it easy to back up or share presets with others.

### What if the compiled ROM or PC port game crashes at startup?
Because the Chaos Engine randomizes asset data and the Behavior Builder registers custom logic handlers, selecting unsupported behavior combinations or applying extreme corruption configurations can occasionally cause the game to freeze or crash. You can easily revert all source code modifications by clicking the **Git / WSL Patches** button in the dashboard and choosing to revert changes to restore your repository to a clean state. We are not responsible for any game crashes, save file loss, or build errors resulting from experimental modifications.

## Reporting Issues and Bugs
If you find any bugs, UI glitches, or errors within the Randm64 application itself, please file a report on our [GitHub Issues](https://github.com/denukernel/Randm64/issues) page. Please include:
- Steps to reproduce the bug.
- The repository version/branch you are editing (e.g., standard US decomp, pc-port, coop).
- Any log file traces or error windows that appeared.

## Technical Stack
- **C# / .NET 8**
- **WPF** (Windows Presentation Foundation)
- **OpenTK** (OpenGL bindings)
- **YamlDotNet** (Level metadata parsing)

## Older Level Editors
- [Quad64](https://github.com/DavidSM64/Quad64)
- Toad's Tool 64

## License
This project is licensed under the MIT License.
