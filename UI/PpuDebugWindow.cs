using Hexa.NET.ImGui;
using OGNES.Components;
using System.Numerics;
using System;
using OGNES.UI.ImGuiTexInspect;
using OGNES.UI.ImGuiTexInspect.Core;
using Hexa.NET.ImGui.Widgets.Dialogs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;

namespace OGNES.UI
{
    public unsafe class PpuDebugWindow
    {
        public bool Visible = false;
        public Action? OnSettingsChanged;

        private int _selectedPalette = -1; // -1 for Auto
        private int _selectedSprite = 0;
        private int _selectedNtPatternTable = -1; // -1 for Auto (PPUCTRL)
        private byte[] _tilePalettes = new byte[512];
        private bool _viewChrRom = false;
        private int _pt0Bank = 0;
        private int _pt1Bank = 1;

        private bool _inspectShowGrid = true;
        private bool _inspectShowTooltip = true;
        private bool _inspectAutoReadTexture = true;
        private bool _inspectForceNearest = true;
        private InspectorAlphaMode _inspectAlphaMode = InspectorAlphaMode.ImGui;

        private readonly SaveFileDialog _exportPt0Dialog = new();
        private readonly SaveFileDialog _exportPt1Dialog = new();
        private readonly SaveFileDialog _exportNtDialog = new();
        private readonly SaveFileDialog _exportSpriteLayerDialog = new();
        private readonly SaveFileDialog _exportSpritePreviewDialog = new();

        private enum ExportRequest
        {
            None,
            PatternTable0,
            PatternTable1,
            NameTable,
            SpriteLayer,
            SpritePreview
        }

        private ExportRequest _exportRequest = ExportRequest.None;
        private AppSettings? _settings;

        public byte[] PatternTable0Buffer { get; } = new byte[128 * 128 * 4];
        public byte[] PatternTable1Buffer { get; } = new byte[128 * 128 * 4];
        public byte[] NameTableBuffer { get; } = new byte[512 * 480 * 4]; // 2x2 NameTables
        public byte[] SpriteAtlasBuffer { get; } = new byte[128 * 128 * 4]; // 8x8 sprites in 16x16 grid
        public byte[] SpritePreviewBuffer { get; } = new byte[64 * 64 * 4]; // Zoomed preview
        public byte[] SpriteLayerBuffer { get; } = new byte[256 * 240 * 4]; // Full screen sprite layer

        private InspectorFlags GetInspectorFlags()
        {
            InspectorFlags flags = InspectorFlags.None;
            if (!_inspectShowTooltip) flags |= InspectorFlags.NoTooltip;
            if (!_inspectShowGrid) flags |= InspectorFlags.NoGrid;
            if (!_inspectAutoReadTexture) flags |= InspectorFlags.NoAutoReadTexture;
            if (!_inspectForceNearest) flags |= InspectorFlags.NoForceFilterNearest;
            return flags;
        }

        private static readonly InspectorFlags ManagedInspectorFlags =
            InspectorFlags.NoTooltip |
            InspectorFlags.NoGrid |
            InspectorFlags.ShowWrap |
            InspectorFlags.NoAutoReadTexture |
            InspectorFlags.NoForceFilterNearest;

        private void ApplyNextInspectorSettings(InspectorFlags desired, InspectorAlphaMode alphaMode, Vector2 gridSize)
        {
            var toSet = desired;
            var toClear = ManagedInspectorFlags & ~desired;
            InspectorPanel.SetNextPanelFlags(toSet, toClear);
            InspectorPanel.SetNextPanelAlphaMode(alphaMode);
            InspectorPanel.SetNextPanelGridCellSize(gridSize);
            InspectorPanel.SetNextPanelMinimumGridScale(1.0f);
        }

        private void DrawInspectorOptions()
        {
            if (ImGui.CollapsingHeader("Inspector Options"))
            {
                ImGui.Checkbox("Tooltip", ref _inspectShowTooltip);
                ImGui.SameLine();
                ImGui.Checkbox("Grid", ref _inspectShowGrid);
                ImGui.SameLine();
                
                string[] alphaModes = { "ImGui Background", "Black", "White", "Checkered", "Custom Color" };
                int alphaMode = (int)_inspectAlphaMode;
                ImGui.SetNextItemWidth(150);
                if (ImGui.Combo("Alpha Mode", ref alphaMode, alphaModes, alphaModes.Length))
                {
                    _inspectAlphaMode = (InspectorAlphaMode)Math.Clamp(alphaMode, 0, 4);
                }

                ImGui.Checkbox("Auto Read Texture", ref _inspectAutoReadTexture);
                ImGui.SameLine();
                ImGui.Checkbox("Force Nearest", ref _inspectForceNearest);
            }
        }

