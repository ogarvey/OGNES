using Hexa.NET.ImGui;
using System.Numerics;
using OGNES.Components;

namespace OGNES.UI
{
    public unsafe class NesWindow
    {
        private const int NesScreenWidth = 256;
        private const int NesScreenHeight = 240;

        public void Draw(Ppu? ppu, uint textureId)
        {
            if (ImGui.Begin("Game"))
            {
                if (ppu?.Cartridge != null)
                {
                    if (ImGui.CollapsingHeader("ROM Info"))
                    {
                        var cart = ppu.Cartridge;
                        ImGui.Text($"File: {cart.FileName}");
                        ImGui.Text($"Mapper: {cart.MapperId} ({cart.MapperName})");
                        ImGui.Text($"PRG Banks: {cart.PrgBanks} ({cart.PrgBanks * 16} KB)");
                        ImGui.Text($"CHR Banks: {cart.ChrBanks} ({cart.ChrBanks * 8} KB)");
                        ImGui.Text($"Mirroring: {cart.MirrorMode}");
                        ImGui.Separator();
                    }
                }

                if (textureId != 0)
                {
                    var windowSize = ImGui.GetContentRegionAvail();
                    if (windowSize.X > 0 && windowSize.Y > 0)
                    {
                        float scale = Math.Min(windowSize.X / NesScreenWidth, windowSize.Y / NesScreenHeight);
                        var displaySize = new Vector2(NesScreenWidth * scale, NesScreenHeight * scale);
                        
                        // Center the image in the window
                        var cursorX = (windowSize.X - displaySize.X) * 0.5f;
                        var cursorY = (windowSize.Y - displaySize.Y) * 0.5f;
                        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(cursorX, cursorY));

                        ImGui.Image(new ImTextureRef(null, textureId), displaySize);
                    }
                }
                else
                {
                    ImGui.Text("No ROM loaded");
                }
            }
            ImGui.End();
        }
    }
}
