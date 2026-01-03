using System;
using System.Collections.Generic;
using System.IO;

namespace OGNES.Components
{
    public class RomInfo
    {
        public byte MapperId;
        public bool HasBattery;
        public Cartridge.Mirror MirrorMode;
    }

    public static class NesDatabase
    {
        private static Dictionary<string, RomInfo> _database = new Dictionary<string, RomInfo>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded = false;

        public static void Initialize()
        {
            if (_loaded) return;

            string[] dbPaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "MesenNesDB.txt"),
                Path.Combine(Directory.GetCurrentDirectory(), "Database", "MesenNesDB.txt"),
                "Database/MesenNesDB.txt"
            };

            foreach (var path in dbPaths)
            {
                if (File.Exists(path))
                {
                    Load(path);
                    return;
                }
            }
            Console.WriteLine("[NesDatabase] Database file not found.");
        }

        private static void Load(string path)
        {
            try 
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    
                    var parts = line.Split(',');
                    if (parts.Length > 12)
                    {
                        string crc = parts[0];
                        if (string.IsNullOrWhiteSpace(crc)) continue;

                        var info = new RomInfo();
                        
                        if (byte.TryParse(parts[5], out byte mapper))
                            info.MapperId = mapper;
                        
                        if (parts[11] == "1") info.HasBattery = true;
                        
                        if (parts[12] == "h") info.MirrorMode = Cartridge.Mirror.Horizontal;
                        else if (parts[12] == "v") info.MirrorMode = Cartridge.Mirror.Vertical;
                        else info.MirrorMode = Cartridge.Mirror.Horizontal;

                        _database[crc] = info;
                    }
                }
                _loaded = true;
                Console.WriteLine($"[NesDatabase] Loaded {_database.Count} entries from {path}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NesDatabase] Error loading database: {ex.Message}");
            }
        }

        public static bool TryGetInfo(string crc, out RomInfo? info)
        {
            if (!_loaded) Initialize();
            return _database.TryGetValue(crc, out info);
        }

        public static string CalculateCrc(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                // Read Header
                byte[] header = br.ReadBytes(16);
                if (header.Length < 16 || header[0] != 'N' || header[1] != 'E' || header[2] != 'S' || header[3] != 0x1A)
                    return string.Empty;

                byte prgBanks = header[4];
                byte chrBanks = header[5];

                // Skip trainer if present
                if ((header[6] & 0x04) != 0)
                {
                    fs.Seek(512, SeekOrigin.Current);
                }

                // Read PRG ROM
                byte[] prgMemory = br.ReadBytes(prgBanks * 16384);

                uint crc = Crc32.Compute(prgMemory);

                // Read CHR ROM
                if (chrBanks > 0)
                {
                    byte[] chrMemory = br.ReadBytes(chrBanks * 8192);
                    crc = Crc32.Update(crc, chrMemory);
                }

                return crc.ToString("X8");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
