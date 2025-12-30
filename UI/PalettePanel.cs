//using Hexa.NET.ImGui;
//using System.Numerics;
//using OGNES.UI.Formats;

//namespace OGNES.UI
//{
//  /// <summary>
//  /// A panel for displaying and interacting with color palettes.
//  /// Based on the C++ palette panel implementation.
//  /// </summary>
//  public class PalettePanel
//  {
//    private Vector4[] _colors = Array.Empty<Vector4>();
//    private int _colorCount = 0;
//    private int _selectedPaletteIndex = -1;
//    private int _highlightPaletteIndex = -1;
//    private Vector2 _colorButtonSize = new Vector2(24, 24);
    
//    public int SelectedPaletteIndex => _selectedPaletteIndex;
//    public int HighlightPaletteIndex
//    {
//      get => _highlightPaletteIndex;
//      set => _highlightPaletteIndex = value;
//    }

//    public Vector2 ColorButtonSize
//    {
//      get => _colorButtonSize;
//      set => _colorButtonSize = value;
//    }

//    public event Action<int>? ColorSelected;

//    /// <summary>
//    /// Load palette colors from a DecodedPalette.
//    /// </summary>
//    public void LoadPalette(DecodedPalette palette)
//    {
//      _colorCount = palette.ColorCount;
//      _colors = new Vector4[_colorCount];

//      // Convert palette data to Vector4 colors
//      // Assuming Colors is in RGBA format with BytesPerColor bytes per color
//      for (int i = 0; i < _colorCount; i++)
//      {
//        int offset = i * palette.BytesPerColor;
        
//        if (palette.BytesPerColor >= 3)
//        {
//          float r = palette.Colors[offset] / 255f;
//          float g = palette.Colors[offset + 1] / 255f;
//          float b = palette.Colors[offset + 2] / 255f;
//          float a = palette.BytesPerColor >= 4 ? palette.Colors[offset + 3] / 255f : 1.0f;
//          if (a == 0.5f && (palette.FormatInfo?.Contains("PS2") ?? false))
//          {
//            // PS2 palettes may have inverted alpha
//            a = 1.0f - a;
//          }
//          _colors[i] = new Vector4(r, g, b, a);
//        }
//        else
//        {
//          _colors[i] = new Vector4(1, 0, 1, 1); // Magenta for invalid data
//        }
//      }

//      Console.WriteLine($"Loaded palette: {_colorCount} colors");
//    }

//    /// <summary>
//    /// Load palette colors from raw RGB or RGBA byte data.
//    /// </summary>
//    public void LoadPaletteFromBytes(byte[] colorData, int bytesPerColor)
//    {
//      _colorCount = colorData.Length / bytesPerColor;
//      _colors = new Vector4[_colorCount];

//      for (int i = 0; i < _colorCount; i++)
//      {
//        int offset = i * bytesPerColor;
        
//        if (bytesPerColor >= 3 && offset + bytesPerColor <= colorData.Length)
//        {
//          float r = colorData[offset] / 255f;
//          float g = colorData[offset + 1] / 255f;
//          float b = colorData[offset + 2] / 255f;
//          float a = bytesPerColor >= 4 ? colorData[offset + 3] / 255f : 1.0f;
          
//          _colors[i] = new Vector4(r, g, b, a);
//        }
//        else
//        {
//          _colors[i] = new Vector4(1, 0, 1, 1); // Magenta for invalid data
//        }
//      }

//      Console.WriteLine($"Loaded palette from bytes: {_colorCount} colors");
//    }

//    /// <summary>
//    /// Clear the palette.
//    /// </summary>
//    public void Clear()
//    {
//      _colors = Array.Empty<Vector4>();
//      _colorCount = 0;
//      _selectedPaletteIndex = -1;
//      _highlightPaletteIndex = -1;
//    }

//    /// <summary>
//    /// Render the palette panel.
//    /// </summary>
//    /// <param name="title">Title for the panel</param>
//    /// <returns>True if the panel is visible</returns>
//    public bool Render(string title)
//    {
//      if (_colorCount == 0)
//      {
//        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No palette loaded");
//        return false;
//      }

//      ImGui.TextColored(new Vector4(0.8f, 0.9f, 1.0f, 1.0f), $"Palette: {_colorCount} colors");
//      ImGui.Spacing();

//      var drawList = ImGui.GetWindowDrawList();
//      var backupFlags = drawList.Flags;
//      drawList.Flags &= ~ImDrawListFlags.AntiAliasedLines;