        private void HandleExports()
        {
            _exportPt0Dialog.Draw(ImGuiWindowFlags.None);
            _exportPt1Dialog.Draw(ImGuiWindowFlags.None);
            _exportNtDialog.Draw(ImGuiWindowFlags.None);
            _exportSpriteLayerDialog.Draw(ImGuiWindowFlags.None);
            _exportSpritePreviewDialog.Draw(ImGuiWindowFlags.None);
        }

        private void ExportCallback(object? sender, DialogResult result)
        {
            if (result != DialogResult.Ok)
            {
                _exportRequest = ExportRequest.None;
                return;
            }

            SaveFileDialog? dialog = sender as SaveFileDialog;
            if (dialog == null)
            {
                _exportRequest = ExportRequest.None;
                return;
            }

            string path = dialog.SelectedFile;
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
            {
                path = Path.Combine(dialog.CurrentFolder, path);
            }

            if (string.IsNullOrEmpty(path))
            {
                _exportRequest = ExportRequest.None;
                return;
            }

            byte[]? buffer = null;
            int w = 0, h = 0;
            switch (_exportRequest)
            {
                case ExportRequest.PatternTable0: buffer = PatternTable0Buffer; w = 128; h = 128; break;
                case ExportRequest.PatternTable1: buffer = PatternTable1Buffer; w = 128; h = 128; break;
                case ExportRequest.NameTable: buffer = NameTableBuffer; w = 512; h = 480; break;
                case ExportRequest.SpriteLayer: buffer = SpriteLayerBuffer; w = 256; h = 240; break;
                case ExportRequest.SpritePreview: buffer = SpritePreviewBuffer; w = 64; h = 64; break;
            }

            if (buffer != null)
            {
                ExportBufferToFile(path, buffer, w, h);
                
                if (_settings != null)
                {
                    _settings.LastExportDirectory = Path.GetDirectoryName(path);
                    OnSettingsChanged?.Invoke();
                }
            }

            _exportRequest = ExportRequest.None;
        }

