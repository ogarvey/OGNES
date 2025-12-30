using Hexa.NET.ImGui;
using OGNES.Components;
using System.Numerics;
using System;
using GBOG.ImGuiTexInspect;
using GBOG.ImGuiTexInspect.Core;

namespace OGNES.UI
{
    public unsafe class PpuDebugWindow
    {
        public bool Visible = false;

        private int _selectedPalette = -1; // -1 for Auto
        private int _selectedSprite = 0;
        private byte[] _tilePalettes = new byte[512];

        public byte[] PatternTable0Buffer { get; } = new byte[128 * 128 * 4];
        public byte[] PatternTable1Buffer { get; } = new byte[128 * 128 * 4];
        public byte[] NameTableBuffer { get; } = new byte[512 * 480 * 4]; // 2x2 NameTables
        public byte[] SpriteAtlasBuffer { get; } = new byte[128 * 128 * 4]; // 8x8 sprites in 16x16 grid
        public byte[] SpritePreviewBuffer { get; } = new byte[64 * 64 * 4]; // Zoomed preview
        public byte[] SpriteLayerBuffer { get; } = new byte[256 * 240 * 4]; // Full screen sprite layer

        private static readonly uint[] NesPalette = {
            0x666666FF, 0x002A88FF, 0x1412A7FF, 0x3B00A4FF, 0x5C007EFF, 0x6E0040FF, 0x670600FF, 0x561D00FF, 0x333500FF, 0x0B4800FF, 0x005200FF, 0x004F08FF, 0x00404DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xADADADFF, 0x155FD9FF, 0x4240FFFF, 0x7527FEFF, 0xA01ACCFF, 0xB71E7BFF, 0xB53120FF, 0x994E00FF, 0x6B6D00FF, 0x388700FF, 0x0C9300FF, 0x008F32FF, 0x007C8DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0x64B0FFFF, 0x9290FFFF, 0xC676FFFF, 0xF36AFFFF, 0xFE6ECCFF, 0xFE8170FF, 0xEA9E22FF, 0xBCBE00FF, 0x88D800FF, 0x5CE430FF, 0x45E082FF, 0x48CDDEFF, 0x4F4F4FFF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0xC0DFFFFF, 0xD1D8FFFF, 0xE8CDFFFF, 0xFBCCFFFF, 0xFECDF5FF, 0xFED5D7FF, 0xFEE2B5FF, 0xEDEB9EFF, 0xD6F296FF, 0xC2F6AFFF, 0xB7F4CCFF, 0xB8ECF0FF, 0xBDBDBDFF, 0x000000FF, 0x000000FF
        };

