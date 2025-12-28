using System.Numerics;
using Hexa.NET.ImGui;
using GBOG.ImGuiTexInspect.Core;

namespace GBOG.ImGuiTexInspect.Annotations
{
    /// <summary>
    /// Description of the current annotation context.
    /// Contains information needed to draw annotations on visible texels.
    /// </summary>
    public struct AnnotationDesc
    {
        /// <summary>
        /// ImGui draw list to add annotation drawings to
        /// </summary>
        public ImDrawListPtr DrawList;

        /// <summary>
        /// Size of the visible texel region (in texels)
        /// </summary>
        public Vector2 TexelViewSize;

        /// <summary>
        /// Top-left corner of visible region (in texel coordinates)
        /// </summary>
        public Vector2 TexelTopLeft;

        /// <summary>
        /// Buffer descriptor for cached texture data
        /// </summary>
        public BufferDesc Buffer;

        /// <summary>
        /// Transform from texel coordinates to screen pixel coordinates
        /// </summary>
        public Transform2D TexelsToPixels;
    }
}
