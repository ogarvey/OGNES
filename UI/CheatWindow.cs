using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Widgets.Dialogs;
using OGNES.Components;
using System.Numerics;
using System.Globalization;
using OGNES.UI.General;
using System.IO;

namespace OGNES.UI
{
    public class CheatWindow
    {
        private readonly CheatManager _cheatManager;
        private readonly MemoryViewerWindow _memoryViewer;
        private readonly AppSettings _settings;
        public Action? OnSettingsChanged;
        private bool _visible = false;
        public bool Visible 
        { 
            get => _visible; 
            set 
            {
                if (_visible != value)
                {
                    _visible = value;
                    if (_settings != null)
                    {
                        _settings.ShowCheats = value;
                        OnSettingsChanged?.Invoke();
                    }
                }
            }
        }

        // Scan UI State
        private string _scanValueStr = "0";
        private int _selectedDataType = 0; // 0: Byte, 1: Int16
        private int _selectedScanType = 0; 
        private int _selectedRegion = 2; // 0: RAM, 1: WRAM, 2: All

        private readonly string[] _dataTypes = { "Byte", "Int16" };
        private readonly string[] _regions = { "Internal RAM ($0000-$07FF)", "WRAM ($6000-$7FFF)", "All Memory ($0000-$FFFF)" };
        
        // Scan Types
        private readonly string[] _firstScanTypes = { "Exact Value", "Unknown Initial Value" };
        private readonly ScanType[] _firstScanTypeMap = { ScanType.ExactValue, ScanType.UnknownInitialValue };

        private readonly string[] _nextScanTypes = { "Exact Value", "Value Changed", "Value Unchanged", "Value Increased", "Value Decreased" };
        private readonly ScanType[] _nextScanTypeMap = { ScanType.ExactValue, ScanType.ValueChanged, ScanType.ValueUnchanged, ScanType.ValueIncreased, ScanType.ValueDecreased };

        // Manual Add State
        private bool _showManualAddPopup = false;
        private string _manualAddressStr = "";
        private string _manualDescription = "";
        private int _manualDataType = 0;

        // File Dialogs
        private readonly FileOpenDialog _loadDialog = new();
        private readonly SaveFileDialog _saveDialog = new();

        public CheatWindow(CheatManager cheatManager, MemoryViewerWindow memoryViewer, AppSettings settings)
        {
            _cheatManager = cheatManager;
            _memoryViewer = memoryViewer;
            _settings = settings;

            if (!string.IsNullOrEmpty(_settings.LastCheatDirectory))
            {
                _loadDialog.CurrentFolder = _settings.LastCheatDirectory;
                _saveDialog.CurrentFolder = _settings.LastCheatDirectory;
            }
        }

        public void Draw()
        {
            if (!_visible) return;

            if (ImGui.Begin("Cheats", ref _visible))
            {
                DrawScanControls();
                ImGui.Separator();
                DrawResults();
                ImGui.Separator();
                DrawCheatTable();
            }
            ImGui.End();

            // Update settings if visibility changed via Close button
            if (_settings.ShowCheats != _visible)
            {
                _settings.ShowCheats = _visible;
                OnSettingsChanged?.Invoke();
            }

            DrawManualAddPopup();
            
            _loadDialog.Draw(ImGuiWindowFlags.None);
            _saveDialog.Draw(ImGuiWindowFlags.None);
        }