        public void Draw(Ppu? ppu, uint pt0Tex, uint pt1Tex, uint ntTex, uint spriteAtlasTex, uint spritePreviewTex, uint spriteLayerTex)
        {
            if (!Visible || ppu == null) return;

            if (ImGui.Begin("PPU Debug", ref Visible))
            {
                if (ImGui.BeginTabBar("PpuTabs"))
                {
                    if (ImGui.BeginTabItem("Palettes"))
                    {
                        DrawPalettes(ppu);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Pattern Tables"))
                    {
                        DrawPatternTables(ppu, pt0Tex, pt1Tex);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Name Tables"))
                    {
                        DrawNameTables(ntTex);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Sprites"))
                    {
                        DrawSpriteLayer(spriteLayerTex);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("OAM"))
                    {
                        DrawOam(ppu, spriteAtlasTex, spritePreviewTex);
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private void DrawSpriteLayer(uint spriteLayerTex)
        {
            if (spriteLayerTex != 0)
            {
                ImGui.Text("Sprite Layer (256x240)");
                if (InspectorPanel.BeginInspectorPanel("SpriteLayer", (nint)spriteLayerTex, new Vector2(256, 240)))
                {
                    InspectorPanel.EndInspectorPanel();
                }
            }
        }

        private void DrawPalettes(Ppu ppu)
        {
            ImGui.Text("Background Palettes");
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    byte colorIdx = ppu.PaletteRam[i * 4 + j];
                    uint color = NesPalette[colorIdx & 0x3F];
                    Vector4 col = new Vector4(
                        ((color >> 24) & 0xFF) / 255.0f,
                        ((color >> 16) & 0xFF) / 255.0f,
                        ((color >> 8) & 0xFF) / 255.0f,
                        1.0f
                    );
                    ImGui.ColorButton($"BG {i} Color {j}", col, ImGuiColorEditFlags.None, new Vector2(20, 20));
                    if (j < 3) ImGui.SameLine();
                }
            }

            ImGui.Separator();
            ImGui.Text("Sprite Palettes");
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    byte colorIdx = ppu.PaletteRam[16 + i * 4 + j];
                    uint color = NesPalette[colorIdx & 0x3F];
                    Vector4 col = new Vector4(
                        ((color >> 24) & 0xFF) / 255.0f,
                        ((color >> 16) & 0xFF) / 255.0f,
                        ((color >> 8) & 0xFF) / 255.0f,
                        1.0f
                    );
                    ImGui.ColorButton($"SP {i} Color {j}", col, ImGuiColorEditFlags.None, new Vector2(20, 20));
                    if (j < 3) ImGui.SameLine();
                }
            }
        }

        private void DrawPatternTables(Ppu ppu, uint pt0Tex, uint pt1Tex)
        {
            ImGui.Text("Palette Selection:");
            if (ImGui.RadioButton("Auto", _selectedPalette == -1))
            {
                _selectedPalette = -1;
            }
            ImGui.SameLine();
            for (int i = 0; i < 8; i++)
            {
                bool selected = _selectedPalette == i;
                if (ImGui.RadioButton($"{(i < 4 ? "BG" : "SP")} {i % 4}", selected))
                {
                    _selectedPalette = i;
                }
                if (i < 7) ImGui.SameLine();
            }

            if (ImGui.BeginTable("PatternTableLayout", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableNextColumn();
                if (pt0Tex != 0)
                {
                    ImGui.Text("Pattern Table 0 ($0000)");
                    if (InspectorPanel.BeginInspectorPanel("PT0", (nint)pt0Tex, new Vector2(128, 128)))
                    {
                        InspectorPanel.EndInspectorPanel();
                    }
                }
                
                ImGui.TableNextColumn();
                if (pt1Tex != 0)
                {
                    ImGui.Text("Pattern Table 1 ($1000)");
                    if (InspectorPanel.BeginInspectorPanel("PT1", (nint)pt1Tex, new Vector2(128, 128)))
                    {
                        InspectorPanel.EndInspectorPanel();
                    }
                }
                ImGui.EndTable();
            }
        }

        private void DrawNameTables(uint ntTex)
        {
            if (ntTex != 0)
            {
                ImGui.Text("Name Tables ($2000, $2400, $2800, $2C00)");
                if (InspectorPanel.BeginInspectorPanel("NameTables", (nint)ntTex, new Vector2(512, 480)))
                {
                    InspectorPanel.EndInspectorPanel();
                }
            }
        }

        private void DrawOam(Ppu ppu, uint spriteAtlasTex, uint spritePreviewTex)
        {
            if (ImGui.BeginTable("OamLayout", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableNextColumn();
                ImGui.Text("OAM Entries");
                if (ImGui.BeginTable("OamTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 30);
                    ImGui.TableSetupColumn("Img", ImGuiTableColumnFlags.WidthFixed, 20);
                    ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 30);
                    ImGui.TableSetupColumn("Tile", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("Attr", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 30);
                    ImGui.TableHeadersRow();

                    for (int i = 0; i < 64; i++)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        bool selected = _selectedSprite == i;
                        if (ImGui.Selectable($"{i}##sprite{i}", selected, ImGuiSelectableFlags.SpanAllColumns))
                        {
                            _selectedSprite = i;
                        }
                        
                        ImGui.TableNextColumn();
                        // Draw small sprite image from atlas
                        float u0 = (i % 16) / 16.0f;
                        float v0 = (i / 16) / 16.0f;
                        float u1 = u0 + (1.0f / 16.0f);
                        float v1 = v0 + (1.0f / 16.0f);
                        ImGui.Image(new ImTextureRef(null, spriteAtlasTex), new Vector2(16, 16), new Vector2(u0, v0), new Vector2(u1, v1));

                        ImGui.TableNextColumn(); ImGui.Text($"{ppu.Oam[i * 4 + 0]}");
                        ImGui.TableNextColumn(); ImGui.Text($"0x{ppu.Oam[i * 4 + 1]:X2}");
                        ImGui.TableNextColumn(); ImGui.Text($"0x{ppu.Oam[i * 4 + 2]:X2}");
                        ImGui.TableNextColumn(); ImGui.Text($"{ppu.Oam[i * 4 + 3]}");
                    }
                    ImGui.EndTable();
                }

                ImGui.TableNextColumn();
                ImGui.Text("Sprite Preview");
                if (spritePreviewTex != 0)
                {
                    if (InspectorPanel.BeginInspectorPanel("SpritePreview", (nint)spritePreviewTex, new Vector2(64, 64)))
                    {
                        InspectorPanel.EndInspectorPanel();
                    }
                }
                
                ImGui.Separator();
                ImGui.Text($"Selected Sprite: {_selectedSprite}");
                byte y = ppu.Oam[_selectedSprite * 4 + 0];
                byte tile = ppu.Oam[_selectedSprite * 4 + 1];
                byte attr = ppu.Oam[_selectedSprite * 4 + 2];
                byte x = ppu.Oam[_selectedSprite * 4 + 3];
                
                ImGui.Text($"Position: ({x}, {y})");
                ImGui.Text($"Tile ID: 0x{tile:X2}");
                ImGui.Text($"Attributes: 0x{attr:X2}");
                ImGui.Text($"- Palette: {attr & 0x03}");
                ImGui.Text($"- Priority: {((attr & 0x20) != 0 ? "Behind BG" : "In front of BG")}");
                ImGui.Text($"- Flip: {((attr & 0x40) != 0 ? "H" : "-")}{((attr & 0x80) != 0 ? "V" : "-")}");
                
                ImGui.EndTable();
            }
        }

        public void UpdateBuffers(Ppu? ppu)
        {
            if (ppu == null) return;
            if (_selectedPalette == -1)
            {
                UpdateTilePaletteMap(ppu);
            }
            UpdatePatternTable(ppu, 0, PatternTable0Buffer);
            UpdatePatternTable(ppu, 1, PatternTable1Buffer);
            UpdateNameTables(ppu, NameTableBuffer);
            UpdateSpriteAtlas(ppu, SpriteAtlasBuffer);
            UpdateSpritePreview(ppu, SpritePreviewBuffer);
            UpdateSpriteLayer(ppu, SpriteLayerBuffer);
        }

        private void UpdateTilePaletteMap(Ppu ppu)
        {
            Array.Clear(_tilePalettes, 0, _tilePalettes.Length);

            // Scan Name Tables
            for (int nt = 0; nt < 4; nt++)
            {
                for (int y = 0; y < 30; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        ushort addr = (ushort)(0x2000 + nt * 0x0400 + y * 32 + x);
                        byte tileId = ppu.PpuRead(addr);
                        
                        ushort attrAddr = (ushort)(0x2000 + nt * 0x0400 + 0x03C0 + (y / 4) * 8 + (x / 4));
                        byte attr = ppu.PpuRead(attrAddr);
                        int paletteIdx = (attr >> (((y % 4) / 2) * 4 + ((x % 4) / 2) * 2)) & 0x03;

                        int ptIdx = (ppu.PeekRegister(0x2000) & 0x10) != 0 ? 1 : 0;
                        _tilePalettes[ptIdx * 256 + tileId] = (byte)paletteIdx;
                    }
                }
            }

            // Scan OAM
            bool mode8x16 = (ppu.PeekRegister(0x2000) & 0x20) != 0;
            for (int i = 0; i < 64; i++)
            {
                byte tileId = ppu.Oam[i * 4 + 1];
                byte attr = ppu.Oam[i * 4 + 2];
                int paletteIdx = (attr & 0x03) + 4;

                int ptIdx;
                if (mode8x16)
                {
                    ptIdx = tileId & 0x01;
                    tileId &= 0xFE;
                    _tilePalettes[ptIdx * 256 + tileId] = (byte)paletteIdx;
                    _tilePalettes[ptIdx * 256 + tileId + 1] = (byte)paletteIdx;
                }
                else
                {
                    ptIdx = (ppu.PeekRegister(0x2000) & 0x08) != 0 ? 1 : 0;
                    _tilePalettes[ptIdx * 256 + tileId] = (byte)paletteIdx;
                }
            }
        }

        private void UpdateSpriteLayer(Ppu ppu, byte[] buffer)
        {
            if (ppu.Cartridge == null) return;

            // Clear buffer (transparent)
            Array.Clear(buffer, 0, buffer.Length);

            bool mode8x16 = (ppu.PeekRegister(0x2000) & 0x20) != 0;
            int spriteHeight = mode8x16 ? 16 : 8;

            // Draw sprites in reverse order (index 63 to 0)
            // Lower OAM index has higher priority, so we draw 63 first so 0 is on top.
            for (int i = 63; i >= 0; i--)
            {
                byte y = ppu.Oam[i * 4 + 0];
                byte tileId = ppu.Oam[i * 4 + 1];
                byte attr = ppu.Oam[i * 4 + 2];
                byte x = ppu.Oam[i * 4 + 3];

                if (y >= 239) continue; // Sprite is hidden

                int paletteIdx = (attr & 0x03) + 4;
                bool flipH = (attr & 0x40) != 0;
                bool flipV = (attr & 0x80) != 0;

                int ptIdx;
                if (mode8x16)
                {
                    ptIdx = tileId & 0x01;
                    tileId &= 0xFE;
                }
                else
                {
                    ptIdx = (ppu.PeekRegister(0x2000) & 0x08) != 0 ? 1 : 0;
                }

                for (int sy = 0; sy < spriteHeight; sy++)
                {
                    int row = sy;
                    if (flipV) row = spriteHeight - 1 - sy;

                    int currentTileId = tileId;
                    int tileRow = row;
                    if (mode8x16 && row >= 8)
                    {
                        currentTileId++;
                        tileRow -= 8;
                    }

                    byte lsb = 0, msb = 0;
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow), out lsb);
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow + 8), out msb);

                    for (int sx = 0; sx < 8; sx++)
                    {
                        int col = sx;
                        if (flipH) col = 7 - sx;

                        byte pixel = (byte)(((lsb >> (7 - col)) & 0x01) | (((msb >> (7 - col)) & 0x01) << 1));
                        
                        if (pixel != 0)
                        {
                            byte colorIdx = ppu.PaletteRam[paletteIdx * 4 + pixel];
                            uint color = NesPalette[colorIdx & 0x3F];

                            int px = x + sx;
                            int py = y + 1 + sy; // NES sprites are delayed by one scanline

                            if (px >= 0 && px < 256 && py >= 0 && py < 240)
                            {
                                int idx = (py * 256 + px) * 4;
                                buffer[idx + 0] = (byte)((color >> 24) & 0xFF);
                                buffer[idx + 1] = (byte)((color >> 16) & 0xFF);
                                buffer[idx + 2] = (byte)((color >> 8) & 0xFF);
                                buffer[idx + 3] = (byte)(color & 0xFF);
                            }
                        }
                    }
                }
            }
        }

