
# <img width="120" height="120" alt="image" src="https://github.com/user-attachments/assets/79a5ea9a-a0c3-4454-9864-d30afd3448d1" /> Randm64


Powerful Level Management for SM64 Decompilation Project, Replacing the Old Level Viewer.

<img width="946" height="687" alt="image" src="https://github.com/user-attachments/assets/6e51690d-5f91-4153-85bc-035a39bd6653" />
<img width="563" height="521" alt="image" src="https://github.com/user-attachments/assets/9cbad077-9ac6-4140-bf38-c445543e0d7f" />
<img width="1000" height="756" alt="image" src="https://github.com/user-attachments/assets/5e98fc46-fafc-4cbb-9940-99f319fb18d4" />


## Key Features

- **3D Level Rendering**: High-performance visualization of level geometry using OpenTK.
- **Collision Mesh Support**: Parse and view `collision.inc.c` data with vertex and triangle counts.
- **Full Object Support**:
    - **Standard Objects**: Parsed from level scripts (`script.c`).
    - **Macro Objects**: Automatic parsing of `macro.inc.c` and preset resolution.
    - **Special Objects**: Extraction of trees, signs, and other modular pieces directly from collision data.
- **Dynamic Project Selection**: 100% portable. Select any SM64 decomp root folder and the viewer handles the rest.
- **Advanced Parsing**: Intelligent regex-based parsers that handle C-style comments, variable whitespace, and complex macro formats (`OBJECT_WITH_ACTS`, `MARIO_POS`, etc.).
- **Modern UI**: Clean WPF-based interface with glassmorphism aesthetics and dark mode support.
- **Object Manipulation**: An Level Editor with best features.

## Getting Started

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A Super Mario 64 Decompilation repository.

## Controls (3D Viewer)

- Check Level Editor how to use.


## Technical Stack

- **C# / .NET 8**
- **WPF** (Windows Presentation Foundation)
- **OpenTK** (OpenGL bindings)
- **YamlDotNet** (Level metadata parsing)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request or open an Issue.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Known Bugs
- Some levels like Inside Castle has no visual at all.
- Sometimes the GUI doesn't close.
- 3D Viewer has some screen bugs sometimes
- Some things might be unfinished.
