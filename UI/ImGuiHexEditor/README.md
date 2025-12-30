# ImGui Hex Editor

A full-featured hex editor control for C# using Hexa.NET.ImGui, converted from the C++ imgui_hex_editor library.

## Features

- **Hex byte editing** with keyboard input (0-9, A-F)
- **ASCII column display** showing printable characters
- **Address column** with customizable formatting
- **Selection support** with mouse drag and keyboard navigation
- **Copy to clipboard** (Ctrl+C) in hex format
- **Keyboard navigation** with arrow keys
- **Customizable display options**:
  - Bytes per line (auto or manual)
  - Column separators
  - Lowercase/uppercase hex digits
  - Show/hide address and ASCII columns
  - Dim zero bytes
- **Read-only mode** for viewing
- **Highlighting system** with:
  - Single byte highlighting via callback
  - Range highlighting
  - Automatic contrast colors
  - Border rendering options
- **Efficient rendering** using ImGui's clipper for large files
- **Callbacks** for custom data reading/writing
- **Scrolling support** for large data buffers

## Files

- `HexEditor.cs` - Main hex editor control implementation
- `HexEditorState.cs` - State management for the editor
- `HexEditorHighlightFlags.cs` - Flags for highlight rendering
- `HexEditorClipboardFlags.cs` - Clipboard operation flags
- `HexEditorHighlightRange.cs` - Structure for highlight ranges

## Usage

### Basic Usage

```csharp
// Create state
var state = new HexEditorState
{
    Bytes = myByteArray,
    MaxBytes = myByteArray.Length,
    BytesPerLine = 16,
    ShowAddress = true,
    ShowAscii = true
};

// In your ImGui render loop
if (HexEditor.BeginHexEditor("MyHexEditor", state))
{
    HexEditor.EndHexEditor();
}
```

### With Custom Callbacks

```csharp
var state = new HexEditorState
{
    MaxBytes = fileSize,
    ReadCallback = (state, offset, buffer, size) =>
    {
        // Read data from your source
        return bytesRead;
    },
    WriteCallback = (state, offset, buffer, size) =>
    {
        // Write modified data
        return bytesWritten;
    }
};
```

### With Highlighting

```csharp
// Add highlight ranges
state.HighlightRanges.Add(new HexEditorHighlightRange
{
    From = 0x100,
    To = 0x1FF,
    Color = 0xFF00FF00, // Green
    Flags = HexEditorHighlightFlags.Apply | HexEditorHighlightFlags.Border
});

// Or use callback for dynamic highlighting
state.SingleHighlightCallback = (state, offset, out uint color, out uint textColor, out uint borderColor) =>
{
    if (IsSomeSpecialByte(offset))
    {
        color = 0xFFFF0000; // Red
        textColor = 0xFFFFFFFF;
        borderColor = 0xFF000000;
        return HexEditorHighlightFlags.Apply | HexEditorHighlightFlags.TextAutomaticContrast;
    }
    
    color = textColor = borderColor = 0;
    return HexEditorHighlightFlags.None;
};
```

## HexViewer Window

A ready-to-use window class `HexViewer` is included in `Windows/Viewers/` that demonstrates the hex editor control:

```csharp
var hexViewer = new HexViewer();
hexViewer.LoadFile("path/to/file.bin");

// Or load data directly
hexViewer.LoadData(byteArray, "MyData");

// In your window manager
hexViewer.Draw();
```

Features of HexViewer:
- File path display
- Size information
- Interactive options panel
- Selection status display
- Copy all to clipboard
- Full editing capabilities

## Key Bindings

- **Arrow Keys** - Navigate bytes
- **0-9, A-F** - Edit hex values (when not read-only)
- **Ctrl+C** - Copy selection to clipboard
- **Mouse Drag** - Select bytes
- **Mouse Click** - Position cursor

## Conversion Notes

This control was converted from the C++ `imgui_hex_editor` library with the following adaptations:

1. **API Compatibility** - Adjusted for Hexa.NET.ImGui C# bindings
2. **Memory Management** - Changed from raw pointers to byte arrays
3. **Delegates** - C++ function pointers converted to C# delegates
4. **ImGuiListClipper** - Updated to use managed wrapper API
5. **Key Input** - Adapted keyboard handling for Hexa.NET.ImGui
6. **Rendering** - All draw list operations ported to C# API

## License

Original C++ library: [imgui_hex_editor](https://github.com/yourrepo/imgui_hex_editor)
C# conversion: Part of OGNES.UI

