using System;
using System.Numerics;
using Hexa.NET.ImGui;
using OGNES.UI.ImGuiTexInspect.Core;

namespace OGNES.UI.ImGuiTexInspect.Annotations
{
    /// <summary>
    /// Annotation that displays texel color values as text.
    /// Text is only drawn when zoomed in enough for it to fit within the texel.
    /// </summary>
    public class ValueText : IAnnotation
    {
        /// <summary>
        /// Format options for displaying texel values
        /// </summary>
        public enum Format
        {
            /// <summary>
            /// Single line hex string: #RRGGBBAA
            /// </summary>
            HexString,

            /// <summary>
            /// Four lines hex: R:#RR G:#GG B:#BB A:#AA
            /// </summary>
            BytesHex,

            /// <summary>
            /// Four lines decimal: R:RRR G:GGG B:BBB A:AAA
            /// </summary>
            BytesDec,

            /// <summary>
            /// Four lines float: R.RRR G.GGG B.BBB A.AAA
            /// </summary>
            Floats
        }

        private readonly int _textRowCount;
        private readonly int _textColumnCount;
        private readonly string _textFormatString;
        private readonly bool _formatAsFloats;

        /// <summary>
        /// Create a new ValueText annotation with the specified format
        /// </summary>
        public ValueText(Format format = Format.HexString)
        {
            switch (format)
            {
                case Format.HexString:
                    _textFormatString = "#%02X%02X%02X%02X";
                    _textColumnCount = 9;
                    _textRowCount = 1;
                    _formatAsFloats = false;
                    break;

                case Format.BytesHex:
                    _textFormatString = "R:#%02X\nG:#%02X\nB:#%02X\nA:#%02X";
                    _textColumnCount = 5;
                    _textRowCount = 4;
                    _formatAsFloats = false;
                    break;

                case Format.BytesDec:
                    _textFormatString = "R:%3d\nG:%3d\nB:%3d\nA:%3d";
                    _textColumnCount = 5;
                    _textRowCount = 4;
                    _formatAsFloats = false;
                    break;

                case Format.Floats:
                    _textFormatString = "%5.3f\n%5.3f\n%5.3f\n%5.3f";
                    _textColumnCount = 5;
                    _textRowCount = 4;
                    _formatAsFloats = true;
                    break;

                default:
                    goto case Format.HexString;
            }
        }

        /// <summary>
        /// Draw the value text annotation for a single texel
        /// </summary>
        public void DrawAnnotation(ImDrawListPtr drawList, Vector2 texel, Transform2D texelsToPixels, Vector4 value)
        {
            float fontHeight = ImGui.GetFontSize();
            // WARNING: This assumes monospace font with width = height/2
            // Works for default font but may not work for others
            float fontWidth = fontHeight / 2.0f;

            // Calculate size of text
            var textSize = new Vector2(_textColumnCount * fontWidth, _textRowCount * fontHeight);

            // Check if text fits in the texel
            if (textSize.X > MathF.Abs(texelsToPixels.Scale.X) || textSize.Y > MathF.Abs(texelsToPixels.Scale.Y))
            {
                // Not enough room - don't draw
                return;
            }

            // Choose black or white text based on brightness
            // Don't draw black text on dark background or vice versa
            float brightness = (value.X + value.Y + value.Z) * value.W / 3.0f;
            uint lineColor = brightness > 0.5f ? 0xFF000000 : 0xFFFFFFFF;

            string text;
            if (_formatAsFloats)
            {
                // Format as floats
                text = $"{value.X:F3}\n{value.Y:F3}\n{value.Z:F3}\n{value.W:F3}";
            }
            else
            {
                // Map [0,1] to [0,255] and clamp
                byte r = (byte)MathF.Round(Math.Clamp(value.X, 0.0f, 1.0f) * 255);
                byte g = (byte)MathF.Round(Math.Clamp(value.Y, 0.0f, 1.0f) * 255);
                byte b = (byte)MathF.Round(Math.Clamp(value.Z, 0.0f, 1.0f) * 255);
                byte a = (byte)MathF.Round(Math.Clamp(value.W, 0.0f, 1.0f) * 255);

                // Format based on the selected style
                if (_textFormatString.Contains("#%02X"))
                {
                    // Single line hex format: #RRGGBBAA
                    text = $"#{r:X2}{g:X2}{b:X2}{a:X2}";
                }
                else if (_textFormatString.Contains("#%02X"))
                {
                    // Multi-line hex format: R:#RR ...
                    text = $"R:#{r:X2}\nG:#{g:X2}\nB:#{b:X2}\nA:#{a:X2}";
                }
                else
                {
                    // Multi-line decimal format: R:RRR ...
                    text = $"R:{r,3}\nG:{g,3}\nB:{b,3}\nA:{a,3}";
                }
            }

            // Calculate pixel position (center of texel)
            Vector2 pixelCenter = texelsToPixels * texel;

            // Draw text centered on the texel
            drawList.AddText(pixelCenter - textSize * 0.5f, lineColor, text);
        }
    }
}
