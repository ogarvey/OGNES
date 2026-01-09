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

                if (ppu?.Joypad != null)
                {
                    bool zapper = ppu.Joypad.ZapperEnabled;
                    if (ImGui.Checkbox("Enable Zapper (Port 2)", ref zapper))
                    {
                        ppu.Joypad.ZapperEnabled = zapper;
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
                        
                        var imageStartPos = ImGui.GetCursorScreenPos() + new Vector2(cursorX, cursorY);

                        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(cursorX, cursorY));
                        
                        // Fix for Upcaling Issue:
                        // ImGui.Image uses the TextureId provided.
                        // The TextureId here is either _textureId (NTSC) or _upscaledTexture (Passthrough)
                        // If it is _upscaledTexture, it has Linear/Linear filters set in Program.cs.
                        // We rely on ImGui to draw this texture quad using those filters.
                        ImGui.Image(new ImTextureRef(null, textureId), displaySize);

                        // Zapper Input
                        if (ppu?.Joypad != null && ppu.Joypad.ZapperEnabled)
                        {
                            var io = ImGui.GetIO();
                            var mousePos = io.MousePos;

                            if (mousePos.X >= imageStartPos.X && mousePos.X < imageStartPos.X + displaySize.X &&
                                mousePos.Y >= imageStartPos.Y && mousePos.Y < imageStartPos.Y + displaySize.Y)
                            {
                                int nesX = (int)((mousePos.X - imageStartPos.X) / scale);
                                int nesY = (int)((mousePos.Y - imageStartPos.Y) / scale);

                                ppu.Joypad.ZapperX = Math.Clamp(nesX, 0, NesScreenWidth - 1);
                                ppu.Joypad.ZapperY = Math.Clamp(nesY, 0, NesScreenHeight - 1);
                                ppu.Joypad.Trigger = io.MouseDown[0];
                            }
                            else
                            {
                                ppu.Joypad.Trigger = false;
                            }
                        }
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
