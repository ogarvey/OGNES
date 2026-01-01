using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OGNES.Library
{
    public class LibraryWindow
    {
        private readonly LibraryManager _manager;
        private readonly GL _gl;
        private bool _isScanning = false;
        private bool _isDownloading = false;
        private string _statusMessage = string.Empty;
        private int _progressCurrent = 0;
        private int _progressTotal = 0;
        private float _cardWidth = 120;
        private LibraryEntry? _searchingEntry;
        private List<CoverSearchResult>? _searchResults;
        private bool _isSearchingCovers = false;
        private bool _shouldOpenPopup = false;
        private bool _hasScanned = false;
        private string _customSearchTerm = string.Empty;

        public LibraryWindow(LibraryManager manager, GL gl)
        {
            _manager = manager;
            _gl = gl;
        }

        public void Render(ref bool show, Action<string> onLaunchRom)
        {
            if (!show) return;

            if (!_hasScanned)
            {
                _manager.ScanLibrary();
                _hasScanned = true;
            }

            if (ImGui.Begin("Game Library", ref show))
            {
                RenderToolbar();

                if (_isScanning || _isDownloading)
                {
                    ImGui.Text(_statusMessage);
                    if (_progressTotal > 0)
                    {
                        ImGui.ProgressBar((float)_progressCurrent / _progressTotal, new Vector2(-1, 0), $"{_progressCurrent}/{_progressTotal}");
                    }
                }

                ImGui.Separator();

                var avail = ImGui.GetContentRegionAvail();
                if (ImGui.BeginChild("LibraryGrid", avail))
                {
                    RenderGrid(onLaunchRom);
                }
                ImGui.EndChild();

                RenderSearchPopup();
            }
            ImGui.End();
        }

        private void RenderToolbar()
        {
            if (ImGui.Button("Scan Folder"))
            {
                _isScanning = true;
                _statusMessage = "Scanning...";
                _manager.ScanLibrary();
                _isScanning = false;
                _statusMessage = string.Empty;
            }
            ImGui.SameLine();
            if (ImGui.Button("Download All Covers"))
            {
                _isDownloading = true;
                _statusMessage = "Downloading covers...";
                _progressTotal = _manager.Entries.Count;
                _progressCurrent = 0;
                Task.Run(async () =>
                {
                    await _manager.DownloadAllCoversAsync((curr, total) =>
                    {
                        _progressCurrent = curr;
                        _progressTotal = total;
                    });
                    _isDownloading = false;
                    _statusMessage = string.Empty;
                });
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.SliderFloat("Cover Size", ref _cardWidth, 60, 300);
        }

        private unsafe void RenderGrid(Action<string> onLaunchRom)
        {
            float cardWidth = _cardWidth;
            float cardHeight = cardWidth * 1.5f;
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float availWidth = ImGui.GetContentRegionAvail().X;
            int columns = (int)Math.Max(1, availWidth / (cardWidth + spacing + 8));

            if (ImGui.BeginTable("LibraryGridTable", columns))
            {
                for (int i = 0; i < _manager.Entries.Count; i++)
                {
                    var entry = _manager.Entries[i];
                    ImGui.TableNextColumn();
                    ImGui.PushID(i);

                    if (entry.CoverTextureId == null && !string.IsNullOrEmpty(entry.CoverPath))
                    {
                        entry.CoverTextureId = LoadTexture(entry.CoverPath);
                    }

                    ImGui.BeginGroup();
                    
                    var startPos = ImGui.GetCursorPos();
                    if (entry.CoverTextureId.HasValue && entry.CoverTextureId.Value != 0)
                    {
                        ImGui.ImageButton("cover", new ImTextureRef(null, entry.CoverTextureId.Value), new Vector2(cardWidth, cardHeight));
                        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            onLaunchRom(entry.RomPath);
                        }
                    }
                    else
                    {
                        ImGui.Button("No Cover\n" + entry.Title, new Vector2(cardWidth, cardHeight));
                        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                        {
                            onLaunchRom(entry.RomPath);
                        }
                    }

                    float titleHeight = ImGui.GetTextLineHeightWithSpacing() * 3.0f;
                    var titleStartPos = ImGui.GetCursorPos();
                    
                    // Use a child window to contain and clip the title text
                    if (ImGui.BeginChild($"title_{i}", new Vector2(cardWidth, titleHeight), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground))
                    {
                        string displayTitle = entry.Title;
                        ImGui.TextWrapped(displayTitle);
                    }
                    ImGui.EndChild();
                    
                    // Ensure the button is always placed below the title area
                    ImGui.SetCursorPos(titleStartPos + new Vector2(0, titleHeight + ImGui.GetStyle().ItemSpacing.Y));
                    
                    if (ImGui.Button("Get Cover", new Vector2(cardWidth, 0)))
                    {
                        SearchCovers(entry);
                    }

                    ImGui.EndGroup();
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
        }

        private void SearchCovers(LibraryEntry entry, string? customTerm = null)
        {
            _searchingEntry = entry;
            _isSearchingCovers = true;
            _searchResults = null;
            _shouldOpenPopup = true;
            
            if (customTerm == null)
            {
                _customSearchTerm = _manager.CleanTitle(entry.Title);
            }

            string term = customTerm ?? _customSearchTerm;

            Task.Run(async () =>
            {
                _searchResults = await _manager.SearchCoversAsync(term);
                _isSearchingCovers = false;
            });
        }

        private void RenderSearchPopup()
        {
            if (_searchingEntry == null) return;

            if (_shouldOpenPopup)
            {
                ImGui.OpenPopup("Select Cover");
                _shouldOpenPopup = false;
            }

            bool isOpen = true;
            if (ImGui.BeginPopupModal("Select Cover", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Search Term:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                if (ImGui.InputText("##customSearch", ref _customSearchTerm, 100, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    SearchCovers(_searchingEntry, _customSearchTerm);
                }
                ImGui.SameLine();
                if (ImGui.Button("Search"))
                {
                    SearchCovers(_searchingEntry, _customSearchTerm);
                }

                ImGui.Separator();

                if (_isSearchingCovers)
                {
                    ImGui.Text("Searching for " + _customSearchTerm + "...");
                }
                else if (_searchResults != null)
                {
                    ImGui.Text("Results for: " + _customSearchTerm);
                    ImGui.Separator();

                    if (_searchResults.Count == 0)
                    {
                        ImGui.Text("No covers found.");
                    }
                    else
                    {
                        foreach (var result in _searchResults)
                        {
                            if (ImGui.Selectable($"{result.GameName}##{result.Url}"))
                            {
                                var entry = _searchingEntry;
                                var url = result.Url;
                                if (entry != null)
                                {
                                    Task.Run(async () => await _manager.DownloadCoverFromUrlAsync(entry, url));
                                }
                                _searchingEntry = null;
                                _searchResults = null;
                                ImGui.CloseCurrentPopup();
                            }
                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip(result.Url);
                            }
                        }
                    }
                }

                if (!isOpen || ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    _searchingEntry = null;
                    _searchResults = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private uint LoadTexture(string path)
        {
            try
            {
                using var image = Image.Load<Rgba32>(path);
                uint textureId;
                unsafe
                {
                    _gl.GenTextures(1, &textureId);
                    _gl.BindTexture(GLTextureTarget.Texture2D, textureId);
                    _gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MinFilter, (int)GLTextureMinFilter.Linear);
                    _gl.TexParameteri(GLTextureTarget.Texture2D, GLTextureParameterName.MagFilter, (int)GLTextureMagFilter.Linear);

                    byte[] pixels = new byte[image.Width * image.Height * 4];
                    image.CopyPixelDataTo(pixels);

                    fixed (byte* p = pixels)
                    {
                        _gl.TexImage2D(GLTextureTarget.Texture2D, 0, GLInternalFormat.Rgba, image.Width, image.Height, 0, GLPixelFormat.Rgba, GLPixelType.UnsignedByte, p);
                    }
                }

                return textureId;
            }
            catch
            {
                return 0;
            }
        }
    }
}
