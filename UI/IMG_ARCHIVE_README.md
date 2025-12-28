# IMG Archive Format Support

The `.IMG` file format from AniMagic DOS games is now supported through a hybrid implementation that treats these files as both archives and image collections with animation capabilities.

## Overview

IMG files contain multiple images organized into three categories:
- **Images**: Regular images
- **Switch Images**: Alternative/switched images
- **Masked Images**: Images with transparency using special encoding (V1 or V2)

## Architecture

### 1. Archive Extractor (`ImgExtractor`)
Located: `Formats/Extractors/AniMagic/ImgExtractor.cs`

Implements `IArchiveExtractor` to allow IMG files to be browsed in the Archive Viewer:
- Lists all images in the archive
- Extracts individual images as raw data
- Organizes images into logical folders (images/, switch/, masked/)

### 2. Image Decoder (`ImgImageDecoder`)
Located: `Formats/Decoders/ImgImageDecoder.cs`

Implements `IImageDecoder` to decode individual images from the archive:
- Handles raw indexed data
- Decodes masked V1 format (I.M. Meen style - 64x64 with bounds)
- Decodes masked V2 format (Chill Manor style - variable dimensions)
- Automatically detects format based on data structure
- Converts indexed data to RGBA using provided palette

### 3. Specialized Viewer (`ImgArchiveViewer`)
Located: `Controls/ImgArchiveViewer.cs`

Provides an advanced UI for viewing IMG files with multiple modes:

#### **Grid View**
- Thumbnail grid of all images
- Adjustable thumbnail size (32-256 pixels)
- Color-coded by type (Image, Switch, Masked)
- Click to view individual image

#### **List View**
- Tabular view with preview thumbnails
- Shows type, index, dimensions, and name
- Sortable columns

#### **Single View**
- Full-size display of selected image
- Previous/Next navigation
- Auto-scaling (up to 8x zoom)
- Centered display

#### **Animation View**
- Playback controls (Play, Pause, Stop)
- Adjustable FPS (1-60 fps)
- Loop option
- Frame counter
- Auto-scaling display

## Usage

### In Archive Viewer

1. Open an IMG file in Archive Viewer
2. Select "IMG Archive" format
3. Browse images organized by type
4. Extract individual images or all images
5. Send images to Image Viewer for detailed viewing

### In Image Viewer (Future Integration)

The `ImgArchiveViewer` component can be integrated into the Image Viewer to provide:
- Automatic detection of IMG files
- Switch to specialized archive mode
- Full animation playback capabilities

## Format Detection

The system automatically detects:

**Masked V2 Format** (Chill Manor):
- Starts with height/width as ushort
- Dimensions between 1-2000 pixels
- Contains footer offsets and compressed pixel data

**Masked V1 Format** (I.M. Meen):
- 64x64 pixel images
- Starts with bounds (left, top, right, bottom)
- Contains footer offsets pointing to pixel data

**Raw Indexed**:
- Simple indexed pixel data
- Requires dimensions to be specified
- No special encoding

## Implementation Notes

### Archive Type Selection

Currently defaults to `AniMagicArchiveType.IMMeen`. For production use, you should:
1. Auto-detect based on file header
2. Allow user to select archive type
3. Store archive type preference per file

### Palette Requirements

All IMG images require a palette to decode. The palette should be:
- Loaded from the same archive (common case)
- Loaded from external `.PAL` file
- Selected from known game palettes

### Transparency Handling

- Index 0 is treated as transparent in masked images
- Regular images use index 0 as a normal color
- Transparency can be toggled via the decoder's `useTransparency` parameter

## Example: Loading an IMG File

```csharp
// In your window/viewer class
var imgViewer = new ImgArchiveViewer(gl, formatManager);

// Load IMG file with palette
var imgData = File.ReadAllBytes("SPRITES.IMG");
var paletteData = File.ReadAllBytes("GAME.PAL");

// Decode palette
var palette = formatManager.DecodePalette("vga_6bit", paletteData);

// Load into viewer
imgViewer.LoadImgFile(
    imgData, 
    "SPRITES.IMG", 
    AniMagicArchiveType.IMMeen,
    palette
);

// Render in your UI
imgViewer.Render();

// Cleanup when done
imgViewer.Dispose();
```

## Integration Points

### FileTypeHandler
- Add `.img` extension detection
- Route to either ArchiveViewer or ImageViewer based on user preference

### Program.cs OpenWith
- Add "Image Viewer (IMG Archive)" option
- Instantiate ImgArchiveViewer when selected

### ImageViewer Enhancement
- Detect IMG files in LoadFromFile
- Switch to ImgArchiveViewer mode
- Provide toggle between standard and archive modes

## Future Enhancements

1. **Auto-detect Archive Type**
   - Analyze file structure
   - Identify game-specific patterns

2. **Thumbnail Caching**
   - Cache decoded textures
   - Faster grid/list view rendering

3. **Export Options**
   - Export as PNG sequence
   - Export as animated GIF
   - Export as sprite sheet

4. **Batch Operations**
   - Select multiple images
   - Bulk export
   - Bulk format conversion

5. **Metadata Display**
   - Image dimensions per entry
   - Raw data size
   - Compression statistics

## Files Modified/Created

### New Files
- `Formats/Extractors/AniMagic/ImgExtractor.cs`
- `Formats/Decoders/ImgImageDecoder.cs`
- `Controls/ImgArchiveViewer.cs`
- `Controls/IMG_ARCHIVE_README.md` (this file)

### Modified Files
- `Formats/FormatRegistration.cs` - Registered IMG archive format
- `Formats/Decoders/RetroFormatDecoders.cs` - Added IMG decoder notes