//      var redColor = ImGui.GetColorU32(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
//      var yellowColor = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
//      var darkRedColor = ImGui.GetColorU32(new Vector4(0.5f, 0.0f, 0.0f, 1.0f));

//      var windowWidth = ImGui.GetContentRegionAvail().X;
//      var globalCursorPos = ImGui.GetCursorScreenPos();
//      var startPos = globalCursorPos;
//      bool colorHovered = false;

//      for (int palIdx = 0; palIdx < _colorCount; palIdx++)
//      {
//        float borderWidth = 1.0f;
//        var v1 = new Vector2(globalCursorPos.X + borderWidth, globalCursorPos.Y + borderWidth);
//        var v2 = new Vector2(globalCursorPos.X + _colorButtonSize.X, globalCursorPos.Y + _colorButtonSize.Y);

//        // Draw the color rectangle
//        drawList.AddRectFilled(v1, v2, ImGui.GetColorU32(_colors[palIdx]));

//        // Invisible button for interaction
//        string id = $"##palitem-{palIdx}";
//        if (ImGui.InvisibleButton(id, _colorButtonSize))
//        {
//          _selectedPaletteIndex = palIdx;
//          ColorSelected?.Invoke(palIdx);
//          Console.WriteLine($"Selected palette color {palIdx}: RGB({_colors[palIdx].X * 255:F0}, {_colors[palIdx].Y * 255:F0}, {_colors[palIdx].Z * 255:F0})");
//        }

//        // Draw border based on state
//        if (!colorHovered && ImGui.IsItemHovered())
//        {
//          colorHovered = true;
//          drawList.AddRect(v1, v2, redColor);
          
//          // Show tooltip with color info
//          ImGui.SetTooltip($"Index: {palIdx}\nRGB: ({(int)(_colors[palIdx].X * 255)}, {(int)(_colors[palIdx].Y * 255)}, {(int)(_colors[palIdx].Z * 255)})");
//        }
//        else if (palIdx == _highlightPaletteIndex)
//        {
//          drawList.AddRect(v1, v2, yellowColor);
//        }
//        else if (palIdx == _selectedPaletteIndex)
//        {
//          drawList.AddRect(v1, v2, darkRedColor);
//        }

//        // Move cursor position
//        globalCursorPos.X += _colorButtonSize.X;
        
//        // Wrap to next line if needed
//        if (globalCursorPos.X > startPos.X + windowWidth - _colorButtonSize.X)
//        {
//          globalCursorPos.X = startPos.X;
//          globalCursorPos.Y += _colorButtonSize.Y;
//        }
        
//        ImGui.SetCursorScreenPos(globalCursorPos);
//      }

//      // Set cursor to final position
//      var cursorPos = new Vector2(startPos.X, globalCursorPos.Y + _colorButtonSize.Y);
//      ImGui.SetCursorScreenPos(cursorPos);

//      // Restore draw list flags
//      drawList.Flags = backupFlags;

//      // Show selected color info
//      if (_selectedPaletteIndex >= 0 && _selectedPaletteIndex < _colorCount)
//      {
//        ImGui.Spacing();
//        ImGui.Separator();
//        ImGui.Spacing();
        
//        ImGui.Text($"Selected: Index {_selectedPaletteIndex}");
//        var color = _colors[_selectedPaletteIndex];
//        ImGui.Text($"RGB: ({(int)(color.X * 255)}, {(int)(color.Y * 255)}, {(int)(color.Z * 255)})");
//        ImGui.Text($"Hex: #{(int)(color.X * 255):X2}{(int)(color.Y * 255):X2}{(int)(color.Z * 255):X2}");
        
//        // Color preview
//        ImGui.ColorButton("##selectedColor", color, ImGuiColorEditFlags.None, new Vector2(50, 50));
//      }

//      return true;
//    }

//    /// <summary>
//    /// Get the color at the specified palette index.
//    /// </summary>
//    public Vector4? GetColor(int index)
//    {
//      if (index >= 0 && index < _colorCount)
//      {
//        return _colors[index];
//      }
//      return null;
//    }

//    /// <summary>
//    /// Check if a palette is currently loaded.
//    /// </summary>
//    public bool HasPalette => _colorCount > 0;

//    /// <summary>
//    /// Get the number of colors in the palette.
//    /// </summary>
//    public int ColorCount => _colorCount;
//  }
//}
