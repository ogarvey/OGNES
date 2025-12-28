using System.Numerics;
using Hexa.NET.ImGui;
using GBOG.ImGuiTexInspect.Core;

namespace GBOG.ImGuiTexInspect.Annotations
{
    /// <summary>
    /// Interface for annotation drawing classes.
    /// Annotations are drawn over texels to provide visual information.
    /// </summary>
    public interface IAnnotation
    {
        /// <summary>
        /// Draw an annotation for a single texel.
        /// </summary>
        /// <param name="drawList">ImGui draw list to add drawing commands to</param>
        /// <param name="texel">Texel coordinates (center of the texel)</param>
        /// <param name="texelsToPixels">Transform from texel space to screen pixel space</param>
        /// <param name="value">RGBA color value of the texel (0-1 range)</param>
        void DrawAnnotation(ImDrawListPtr drawList, Vector2 texel, Transform2D texelsToPixels, Vector4 value);
    }
}