        private void UpdatePatternTable(Ppu? ppu, int tableIdx, byte[] buffer)
        {
            if (ppu?.Cartridge == null) return;

            for (int tileY = 0; tileY < 16; tileY++)
            {
                for (int tileX = 0; tileX < 16; tileX++)
                {
                    int tileOffset = (tileY * 16 + tileX) * 16;
                    int tileId = tileY * 16 + tileX;
                    int paletteIdx = _selectedPalette;
                    if (paletteIdx == -1)
                    {
                        paletteIdx = _tilePalettes[tableIdx * 256 + tileId];
                    }

                    for (int row = 0; row < 8; row++)
                    {
                        byte lsb = 0, msb = 0;
                        ppu.Cartridge.PpuRead((ushort)(tableIdx * 0x1000 + tileOffset + row), out lsb);
                        ppu.Cartridge.PpuRead((ushort)(tableIdx * 0x1000 + tileOffset + row + 8), out msb);

                        for (int col = 0; col < 8; col++)
                        {
                            byte pixel = (byte)(((lsb >> (7 - col)) & 0x01) | (((msb >> (7 - col)) & 0x01) << 1));
                            
                            byte colorIdx = ppu.PaletteRam[(paletteIdx * 4) + pixel];
                            uint color = NesPalette[colorIdx & 0x3F];

                            int x = tileX * 8 + col;
                            int y = tileY * 8 + row;
                            int idx = (y * 128 + x) * 4;
                            buffer[idx + 0] = (byte)((color >> 24) & 0xFF);
                            buffer[idx + 1] = (byte)((color >> 16) & 0xFF);
                            buffer[idx + 2] = (byte)((color >> 8) & 0xFF);
                            buffer[idx + 3] = (byte)(color & 0xFF);
                        }
                    }
                }
            }
        }

