using System.Numerics;

namespace OGNES.UI.ImGuiTexInspect.Core
{
    /// <summary>
    /// 2D transformation for converting between texel coordinates and screen pixel coordinates
    /// </summary>
    public struct Transform2D
    {
        /// <summary>Scale component (applied first)</summary>
        public Vector2 Scale;
        
        /// <summary>Translation component (applied after scale)</summary>
        public Vector2 Translate;

        public Transform2D(Vector2 scale, Vector2 translate)
        {
            Scale = scale;
            Translate = translate;
        }

        /// <summary>
        /// Transform a vector by this transform. Scale is applied first, then translation.
        /// </summary>
        public Vector2 Transform(Vector2 point)
        {
            return new Vector2(
                Scale.X * point.X + Translate.X,
                Scale.Y * point.Y + Translate.Y
            );
        }

        /// <summary>
        /// Operator overload for transforming a vector
        /// </summary>
        public static Vector2 operator *(Transform2D transform, Vector2 point)
        {
            return transform.Transform(point);
        }

        /// <summary>
        /// Return an inverse transform such that transform.Inverse() * transform * vector == vector
        /// </summary>
        public Transform2D Inverse()
        {
            var inverseScale = new Vector2(1.0f / Scale.X, 1.0f / Scale.Y);
            return new Transform2D(
                inverseScale,
                new Vector2(-inverseScale.X * Translate.X, -inverseScale.Y * Translate.Y)
            );
        }
    }
}
