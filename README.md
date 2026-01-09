# OGNES - Nintendo Entertainment System Emulator

OGNES is a feature-rich NES emulator written in C# running on .NET 10.0. It leverages [Hexa.NET](https://github.com/HexaEngine/Hexa.NET) for cross-platform windowing, graphics (OpenGL), and UI (ImGui).

## Demo Video

[![OGNES Demo Video](https://i9.ytimg.com/vi_webp/_m4NELf4hfc/mq2.webp?sqp=CJyyhMsG-oaymwEmCMACELQB8quKqQMa8AEB-AHeCIAC0AWKAgwIABABGDAgUChyMA8=&rs=AOn4CLCvshMaSD98Q-hyqcAfB2CY8kB8dg)](https://www.youtube.com/watch?v=_m4NELf4hfc)

## Features\Goals

### Emulation Accuracy
- **CPU**: Cycle-accurate 6502 (2A03) emulation.
- **PPU**: Pixel-accurate 2C02 graphics emulation with support for standard rendering.
- **APU**: Sound emulation using NAudio.
- **Mappers Support**: Wide range of mappers supported including:
  - Mapper 0 (NROM)
  - Mapper 1 (MMC1)
  - Mapper 2 (UxROM)
  - Mapper 3 (CNROM)
  - Mapper 4 (MMC3)
  - Mapper 7 (AxROM)
  - Mapper 9 (MMC2)
  - Mapper 10 (MMC4)
  - Mapper 71 (Camerica)

### Graphics & Audio
- **NTSC CRT Simulation**: High-quality NTSC signal simulation via GPU shaders for an authentic retro look.
- **ImGui Interface**: Modern, responsive user interface overlay.
- **Palette Management**: Custom palette support and viewing tools.

### Input
- **Controller Support**: Support for both Keyboard and Gamepads (via GLFW).
- **Macro System**: Record and playback input macros (`JoypadMacroExecutor`).
- **Configurable Controls**: Remap buttons to your preference.

### Game Library
- **Library Manager**: Scan and organize your game collection.
- **Metadata**: Automatic detection of game details (Mapper, Battery, Mirroring).
- **Box Art**: Integration with IGDB for downloading game cover art.

### Debugging & Development Tools
OGNES includes a comprehensive suite of debugging tools perfect for ROM hacking or emulator development:
- **CPU Monitor**: Real-time instruction logging and state monitoring (`CpuLogWindow`).
- **PPU Debugger**: Inspect Nametables, Pattern Tables, and Sprites (`PpuDebugWindow`).
- **Memory Viewer**: Hex editor for inspecting and modifying RAM/WRAM (`MemoryViewerWindow`).
- **Palette Viewer**: Visual inspection of currently loaded palettes (`PalettePanel`).

### Cheats
- **Cheat Manager**: Search for cheats in memory (Exact value, Changed/Unchanged, Increased/Decreased).
- **Game Genie**: Support for entering Game Genie codes.

## Requirements

- **Runtime**: .NET 10.0 SDK/Runtime.
- **Graphics**: OpenGL 3.3 supported graphics card.
- **OS**: Windows (tested), likely cross-platform due to .NET and GLFW usage.

## building & Running

1. **Prerequisites**: Ensure you have the .NET 10.0 SDK installed.
2. **Clone**: Clone the repository.
3. **Build**:
   ```bash
   dotnet build
   ```
4. **Run**:
   ```bash
   dotnet run --project OGNES.csproj
   ```

## Controls (Default)

*Mappings can be configured in the Settings window.*

- **D-Pad**: Arrow Keys
- **A**: Z
- **B**: X
- **Start**: Enter
- **Select**: Right Shift

## License

[MIT License](LICENSE) (Assuming open source, adjust as necessary)