        private void DrawManualAddPopup()
        {
            if (_showManualAddPopup)
            {
                ImGui.OpenPopup("Add Cheat");
                _showManualAddPopup = false;
            }

            if (ImGui.BeginPopupModal("Add Cheat", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.InputText("Address (Hex)", ref _manualAddressStr, 4);
                ImGui.Combo("Type", ref _manualDataType, _dataTypes, _dataTypes.Length);
                ImGui.InputText("Description", ref _manualDescription, 64);

                if (ImGui.Button("Add"))
                {
                    if (int.TryParse(_manualAddressStr, NumberStyles.HexNumber, null, out int address))
                    {
                        _cheatManager.AddCheat(address, (CheatDataType)_manualDataType, _manualDescription);
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void LoadCheatsCallback(object? sender, DialogResult result)
        {
            if (result != DialogResult.Ok) return;

            var dialog = sender as FileOpenDialog;
            if (dialog == null) return;

            string? path = dialog.SelectedFile;
            if (string.IsNullOrEmpty(path)) return;

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(dialog.CurrentFolder, path);
            }

            _cheatManager.LoadCheats(path!);
            
            _settings.LastCheatDirectory = Path.GetDirectoryName(path);
            OnSettingsChanged?.Invoke();
        }

        private void SaveCheatsCallback(object? sender, DialogResult result)
        {
            if (result != DialogResult.Ok) return;

            var dialog = sender as SaveFileDialog;
            if (dialog == null) return;

            string? path = dialog.SelectedFile;
            if (string.IsNullOrEmpty(path)) return;

            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(dialog.CurrentFolder, path);
            }

            _cheatManager.SaveCheats(path!);

            _settings.LastCheatDirectory = Path.GetDirectoryName(path);
            OnSettingsChanged?.Invoke();
        }

        private void DrawScanControls()
        {
            ImGui.Text("Scan Settings");
            
            ImGui.Combo("Region", ref _selectedRegion, _regions, _regions.Length);
            ImGui.Combo("Data Type", ref _selectedDataType, _dataTypes, _dataTypes.Length);

            string[] currentScanTypes = _cheatManager.IsFirstScan ? _firstScanTypes : _nextScanTypes;
            ScanType[] currentScanTypeMap = _cheatManager.IsFirstScan ? _firstScanTypeMap : _nextScanTypeMap;

            // Reset selection if out of bounds when switching modes
            if (_selectedScanType >= currentScanTypes.Length) _selectedScanType = 0;

            ImGui.Combo("Scan Type", ref _selectedScanType, currentScanTypes, currentScanTypes.Length);

            bool showValueInput = true;
            if (currentScanTypeMap[_selectedScanType] == ScanType.UnknownInitialValue ||
                currentScanTypeMap[_selectedScanType] == ScanType.ValueChanged ||
                currentScanTypeMap[_selectedScanType] == ScanType.ValueUnchanged ||
                currentScanTypeMap[_selectedScanType] == ScanType.ValueIncreased ||
                currentScanTypeMap[_selectedScanType] == ScanType.ValueDecreased)
            {
                // Some of these might not need a value, but "Increased by X" could be supported later.
                // For now, these are just comparison with previous, so no value needed.
                showValueInput = false;
            }
            
            // Exact Value always needs input
            if (currentScanTypeMap[_selectedScanType] == ScanType.ExactValue)
                showValueInput = true;

            if (showValueInput)
            {
                ImGui.InputText("Value", ref _scanValueStr, 32);
            }

            if (ImGui.Button(_cheatManager.IsFirstScan ? "First Scan" : "Next Scan"))
            {
                MemoryRegion region = (MemoryRegion)_selectedRegion;
                CheatDataType dataType = (CheatDataType)_selectedDataType;
                ScanType scanType = currentScanTypeMap[_selectedScanType];
                
                _cheatManager.Scan(region, scanType, dataType, _scanValueStr);
            }

            if (!_cheatManager.IsFirstScan)
            {
                ImGui.SameLine();
                if (ImGui.Button("New Scan"))
                {
                    _cheatManager.NewScan();
                }
            }
        }

        private void DrawResults()
        {
            ImGui.Text($"Found: {_cheatManager.ResultCount}");
            
            if (ImGui.BeginChild("Results", new Vector2(0, 200), ImGuiChildFlags.Borders))
            {
                // Use clipper for performance if many results
                // Hexa.NET.ImGui doesn't have a nice wrapper for ListClipper yet in the version I recall, 
                // or maybe it does. Let's just limit display to 100 for now.
                
                var results = _cheatManager.Results.Take(100).ToList();

                ImGui.Columns(3, "ResultColumns");
                ImGui.Text("Address"); ImGui.NextColumn();
                ImGui.Text("Value"); ImGui.NextColumn();
                ImGui.Text("Prev"); ImGui.NextColumn();
                ImGui.Separator();

                foreach (var result in results)
                {
                    if (ImGui.Selectable($"${result.Address:X4}", false, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        // Double click to add?
                    }
                    
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _cheatManager.AddCheat(result.Address, (CheatDataType)_selectedDataType);
                    }

                    if (ImGui.BeginPopupContextItem($"Ctx{result.Address}"))
                    {
                        if (ImGui.MenuItem("Add to Cheat Table"))
                        {
                            _cheatManager.AddCheat(result.Address, (CheatDataType)_selectedDataType);
                        }
                        if (ImGui.MenuItem("View in Memory Editor"))
                        {
                            _memoryViewer.GoToAddress(result.Address);
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.NextColumn();
                    ImGui.Text($"{result.CurrentValue}");
                    ImGui.NextColumn();
                    ImGui.Text($"{result.PreviousValue}");
                    ImGui.NextColumn();
                }
                ImGui.Columns(1);
            }
            ImGui.EndChild();
        }

        private void DrawCheatTable()
        {
            ImGui.Text("Cheat Table");
            
            if (ImGui.Button("Add Address"))
            {
                _showManualAddPopup = true;
                _manualAddressStr = "";
                _manualDescription = "New Cheat";
            }
            ImGui.SameLine();
            if (ImGui.Button("Save Cheats"))
            {
                _saveDialog.Show(SaveCheatsCallback);
            }
            ImGui.SameLine();
            if (ImGui.Button("Load Cheats"))
            {
                _loadDialog.Show(LoadCheatsCallback);
            }

            if (ImGui.BeginChild("CheatTable", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                ImGui.Columns(5, "CheatColumns");
                ImGui.Text("Active"); ImGui.NextColumn();
                ImGui.Text("Description"); ImGui.NextColumn();
                ImGui.Text("Address"); ImGui.NextColumn();
                ImGui.Text("Value"); ImGui.NextColumn();
                ImGui.Text("Type"); ImGui.NextColumn();
                ImGui.Separator();

                var cheats = _cheatManager.Cheats.ToList(); // Copy to avoid modification issues
                foreach (var cheat in cheats)
                {
                    bool active = cheat.Active;
                    if (ImGui.Checkbox($"##Active{cheat.Address}", ref active))
                    {
                        cheat.Active = active;
                    }
                    ImGui.NextColumn();

                    string desc = cheat.Description;
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.InputText($"##Desc{cheat.Address}", ref desc, 64))
                    {
                        cheat.Description = desc;
                    }
                    ImGui.NextColumn();

                    ImGui.Text($"${cheat.Address:X4}");
                    ImGui.NextColumn();

                    int val = cheat.Value;
                    ImGui.SetNextItemWidth(80);
                    if (ImGui.InputInt($"##Val{cheat.Address}", ref val))
                    {
                        cheat.Value = val;
                        _cheatManager.WriteMemory(cheat.Address, val, cheat.DataType);
                    }
                    ImGui.NextColumn();

                    ImGui.Text($"{cheat.DataType}");
                    
                    if (ImGui.BeginPopupContextItem($"CheatCtx{cheat.Address}"))
                    {
                        if (ImGui.MenuItem("Delete"))
                        {
                            _cheatManager.RemoveCheat(cheat);
                        }
                        if (ImGui.MenuItem("View in Memory Editor"))
                        {
                            _memoryViewer.GoToAddress(cheat.Address);
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.NextColumn();
                }
                ImGui.Columns(1);
            }
            ImGui.EndChild();
        }
    }
}
