using System.Numerics;
using System.Collections.Concurrent;
using Hexa.NET.ImGui;
using Hexa.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OGNES.Components;

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
        private string _filterText = string.Empty;
        private readonly ConcurrentQueue<(LibraryEntry Entry, Image<Rgba32> Image)> _loadedImages = new();
        private readonly HashSet<string> _loadingCovers = new();

        public LibraryWindow(LibraryManager manager, GL gl)
        {
            _manager = manager;
            _gl = gl;
        }

        public void Render(ref bool show, Action<string> onLaunchRom)
        {
            if (!show) return;

            ProcessLoadedCovers();

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

        private void ProcessLoadedCovers()
        {
            int processed = 0;
            // Limit uploads per frame to avoid stalling the main thread
            while (processed < 5 && _loadedImages.TryDequeue(out var item))
            {
                try
                {
                    item.Entry.CoverTextureId = UploadTexture(item.Image);
                }
                catch
                {
                    // Ignore upload errors
                }
                finally
                {
                    item.Image.Dispose();
                }
                processed++;
            }
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

            ImGui.SameLine();
            ImGui.Text("Filter:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("##filter", ref _filterText, 100);
        }

        private unsafe void RenderGrid(Action<string> onLaunchRom)
        {
            float cardWidth = _cardWidth;
            float cardHeight = cardWidth * 1.5f;
            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float availWidth = ImGui.GetContentRegionAvail().X;
            int columns = (int)Math.Max(1, availWidth / (cardWidth + spacing + 8));

            var filteredEntries = _manager.Entries;
            if (!string.IsNullOrEmpty(_filterText))
            {
                filteredEntries = _manager.Entries
                    .Where(e => e.Title.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (ImGui.BeginTable("LibraryGridTable", columns))
            {
                for (int i = 0; i < filteredEntries.Count; i++)
                {
                    var entry = filteredEntries[i];
                    ImGui.TableNextColumn();
                    ImGui.PushID(i);

                    if (entry.CoverTextureId == null && !string.IsNullOrEmpty(entry.CoverPath))
                    {
                        if (!_loadingCovers.Contains(entry.CoverPath))
                        {
                            _loadingCovers.Add(entry.CoverPath);
                            Task.Run(() => LoadCoverAsync(entry));
                        }
                    }

                    ImGui.BeginGroup();
                    
                    var startPos = ImGui.GetCursorPos();
                    if (entry.CoverTextureId.HasValue && entry.CoverTextureId.Value != 0)
                    {
                        ImGui.ImageButton("cover", new ImTextureRef(null, entry.CoverTextureId.Value), new Vector2(cardWidth, cardHeight));
                        if (ImGui.IsItemHovered())
                        {
                            RenderTooltip(entry);
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                onLaunchRom(entry.RomPath);
                            }
                        }
                    }
                    else
                    {
                        ImGui.Button("No Cover\n" + entry.Title, new Vector2(cardWidth, cardHeight));
                        if (ImGui.IsItemHovered())
                        {
                            RenderTooltip(entry);
                            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                            {
                                onLaunchRom(entry.RomPath);
                            }
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

        private void RenderTooltip(LibraryEntry entry)
        {
            ImGui.BeginTooltip();
            if (entry.MapperId.HasValue)
                ImGui.Text($"Mapper: {entry.MapperId}");
            if (entry.HasBattery.HasValue)
                ImGui.Text($"Battery: {(entry.HasBattery.Value ? "Yes" : "No")}");
            if (!string.IsNullOrEmpty(entry.MirrorMode))
                ImGui.Text($"Mirroring: {entry.MirrorMode}");
            if (!string.IsNullOrEmpty(entry.Crc))
                ImGui.Text($"CRC: {entry.Crc}");
            ImGui.EndTooltip();
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

        private void LoadCoverAsync(LibraryEntry entry)
        {
            if (entry.CoverPath == null) return;
            try
            {
                var image = Image.Load<Rgba32>(entry.CoverPath);
                _loadedImages.Enqueue((entry, image));
            }
            catch
            {
                // Failed to load, maybe corrupt or locked.
                // We leave it in _loadingCovers so we don't try again this session.
            }
        }

        private uint UploadTexture(Image<Rgba32> image)
        {
            try
            {
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
