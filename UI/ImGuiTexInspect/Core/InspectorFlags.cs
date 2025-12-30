using System;

namespace OGNES.UI.ImGuiTexInspect.Core
{
    /// <summary>
    /// Alpha blending mode for texture display
    /// </summary>
    public enum InspectorAlphaMode
    {
        /// <summary>Alpha is transparency - see ImGui panel background behind image</summary>
        ImGui,
        
        /// <summary>Alpha blends over a black background</summary>
        Black,
        
        /// <summary>Alpha blends over a white background</summary>
        White,
        
        /// <summary>Alpha blends over a checkerboard background</summary>
        Checkered,

        /// <summary>Alpha blends over a custom color</summary>
        CustomColor
    }

    /// <summary>
    /// Configuration flags for texture inspector behavior
    /// </summary>
    [Flags]
    public enum InspectorFlags : ulong
    {
        /// <summary>No special flags</summary>
        None = 0,
        
        /// <summary>Draw beyond the [0,1] UV range. What you see will depend on API and texture settings</summary>
        ShowWrap = 1 << 0,
        
        /// <summary>Normally we force nearest neighbour sampling when zoomed in. Set to disable this</summary>
        NoForceFilterNearest = 1 << 1,
        
        /// <summary>By default a grid is shown at high zoom levels. Set to disable</summary>
        NoGrid = 1 << 2,
        
        /// <summary>Disable tooltip on hover</summary>
        NoTooltip = 1 << 3,
        
        /// <summary>Scale to fill available space horizontally</summary>
        FillHorizontal = 1 << 4,
        
        /// <summary>Scale to fill available space vertically</summary>
        FillVertical = 1 << 5,
        
        /// <summary>By default texture data is read to CPU every frame for tooltip and annotations. Set to disable auto-read</summary>
        NoAutoReadTexture = 1 << 6,
        
        /// <summary>Horizontally flip the way the texture is displayed</summary>
        FlipX = 1 << 7,
        
        /// <summary>Vertically flip the way the texture is displayed</summary>
        FlipY = 1 << 8,
    }
}