        private void ExportBufferToFile(string path, byte[] buffer, int width, int height)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) path += ".png";
                
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var image = Image.LoadPixelData<Rgba32>(buffer, width, height);
                image.Save(path);
                Console.WriteLine($"Successfully exported image to: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to export image to {path}: {ex.Message}");
            }
        }

        private static readonly uint[] NesPalette = {
            0x666666FF, 0x002A88FF, 0x1412A7FF, 0x3B00A4FF, 0x5C007EFF, 0x6E0040FF, 0x670600FF, 0x561D00FF, 0x333500FF, 0x0B4800FF, 0x005200FF, 0x004F08FF, 0x00404DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xADADADFF, 0x155FD9FF, 0x4240FFFF, 0x7527FEFF, 0xA01ACCFF, 0xB71E7BFF, 0xB53120FF, 0x994E00FF, 0x6B6D00FF, 0x388700FF, 0x0C9300FF, 0x008F32FF, 0x007C8DFF, 0x000000FF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0x64B0FFFF, 0x9290FFFF, 0xC676FFFF, 0xF36AFFFF, 0xFE6ECCFF, 0xFE8170FF, 0xEA9E22FF, 0xBCBE00FF, 0x88D800FF, 0x5CE430FF, 0x45E082FF, 0x48CDDEFF, 0x4F4F4FFF, 0x000000FF, 0x000000FF,
            0xFFFEFFFF, 0xC0DFFFFF, 0xD1D8FFFF, 0xE8CDFFFF, 0xFBCCFFFF, 0xFECDF5FF, 0xFED5D7FF, 0xFEE2B5FF, 0xEDEB9EFF, 0xD6F296FF, 0xC2F6AFFF, 0xB7F4CCFF, 0xB8ECF0FF, 0xBDBDBDFF, 0x000000FF, 0x000000FF
        };

        private void ShowExportDialog(SaveFileDialog dialog, ExportRequest request)
        {
            if (_settings?.LastExportDirectory != null && Directory.Exists(_settings.LastExportDirectory))
            {
                dialog.CurrentFolder = _settings.LastExportDirectory;
            }
            _exportRequest = request;
            dialog.Show(ExportCallback);
        }

        public void Draw(Ppu? ppu, AppSettings settings, uint pt0Tex, uint pt1Tex, uint ntTex, uint spriteAtlasTex, uint spritePreviewTex, uint spriteLayerTex)
        {
            _settings = settings;
            if (!Visible || ppu == null) return;

            if (ImGui.Begin("PPU Debug", ref Visible))
            {
                DrawInspectorOptions();
                ImGui.Separator();

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

            HandleExports();
        }

        private void DrawSpriteLayer(uint spriteLayerTex)
        {
            if (spriteLayerTex != 0)
            {
                ImGui.Text("Sprite Layer (256x240)");
                ImGui.SameLine();
                if (ImGui.Button("Export##SpriteLayer"))
                {
                    ShowExportDialog(_exportSpriteLayerDialog, ExportRequest.SpriteLayer);
                }

                ApplyNextInspectorSettings(GetInspectorFlags(), _inspectAlphaMode, new Vector2(8, 8));
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
            ImGui.Text("Source:");
            ImGui.SameLine();
            if (ImGui.RadioButton("PPU Bus", !_viewChrRom)) _viewChrRom = false;
            ImGui.SameLine();
            if (ImGui.RadioButton("CHR ROM", _viewChrRom)) _viewChrRom = true;

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
                var inspectorFlags = GetInspectorFlags();

                ImGui.TableNextColumn();
                if (pt0Tex != 0)
                {
                    ImGui.Text("Pattern Table 0 ($0000)");
                    if (_viewChrRom && ppu.Cartridge != null)
                    {
                        int maxBanks = Math.Max(1, ppu.Cartridge.ChrRomLength / 4096);
                        ImGui.SliderInt("Bank##PT0", ref _pt0Bank, 0, maxBanks - 1);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Export##PT0"))
                    {
                        ShowExportDialog(_exportPt0Dialog, ExportRequest.PatternTable0);
                    }

                    ApplyNextInspectorSettings(inspectorFlags, _inspectAlphaMode, new Vector2(8, 8));
                    if (InspectorPanel.BeginInspectorPanel("PT0", (nint)pt0Tex, new Vector2(128, 128)))
                    {
                        InspectorPanel.EndInspectorPanel();
                    }
                }
                
                ImGui.TableNextColumn();
                if (pt1Tex != 0)
                {
                    ImGui.Text("Pattern Table 1 ($1000)");
                    if (_viewChrRom && ppu.Cartridge != null)
                    {
                        int maxBanks = Math.Max(1, ppu.Cartridge.ChrRomLength / 4096);
                        ImGui.SliderInt("Bank##PT1", ref _pt1Bank, 0, maxBanks - 1);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Export##PT1"))
                    {
                        ShowExportDialog(_exportPt1Dialog, ExportRequest.PatternTable1);
                    }

                    ApplyNextInspectorSettings(inspectorFlags, _inspectAlphaMode, new Vector2(8, 8));
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
                ImGui.SameLine();
                if (ImGui.Button("Export##NT"))
                {
                    ShowExportDialog(_exportNtDialog, ExportRequest.NameTable);
                }

                ImGui.Text("Pattern Table:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Auto##NT", _selectedNtPatternTable == -1)) _selectedNtPatternTable = -1;
                ImGui.SameLine();
                if (ImGui.RadioButton("PT 0##NT", _selectedNtPatternTable == 0)) _selectedNtPatternTable = 0;
                ImGui.SameLine();
                if (ImGui.RadioButton("PT 1##NT", _selectedNtPatternTable == 1)) _selectedNtPatternTable = 1;

                ApplyNextInspectorSettings(GetInspectorFlags(), _inspectAlphaMode, new Vector2(8, 8));
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
                    ImGui.SameLine();
                    if (ImGui.Button("Export##SpritePreview"))
                    {
                        ShowExportDialog(_exportSpritePreviewDialog, ExportRequest.SpritePreview);
                    }

                    ApplyNextInspectorSettings(GetInspectorFlags(), _inspectAlphaMode, new Vector2(8, 8));
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
                        byte tileId = ppu.PeekVram(addr);
                        
                        ushort attrAddr = (ushort)(0x2000 + nt * 0x0400 + 0x03C0 + (y / 4) * 8 + (x / 4));
                        byte attr = ppu.PeekVram(attrAddr);
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
                    if (_viewChrRom)
                    {
                        int bank = (ptIdx == 0) ? _pt0Bank : _pt1Bank;
                        int addr = bank * 4096 + currentTileId * 16 + tileRow;
                        lsb = ppu.Cartridge.ReadChrByte(addr);
                        msb = ppu.Cartridge.ReadChrByte(addr + 8);
                    }
                    else
                    {
                        ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow), out lsb);
                        ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow + 8), out msb);
                    }

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
                                buffer[idx + 3] = (byte)0xFF; // Alpha
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
                        
                        if (_viewChrRom)
                        {
                            int bank = (tableIdx == 0) ? _pt0Bank : _pt1Bank;
                            int addr = bank * 4096 + tileOffset + row;
                            lsb = ppu.Cartridge.ReadChrByte(addr);
                            msb = ppu.Cartridge.ReadChrByte(addr + 8);
                        }
                        else
                        {
                            ppu.Cartridge.PpuRead((ushort)(tableIdx * 0x1000 + tileOffset + row), out lsb);
                            ppu.Cartridge.PpuRead((ushort)(tableIdx * 0x1000 + tileOffset + row + 8), out msb);
                        }

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
                            buffer[idx + 3] = (byte)0xFF; // Alpha
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
                    if (_viewChrRom)
                    {
                        int bank = (ptIdx == 0) ? _pt0Bank : _pt1Bank;
                        int addr = bank * 4096 + tileId * 16 + row;
                        lsb = ppu.Cartridge.ReadChrByte(addr);
                        msb = ppu.Cartridge.ReadChrByte(addr + 8);
                    }
                    else
                    {
                        ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row), out lsb);
                        ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row + 8), out msb);
                    }

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
                        buffer[idx + 3] = (byte)0xFF; // Alpha
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
                if (_viewChrRom)
                {
                    int bank = (ptIdx == 0) ? _pt0Bank : _pt1Bank;
                    int addr = bank * 4096 + currentTileId * 16 + tileRow;
                    lsb = ppu.Cartridge.ReadChrByte(addr);
                    msb = ppu.Cartridge.ReadChrByte(addr + 8);
                }
                else
                {
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow), out lsb);
                    ppu.Cartridge.PpuRead((ushort)(ptIdx * 0x1000 + currentTileId * 16 + tileRow + 8), out msb);
                }

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
                        buffer[idx + 3] = (byte)0xFF; // Alpha
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
                        byte tileId = ppu.PeekVram(addr);
                        
                        // Get attribute byte
                        ushort attrAddr = (ushort)(0x2000 + nt * 0x0400 + 0x03C0 + (y / 4) * 8 + (x / 4));
                        byte attr = ppu.PeekVram(attrAddr);
                        int paletteIdx = (attr >> (((y % 4) / 2) * 4 + ((x % 4) / 2) * 2)) & 0x03;

                        // Use selected Pattern Table or Auto (based on PPUCTRL)
                        int ptIdx = _selectedNtPatternTable;
                        if (ptIdx == -1)
                        {
                            ptIdx = (ppu.PeekRegister(0x2000) & 0x10) != 0 ? 1 : 0;
                        }

                        // Draw 8x8 tile
                        for (int row = 0; row < 8; row++)
                        {
                            byte lsb = 0, msb = 0;
                            if (_viewChrRom)
                            {
                                int bank = (ptIdx == 0) ? _pt0Bank : _pt1Bank;
                                int chrAddr = bank * 4096 + tileId * 16 + row;
                                lsb = ppu.Cartridge?.ReadChrByte(chrAddr) ?? 0;
                                msb = ppu.Cartridge?.ReadChrByte(chrAddr + 8) ?? 0;
                            }
                            else
                            {
                                ppu.Cartridge?.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row), out lsb);
                                ppu.Cartridge?.PpuRead((ushort)(ptIdx * 0x1000 + tileId * 16 + row + 8), out msb);
                            }

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
                                buffer[idx + 3] = (byte)0xFF; // Alpha
                            }
                        }
                    }
                }
            }
        }
    }
}
