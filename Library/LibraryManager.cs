using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using IGDB;
using IGDB.Models;
using OGNES.Components;

namespace OGNES.Library
{
    public class LibraryEntry
    {
        public string RomPath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? CoverPath { get; set; }
        public uint? CoverTextureId { get; set; }
        public string? Crc { get; set; }
        public byte? MapperId { get; set; }
        public bool? HasBattery { get; set; }
        public string? MirrorMode { get; set; }
    }

    public class CoverSearchResult
    {
        public string Url { get; set; } = string.Empty;
        public string GameName { get; set; } = string.Empty;
    }

    public class LibraryManager
    {
        private readonly AppSettings _settings;
        private IGDBClient? _igdbClient;
        private readonly List<LibraryEntry> _entries = new();
        private readonly string _coversDirectory;

        public IReadOnlyList<LibraryEntry> Entries => _entries;

        public LibraryManager(AppSettings settings)
        {
            _settings = settings;
            _coversDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Covers");
            if (!Directory.Exists(_coversDirectory))
            {
                Directory.CreateDirectory(_coversDirectory);
            }
        }

        public void ResetClient()
        {
            _igdbClient = null;
        }

        private void EnsureClient()
        {
            if (_igdbClient == null && !string.IsNullOrEmpty(_settings.IgdbClientId) && !string.IsNullOrEmpty(_settings.IgdbClientSecret))
            {
                _igdbClient = new IGDBClient(_settings.IgdbClientId, _settings.IgdbClientSecret);
            }
        }

        public void ScanLibrary()
        {
            _entries.Clear();
            if (string.IsNullOrEmpty(_settings.GameFolderPath) || !Directory.Exists(_settings.GameFolderPath))
            {
                return;
            }

            var files = Directory.GetFiles(_settings.GameFolderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".nes", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                var entry = new LibraryEntry
                {
                    RomPath = file,
                    Title = Path.GetFileNameWithoutExtension(file),
                    Crc = NesDatabase.CalculateCrc(file)
                };

                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    using (var br = new BinaryReader(fs))
                    {
                        byte[] header = br.ReadBytes(16);
                        if (header.Length >= 16 && header[0] == 'N' && header[1] == 'E' && header[2] == 'S' && header[3] == 0x1A)
                        {
                            byte mapperLo = (byte)((header[6] >> 4) & 0x0F);
                            byte mapperHi = (byte)((header[7] >> 4) & 0x0F);
                            entry.MapperId = (byte)((mapperHi << 4) | mapperLo);
                            entry.HasBattery = (header[6] & 0x02) != 0;
                            entry.MirrorMode = (header[6] & 0x01) != 0 ? "Vertical" : "Horizontal";
                        }
                    }
                }
                catch { }

                if (!string.IsNullOrEmpty(entry.Crc) && NesDatabase.TryGetInfo(entry.Crc, out var info))
                {
                    entry.MapperId = info!.MapperId;
                    entry.HasBattery = info.HasBattery;
                    entry.MirrorMode = info.MirrorMode.ToString();
                }

                var coverPath = Path.Combine(_coversDirectory, entry.Title + ".jpg");
                if (File.Exists(coverPath))
                {
                    entry.CoverPath = coverPath;
                }

                _entries.Add(entry);
            }

            _entries.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        }

        public async Task DownloadCoverAsync(LibraryEntry entry)
        {
            EnsureClient();
            if (_igdbClient == null) return;

            try
            {
                var results = await SearchCoversAsync(entry.Title);
                if (results.Count > 0)
                {
                    await DownloadCoverFromUrlAsync(entry, results[0].Url);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download cover for {entry.Title}: {ex.Message}");
            }
        }

        public string CleanTitle(string title)
        {
            string searchTitle = title;
            int bracketIndex = searchTitle.IndexOf('(');
            if (bracketIndex != -1) searchTitle = searchTitle.Substring(0, bracketIndex);
            bracketIndex = searchTitle.IndexOf('[');
            if (bracketIndex != -1) searchTitle = searchTitle.Substring(0, bracketIndex);
            return searchTitle.Trim();
        }

        public async Task<List<CoverSearchResult>> SearchCoversAsync(string title)
        {
            EnsureClient();
            if (_igdbClient == null) return new List<CoverSearchResult>();

            try
            {
                string searchTitle = CleanTitle(title);

                // Search for the game
                var games = await _igdbClient.QueryAsync<Game>(IGDBClient.Endpoints.Games, 
                    $"search \"{searchTitle}\"; fields name,cover.url; where platforms = (99,18); limit 10;");

                var results = new List<CoverSearchResult>();
                if (games != null)
                {
                    foreach (var game in games)
                    {
                        if (game.Cover != null && game.Cover.Value.Url != null)
                        {
                            var url = game.Cover.Value.Url;
                            if (url.StartsWith("//")) url = "https:" + url;
                            
                            // IGDB returns thumb by default, we want big cover
                            url = url.Replace("t_thumb", "t_cover_big");
                            results.Add(new CoverSearchResult { Url = url, GameName = game.Name });
                        }
                    }
                }
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to search covers for {title}: {ex.Message}");
                return new List<CoverSearchResult>();
            }
        }

        public async Task DownloadCoverFromUrlAsync(LibraryEntry entry, string url)
        {
            try
            {
                using var httpClient = new HttpClient();
                var bytes = await httpClient.GetByteArrayAsync(url);
                
                var coverPath = Path.Combine(_coversDirectory, entry.Title + ".jpg");
                await File.WriteAllBytesAsync(coverPath, bytes);
                entry.CoverPath = coverPath;
                entry.CoverTextureId = null; // Force reload
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download cover for {entry.Title}: {ex.Message}");
            }
        }

        public async Task DownloadAllCoversAsync(Action<int, int>? progressCallback = null)
        {
            int count = 0;
            foreach (var entry in _entries)
            {
                if (string.IsNullOrEmpty(entry.CoverPath))
                {
                    await DownloadCoverAsync(entry);
                }
                count++;
                progressCallback?.Invoke(count, _entries.Count);
            }
        }
    }
}
