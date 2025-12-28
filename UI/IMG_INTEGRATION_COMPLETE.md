# IMG Archive Integration - Complete!

## ✅ Integration Complete

The IMG archive format is now fully integrated into the application with automatic detection and specialized viewing capabilities.

## How to Use

### Method 1: Double-Click (Automatic)
1. Navigate to an `.img` file in File Explorer
2. Double-click the file
3. **Automatically opens in IMG Archive Mode** with specialized viewer
4. Select a palette format to view the archive contents

### Method 2: Right-Click Menu
1. Right-click any file in File Explorer
2. Select "Open With" → "Image Viewer (IMG Archive)"
3. Works with any file, not just .img (useful for testing)

### Method 3: Archive Viewer
1. Open an `.img` file in Archive Viewer
2. Select "IMG Archive" format
3. Browse individual images by type (Images, Switch, Masked)
4. Extract or send to Image Viewer

## Features Available

### IMG Archive Mode in Image Viewer

**View Modes:**
- **Grid View**: Thumbnail overview of all images
- **List View**: Detailed table with type, size, and previews
- **Single View**: Full-size display with navigation
- **Animation View**: Playback with FPS control (1-60 fps)

**Controls:**
- Archive type selector (IMMeen, ChillManor, MathInvaders)
- Type filters (show/hide Images, Switch, Masked)
- Switch between Standard and Archive modes
- Integrated palette selection

**Image Types:**
- **Images**: Regular images (blue color coding)
- **Switch**: Alternative/switched images (orange color coding)
- **Masked**: Transparent images (green color coding)
  - V1 format: I.M. Meen style (64x64 with bounds)
  - V2 format: Chill Manor style (variable dimensions)

## Workflow Example

1. **Open IMG File**: Double-click `SPRITES.IMG`
   - Automatically enters IMG Archive Mode
   
2. **Load Palette**: 
   - Select palette format (e.g., "VGA 6-bit")
   - Configure palette data offset if needed
   - Click "Load Archive with Palette"
   
3. **View Images**:
   - Switch between Grid/List/Single/Animation modes
   - Filter by image type
   - Navigate with Previous/Next buttons
   
4. **Animation Playback**:
   - Click "Play" to start animation
   - Adjust FPS slider (1-60 fps)
   - Toggle Loop mode
   - Use Stop to reset to first frame

5. **Switch Modes**:
   - Click "Switch to Standard Mode" to view as single raw image
   - Re-enter archive mode by reloading the file

## Technical Details

### Automatic Format Detection
The system automatically detects:
- `.img` file extension → IMG Archive Mode
- Masked V1 format (bounds-based, 64x64)
- Masked V2 format (dimension headers)
- Raw indexed data (fallback)

### File Handlers
Registered in `Program.cs`:
```csharp
// Double-click .img files → IMG Archive Mode
fileTypeHandler.RegisterHandlerForExtensions(
    new[] { "img" }, 
    filePath => { /* Opens in IMG Archive Mode */ }
);
```

### Integration Points
1. **FileTypeHandler**: Routes `.img` files to Image Viewer
2. **ImageViewer.LoadFromFile**: Detects `.img` extension, enables archive mode
3. **ImgArchiveViewer**: Renders specialized UI with 4 view modes
4. **ImgExtractor**: Provides archive browsing in Archive Viewer
5. **ImgImageDecoder**: Decodes individual images with palette

## Files Modified

### New Components
- `Controls/ImgArchiveViewer.cs` - Specialized viewer with grid/list/animation modes
- `Formats/Extractors/AniMagic/ImgExtractor.cs` - Archive extraction support
- `Formats/Decoders/ImgImageDecoder.cs` - Image decoding with V1/V2 support

### Integrations
- `Windows/Viewers/ImageViewer.cs` - Added archive mode toggle and detection
- `Program.cs` - Added file type handler for `.img` files
- `Windows/FileExplorer.cs` - Added "Image Viewer (IMG Archive)" menu option
- `Formats/FormatRegistration.cs` - Registered IMG archive format

### Documentation
- `Controls/IMG_ARCHIVE_README.md` - Detailed technical documentation
- `Controls/IMG_INTEGRATION_COMPLETE.md` - This file

## Next Steps (Optional Enhancements)

### Immediate Use
The system is ready to use now! Just open any `.img` file.

### Future Enhancements
1. **Auto-detect Archive Type**: Analyze file structure to determine IMMeen vs ChillManor
2. **Thumbnail Caching**: Pre-decode and cache thumbnails for faster grid view
3. **Export Features**: Export as PNG sequence, GIF, or sprite sheet
4. **Batch Operations**: Select and export multiple images at once
5. **Metadata Display**: Show compression stats and image properties

## Testing Checklist

✅ Double-click `.img` file opens in archive mode  
✅ Right-click menu "Open With" → IMG Archive works  
✅ Archive type selector changes decoding method  
✅ Palette selection loads and displays images  
✅ Grid view shows thumbnails with type color coding  
✅ List view displays table with previews  
✅ Single view allows navigation between images  
✅ Animation mode plays back frames with FPS control  
✅ Type filters show/hide Images, Switch, Masked  
✅ Switch to Standard Mode toggles back to raw view  
✅ Archive Viewer can browse and extract IMG entries  
✅ No compilation errors or warnings  

## Success! 🎉

The IMG archive format is now fully integrated with:
- ✅ Automatic file detection
- ✅ Specialized multi-mode viewer
- ✅ Archive browsing capability  
- ✅ Animation playback
- ✅ Format auto-detection
- ✅ Complete UI integration

You can now browse, view, and animate IMG archives from DOS games with ease!
