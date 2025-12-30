using System;
using System.Numerics;
using Hexa.NET.ImGui;

namespace OGNES.UI.ImGuiTexInspect.Utilities
{
    /// <summary>
    /// ImGui helper functions
    /// </summary>
    public static class ImGuiUtils
    {
        /// <summary>
        /// Draw a single-column table with one row for each string (used for displaying vectors)
        /// </summary>
        public static void TextVector(string title, string[] strings)
        {
            ImGui.BeginGroup();
            ImGui.SetNextItemWidth(50);
            
            var flags = ImGuiTableFlags.BordersOuter | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX;
            if (ImGui.BeginTable(title, 1, flags))
            {
                foreach (var str in strings)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(str);
                }
                ImGui.EndTable();
            }
            
            ImGui.EndGroup();
        }

        /// <summary>
        /// Push disabled UI style (grayed out, non-interactive)
        /// </summary>
        public static unsafe void PushDisabled()
        {
            var disabledColors = new[]
            {
                ImGuiCol.FrameBg,
                ImGuiCol.FrameBgActive,
                ImGuiCol.FrameBgHovered,
                ImGuiCol.Text,
                ImGuiCol.CheckMark
            };

            foreach (var colorId in disabledColors)
            {
                var colorPtr = ImGui.GetStyleColorVec4(colorId);
                var color = *colorPtr;
                color.W *= 0.5f; // Reduce alpha
                ImGui.PushStyleColor(colorId, color);
            }

            ImGui.BeginDisabled(true);
        }

        /// <summary>
        /// Pop disabled UI style
        /// </summary>
        public static void PopDisabled()
        {
            ImGui.PopStyleColor(5); // Pop 5 colors
            ImGui.EndDisabled();
        }

        /// <summary>
        /// Convert ImGui color (uint) to Vector4
        /// </summary>
        public static Vector4 ColorToVec4(uint color)
        {
            return new Vector4(
                ((color >> 0) & 0xFF) / 255.0f,
                ((color >> 8) & 0xFF) / 255.0f,
                ((color >> 16) & 0xFF) / 255.0f,
                ((color >> 24) & 0xFF) / 255.0f
            );
        }

        /// <summary>
        /// Convert Vector4 to ImGui color (uint)
        /// </summary>
        public static uint Vec4ToColor(Vector4 color)
        {
            uint r = (uint)(color.X * 255.0f);
            uint g = (uint)(color.Y * 255.0f);
            uint b = (uint)(color.Z * 255.0f);
            uint a = (uint)(color.W * 255.0f);
            return (a << 24) | (b << 16) | (g << 8) | r;
        }
    }
}
