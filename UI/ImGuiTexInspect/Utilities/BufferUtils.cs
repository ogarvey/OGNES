using System;
using System.Numerics;
using OGNES.UI.ImGuiTexInspect.Core;

namespace OGNES.UI.ImGuiTexInspect.Utilities
{
    /// <summary>
    /// Utility methods for working with texture data buffers
    /// </summary>
    public static class BufferUtils
    {
        /// <summary>
        /// Get the color value of a specific texel from a buffer
        /// </summary>
        /// <param name="buffer">Buffer descriptor containing texture data</param>
        /// <param name="x">X coordinate of the texel</param>
        /// <param name="y">Y coordinate of the texel</param>
        /// <returns>RGBA color value (0-1 range), or zero if outside buffer bounds</returns>
        public static unsafe Vector4 GetTexel(BufferDesc buffer, int x, int y)
        {
            // Check bounds
            if (x < buffer.StartX || x >= buffer.StartX + buffer.Width ||
                y < buffer.StartY || y >= buffer.StartY + buffer.Height)
            {
                return Vector4.Zero;
            }

            // Calculate position in array
            int offset = buffer.LineStride * (y - buffer.StartY) + buffer.Stride * (x - buffer.StartX);

            if (buffer.DataFloat != null)
            {
                float* texel = buffer.DataFloat + offset;
                
                return new Vector4(
                    texel[buffer.Red],
                    buffer.ChannelCount >= 2 ? texel[buffer.Green] : 0,
                    buffer.ChannelCount >= 3 ? texel[buffer.Blue] : 0,
                    buffer.ChannelCount >= 4 ? texel[buffer.Alpha] : 0
                );
            }
            else if (buffer.DataUInt8 != null)
            {
                byte* texel = buffer.DataUInt8 + offset;
                
                // Map from [0,255] to [0,1]
                return new Vector4(
                    texel[buffer.Red] / 255.0f,
                    buffer.ChannelCount >= 2 ? texel[buffer.Green] / 255.0f : 0,
                    buffer.ChannelCount >= 3 ? texel[buffer.Blue] / 255.0f : 0,
                    buffer.ChannelCount >= 4 ? texel[buffer.Alpha] / 255.0f : 0
                );
            }

            return Vector4.Zero;
        }
    }
}
