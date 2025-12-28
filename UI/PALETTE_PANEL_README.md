# Palette Panel

A C# ImGui control for displaying and interacting with color palettes, based on the C++ palette viewer implementation.

## Features

- **Display color palettes** in a grid layout
- **Interactive color selection** with visual feedback
- **Color highlighting** with customizable border colors
- **Hover tooltips** showing RGB values
- **Automatic wrapping** based on available width
- **Selected color info panel** displaying RGB, Hex values, and a color preview

## Usage

### Basic Setup

```csharp
using OGNES.UI;

// Create a palette panel
var palettePanel = new PalettePanel();

// Load palette from a DecodedPalette
DecodedPalette palette = formatManager.DecodePalette("vga_6bit", paletteData);
palettePanel.LoadPalette(palette);

// Or load from raw RGB/RGBA bytes
byte[] rgbData = new byte[] {
    255, 0, 0,      // Red
    0, 255, 0,      // Green
    0, 0, 255       // Blue
};
palettePanel.LoadPaletteFromBytes(rgbData, bytesPerColor: 3);

// Render in your ImGui window
palettePanel.Render("Palette");
```

### Integration with ImageViewer

The palette panel is automatically integrated into the ImageViewer:

```csharp
// When decoding an indexed image with a palette, the palette automatically loads
// No manual setup needed!

// Or load a palette independently
imageViewer.LoadPalette(decodedPalette);

// Or from bytes
imageViewer.LoadPaletteFromBytes(paletteBytes, 3);
```

### Color Selection Events

```csharp
palettePanel.ColorSelected += (index) =>
{
    var color = palettePanel.GetColor(index);
    if (color.HasValue)
    {
        Console.WriteLine($"Selected: RGB({color.Value.X * 255:F0}, " +
                         $"{color.Value.Y * 255:F0}, " +
                         $"{color.Value.Z * 255:F0})");
    }
};
```

### Customization

```csharp
// Adjust color swatch size
palettePanel.ColorButtonSize = new Vector2(32, 32); // Default is 24x24

// Highlight a specific color (yellow border)
palettePanel.HighlightPaletteIndex = 5;

// Clear highlighting
palettePanel.HighlightPaletteIndex = -1;
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `SelectedPaletteIndex` | `int` | Currently selected color index (-1 if none) |
| `HighlightPaletteIndex` | `int` | Index to highlight with yellow border (-1 for none) |
| `ColorButtonSize` | `Vector2` | Size of each color swatch (default: 24x24) |
| `HasPalette` | `bool` | Whether a palette is currently loaded |
| `ColorCount` | `int` | Number of colors in the palette |

### Methods

| Method | Description |
|--------|-------------|
| `LoadPalette(DecodedPalette)` | Load from a decoded palette |
| `LoadPaletteFromBytes(byte[], int)` | Load from raw RGB/RGBA bytes |
| `Render(string)` | Render the palette panel |
| `GetColor(int)` | Get color at specific index |
| `Clear()` | Clear the current palette |

## Visual Features

### Border Colors

- **Red border**: Hovered color
- **Yellow border**: Highlighted color (set via `HighlightPaletteIndex`)
- **Dark red border**: Selected color

### Tooltip

Hover over any color to see:
- Color index
- RGB values (0-255)

### Selected Color Info

When a color is selected, a panel shows:
- Index number
- RGB values (0-255)
- Hex color code
- Large color preview swatch (50x50)

## In ImageViewer

The palette panel appears in two locations within ImageViewer:

1. **Left panel** (when `ShowFormatSelector` is true):
   - Under "Palette Viewer" collapsing header
   - Always visible during format selection workflow

2. **Inspector Controls area** (when palette is loaded and `ShowFormatSelector` is false):
   - Shows palette even when not using format selector
   - Useful for quick palette reference

## Example Workflow

1. Open a raw indexed image file (.DAT, .BIN, etc.)
2. Select platform and indexed image format (e.g., "8-bit Indexed")
3. Select palette format (e.g., "VGA 6-bit RGB")
4. Configure palette data source and offset
5. Click "Decode with Selected Formats"
6. **Palette automatically appears** in the Palette Viewer panel
7. Click colors to select and view RGB/Hex values
8. Use the decoded image and palette together

## Notes

- Colors are stored as `Vector4` (RGBA with 0-1 range)
- Supports both RGB (3 bytes) and RGBA (4 bytes) formats
- Automatically wraps colors based on available width
- Grid layout with anti-aliased lines disabled for sharp pixel rendering
