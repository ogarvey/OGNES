using System;
using System.Numerics;

namespace OGNES.UI.ImGuiTexInspect.Utilities
{
    /// <summary>
    /// Math utility functions for texture inspector
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Check if a flag is set in a flags enum
        /// </summary>
        public static bool HasFlag<T>(T value, T flag) where T : Enum
        {
            ulong valueNum = Convert.ToUInt64(value);
            ulong flagNum = Convert.ToUInt64(flag);
            return (valueNum & flagNum) == flagNum;
        }

        /// <summary>
        /// Set a flag in a flags enum
        /// </summary>
        public static T SetFlag<T>(T value, T flag) where T : Enum
        {
            ulong valueNum = Convert.ToUInt64(value);
            ulong flagNum = Convert.ToUInt64(flag);
            return (T)Enum.ToObject(typeof(T), valueNum | flagNum);
        }

        /// <summary>
        /// Clear a flag in a flags enum
        /// </summary>
        public static T ClearFlag<T>(T value, T flag) where T : Enum
        {
            ulong valueNum = Convert.ToUInt64(value);
            ulong flagNum = Convert.ToUInt64(flag);
            return (T)Enum.ToObject(typeof(T), valueNum & ~flagNum);
        }

        /// <summary>
        /// Proper modulus operator (not remainder like %)
        /// </summary>
        public static float Modulus(float a, float b)
        {
            return a - b * MathF.Floor(a / b);
        }

        /// <summary>
        /// Floor with correct behavior for negative numbers
        /// </summary>
        public static float FloorSigned(float f)
        {
            return (f >= 0 || (int)f == f) ? (int)f : (int)f - 1;
        }

        /// <summary>
        /// Round to nearest integer
        /// </summary>
        public static float Round(float f)
        {
            return FloorSigned(f + 0.5f);
        }

        /// <summary>
        /// Absolute value of a Vector2
        /// </summary>
        public static Vector2 Abs(Vector2 v)
        {
            return new Vector2(MathF.Abs(v.X), MathF.Abs(v.Y));
        }

        /// <summary>
        /// Floor each component of a Vector2
        /// </summary>
        public static Vector2 Floor(Vector2 v)
        {
            return new Vector2(MathF.Floor(v.X), MathF.Floor(v.Y));
        }

        /// <summary>
        /// Clamp a Vector2 between min and max
        /// </summary>
        public static Vector2 Clamp(Vector2 value, Vector2 min, Vector2 max)
        {
            return new Vector2(
                Math.Clamp(value.X, min.X, max.X),
                Math.Clamp(value.Y, min.Y, max.Y)
            );
        }

        /// <summary>
        /// Swap two values
        /// </summary>
        public static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }

        /// <summary>
        /// Calculate transform from texel coordinates to screen pixel coordinates
        /// </summary>
        public static Core.Transform2D GetTexelsToPixels(
            Vector2 screenTopLeft, 
            Vector2 screenViewSize, 
            Vector2 uvTopLeft, 
            Vector2 uvViewSize, 
            Vector2 textureSize)
        {
            Vector2 uvToPixel = screenViewSize / uvViewSize;

            var transform = new Core.Transform2D
            {
                Scale = uvToPixel / textureSize,
                Translate = new Vector2(
                    screenTopLeft.X - uvTopLeft.X * uvToPixel.X,
                    screenTopLeft.Y - uvTopLeft.Y * uvToPixel.Y
                )
            };

            return transform;
        }
    }
}
