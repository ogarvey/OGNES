using System;
using System.Numerics;

namespace OGNES.UI.ImGuiTexInspect.Core
{
    /// <summary>
    /// Options for the custom texture shader used by the inspector
    /// </summary>
    public class ShaderOptions
    {
        /// <summary>
        /// 4x4 color transformation matrix (column-major).
        /// Final color = ColorTransform * Original color + ColorOffset
        /// </summary>
        public float[] ColorTransform { get; set; }
        
        /// <summary>
        /// Color offset vector added after matrix multiplication
        /// </summary>
        public float[] ColorOffset { get; set; }
        
        /// <summary>
        /// Background color used for alpha blending
        /// </summary>
        public Vector4 BackgroundColor { get; set; }

        /// <summary>
        /// If true, a checkerboard pattern will be used as background
        /// </summary>
        public bool CheckeredBackground { get; set; }
        
        /// <summary>
        /// If 1, color will be multiplied by alpha before blend stage (premultiplied alpha)
        /// </summary>
        public float PremultiplyAlpha { get; set; }
        
        /// <summary>
        /// If 1, fragment shader will always output alpha = 1
        /// </summary>
        public float DisableFinalAlpha { get; set; }
        
        /// <summary>
        /// If true, fragment shader will always sample from texel centers (nearest filtering)
        /// </summary>
        public bool ForceNearestSampling { get; set; }
        
        /// <summary>
        /// Width in UV coordinates of grid lines
        /// </summary>
        public Vector2 GridWidth { get; set; }

        /// <summary>
        /// Grid cell size expressed in texture texels. (1,1) draws per-texel grid; (8,8) draws an 8x8 "tile" grid.
        /// </summary>
        public Vector2 GridCellSizeTexels { get; set; }
        
        /// <summary>
        /// Color of grid lines (RGB + Alpha for visibility)
        /// </summary>
        public Vector4 GridColor { get; set; }

        public ShaderOptions()
        {
            ColorTransform = new float[16];
            ColorOffset = new float[4];
            ResetColorTransform();
            BackgroundColor = Vector4.Zero;
            PremultiplyAlpha = 0;
            DisableFinalAlpha = 0;
            ForceNearestSampling = false;
            GridWidth = Vector2.Zero;
            GridCellSizeTexels = Vector2.One;
            GridColor = Vector4.Zero;
        }

        /// <summary>
        /// Reset color transform to identity matrix
        /// </summary>
        public void ResetColorTransform()
        {
            Array.Clear(ColorTransform, 0, 16);
            Array.Clear(ColorOffset, 0, 4);
            
            // Set diagonal to 1 (identity matrix)
            for (int i = 0; i < 4; i++)
            {
                ColorTransform[i * 4 + i] = 1.0f;
            }
        }

        /// <summary>
        /// Create a copy of these shader options
        /// </summary>
        public ShaderOptions Clone()
        {
            var clone = new ShaderOptions
            {
                BackgroundColor = BackgroundColor,
                CheckeredBackground = CheckeredBackground,
                PremultiplyAlpha = PremultiplyAlpha,
                DisableFinalAlpha = DisableFinalAlpha,
                ForceNearestSampling = ForceNearestSampling,
                GridWidth = GridWidth,
                GridCellSizeTexels = GridCellSizeTexels,
                GridColor = GridColor
            };
            
            Array.Copy(ColorTransform, clone.ColorTransform, 16);
            Array.Copy(ColorOffset, clone.ColorOffset, 4);
            
            return clone;
        }
    }
}