        private void UpdateSpriteAtlas(Ppu ppu, byte[] buffer)
        {
            if (ppu.Cartridge == null) return;

            // Draw all 64 sprites into a 128x128 atlas (16x16 grid of 8x8 tiles)
            // Note: This only shows the first 8x8 of a sprite even if in 8x16 mode.
            for (int i = 0; i < 64; i++)
            {
                byte tileId = ppu.Oam[i * 4 + 1];
                byte attr = ppu.Oam[i * 4 + 2];
                int paletteIdx = (attr & 0x03) + 4; // Sprite palettes are 4-7

                int ptIdx = (ppu.PeekRegister(0x2000) & 0x08) != 0 ? 1 : 0;
                // In 8x16 mode, the PT is determined by the bit 0 of tileId
                bool mode8x16 = (ppu.PeekRegister(0x2000) & 0x20) != 0;
                if (mode8x16)
                {
                    ptIdx = tileId & 0x01;
                    tileId &= 0xFE;
                }

                int offsetX = (i % 16) * 8;
                int offsetY = (i / 16) * 8;

                for (int row = 0; row < 8; row++)
                {
                    byte lsb = 0, msb = 0;
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row), out lsb);
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row + 8), out msb);

                    for (int col = 0; col < 8; col++)
                    {
                        byte pixel = (byte)(((lsb >> (7 - col)) & 0x01) | (((msb >> (7 - col)) & 0x01) << 1));
                        
                        uint color = 0;
                        if (pixel != 0)
                        {
                            byte colorIdx = ppu.PaletteRam[paletteIdx * 4 + pixel];
                            color = NesPalette[colorIdx & 0x3F];
                        }

                        int px = offsetX + col;
                        int py = offsetY + row;
                        int idx = (py * 128 + px) * 4;
                        buffer[idx + 0] = (byte)((color >> 24) & 0xFF);
                        buffer[idx + 1] = (byte)((color >> 16) & 0xFF);
                        buffer[idx + 2] = (byte)((color >> 8) & 0xFF);
                        buffer[idx + 3] = (byte)(color & 0xFF);
                    }
                }
            }
        }

        private void UpdateSpritePreview(Ppu ppu, byte[] buffer)
        {
            if (ppu.Cartridge == null) return;

            // Clear buffer
            Array.Clear(buffer, 0, buffer.Length);

            int i = _selectedSprite;
            byte tileId = ppu.Oam[i * 4 + 1];
            byte attr = ppu.Oam[i * 4 + 2];
            int paletteIdx = (attr & 0x03) + 4;
            bool flipH = (attr & 0x40) != 0;
            bool flipV = (attr & 0x80) != 0;

            bool mode8x16 = (ppu.PeekRegister(0x2000) & 0x20) != 0;
            int spriteHeight = mode8x16 ? 16 : 8;
            int ptIdx;
            
            if (mode8x16)
            {
                ptIdx = tileId & 0x01;
                tileId &= 0xFE;
            }
            else
            {
                ptIdx = (ppu.PeekRegister(0x2000) & 0x08) != 0 ? 1 : 0;
            }

            // Center the sprite in the 64x64 preview
            int startX = 32 - 4;
            int startY = 32 - (spriteHeight / 2);

            for (int sy = 0; sy < spriteHeight; sy++)
            {
                int row = sy;
                if (flipV) row = spriteHeight - 1 - sy;

                int currentTileId = tileId;
                int tileRow = row;
                if (mode8x16 && row >= 8)
                {
                    currentTileId++;
                    tileRow -= 8;
                }

                byte lsb = 0, msb = 0;
                ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow), out lsb);
                ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow + 8), out msb);

                for (int sx = 0; sx < 8; sx++)
                {
                    int col = sx;
                    if (flipH) col = 7 - sx;

                    byte pixel = (byte)(((lsb >> (7 - col)) & 0x01) | (((msb >> (7 - col)) & 0x01) << 1));
                    
                    if (pixel != 0)
                    {
                        byte colorIdx = ppu.PaletteRam[paletteIdx * 4 + pixel];
                        uint color = NesPalette[colorIdx & 0x3F];

                        int px = startX + sx;
                        int py = startY + sy;
                        int idx = (py * 64 + px) * 4;
                        buffer[idx + 0] = (byte)((color >> 24) & 0xFF);
                        buffer[idx + 1] = (byte)((color >> 16) & 0xFF);
                        buffer[idx + 2] = (byte)((color >> 8) & 0xFF);
                        buffer[idx + 3] = (byte)(color & 0xFF);
                    }
                }
            }
        }

        private void UpdateNameTables(Ppu? ppu, byte[] buffer)
        {
            if (ppu == null) return;
            // Draw all 4 name tables into a 512x480 buffer
            for (int nt = 0; nt < 4; nt++)
            {
                int offsetX = (nt % 2) * 256;
                int offsetY = (nt / 2) * 240;

                for (int y = 0; y < 30; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        ushort addr = (ushort)(0x2000 + nt * 0x0400 + y * 32 + x);
                        byte tileId = ppu.PpuRead(addr);
                        
                        // Get attribute byte
                        ushort attrAddr = (ushort)(0x2000 + nt * 0x0400 + 0x03C0 + (y / 4) * 8 + (x / 4));
                        byte attr = ppu.PpuRead(attrAddr);
                        int paletteIdx = (attr >> (((y % 4) / 2) * 4 + ((x % 4) / 2) * 2)) & 0x03;

                        // For simplicity, use Pattern Table 0 or 1 based on PPUCTRL
                        // In a real viewer, we might want to toggle this.
                        int ptIdx = (ppu.PeekRegister(0x2000) & 0x10) != 0 ? 1 : 0;

                        // Draw 8x8 tile
                        for (int row = 0; row < 8; row++)
                        {
                            byte lsb = 0, msb = 0;
                            ppu.Cartridge?.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row), out lsb);
                            ppu.Cartridge?.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row + 8), out msb);

                            for (int col = 0; col < 8; col++)
                            {
                                byte pixel = (byte)(((lsb >> (7 - col)) & 0x01) | (((msb >> (7 - col)) & 0x01) << 1));
                                
                                byte colorIdx = ppu.PaletteRam[(pixel == 0) ? 0 : (paletteIdx * 4 + pixel)];
                                uint color = NesPalette[colorIdx & 0x3F];

                                int px = offsetX + x * 8 + col;
                                int py = offsetY + y * 8 + row;
                                int idx = (py * 512 + px) * 4;
                                buffer[idx + 0] = (byte)((color >> 24) & 0xFF);
                                buffer[idx + 1] = (byte)((color >> 16) & 0xFF);
                                buffer[idx + 2] = (byte)((color >> 8) & 0xFF);
                                buffer[idx + 3] = (byte)(color & 0xFF);
                            }
                        }
                    }
                }
            }
        }
    }
}
