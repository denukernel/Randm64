# Randm64

Randm64 is an advanced, professional modding platform and chaos orchestrator for Super Mario 64 decompilation projects. It extends beyond simple level editing, providing a complete suite of low-level ROM corruption, sequence translation, custom entity builders, and code patching engines.

<img width="946" height="687" alt="image" src="https://github.com/user-attachments/assets/6e51690d-5f91-4153-85bc-035a39bd6653" />

## Key Features

### 📐 3D Geometry and Level Editor
- **Real-Time Render**: High-performance visualization of level geometry, visual meshes, and collision triangles using OpenTK.
- **Object Manipulation**: Move, rotate, scale, or snap actors in 3D.
- **Drop to Ground**: Automatically drop selected entities directly onto collision surfaces below.
- **Splines and Warp Editor**: Build custom camera/movement paths and configure level connection warp portals.
- **Painting Editor**: Edit castle painting behavior scripts and texture assignments.

### 🔥 Chaos Engine and Goddard Corruptor
- **10 Goddard Face Modes**: Deform Mario's start screen face using safe algorithms (Face Meltdown, Face Stretch, Face Squash, Tremors, Melt, and Vertex Shuttering).
- **8 Animation Glitcher Modes**: Scramble coordinates, joint scaling, angle negations, and frame stuttering, or detach limbs.
- **8 Display List Randomizers**: Transform geometry rendering including Rainbow World rendering, UV texture mapping removals, and scaling distortions.
- **5 HUD Glitcher Modes**: Warp health meters, format tags, and glyph coordinates.
- **Preset Manager**: Save and load custom corruption profiles to the settings folder for quick selection.

### 🔀 Sound and Instrument Replacer
- **Independent Filtering**: Swap sounds across separate directories with custom rule filters.
- **Audition Preview**: Transcode AIFF audio samples to WAV in-memory and preview them inside the config dialog before compiling.
- **Search Filtering**: Filter targets and replacements on-the-fly with integrated text searches.

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

## Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A Super Mario 64 Decompilation project folder (like sm64, pc-port, coop).

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

### Does it work on Windows drives (C:, D:, etc.)?
Yes! You do not need to clone your repository inside the internal WSL filesystem (for example, `/home/username/`).
WSL automatically mounts your Windows drives under `/mnt/` (for example, `C:\` is mounted at `/mnt/c/`).
1. Place your SM64 decomp repository folder anywhere on your computer (like your Downloads folder: `C:\Users\...\Downloads\sm64`).
2. Place a valid `baserom.us.z64` (or target region) inside your repository root folder.
3. Open Randm64, select the repository folder, and compile. The build engine runs commands directly on the Windows host folder via the WSL mounts, and output files will compile directly on your host drive!

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
