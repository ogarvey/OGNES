using System;
using System.IO;
using OGNES.Components.Mappers;

namespace OGNES.Components
{
    public class Cartridge
    {
        private byte[] _prgMemory;
        private byte[] _chrMemory;
        private byte[] _prgRam = new byte[8192];
        private Mapper _mapper;

        public string FileName { get; private set; }
        public byte MapperId { get; private set; }
        public string MapperName => _mapper.Name;
        public byte PrgBanks { get; private set; }
        public byte ChrBanks { get; private set; }

        public enum Mirror
        {
            Horizontal,
            Vertical,
            OnescreenLo,
            OnescreenHi,
        }

        public Mirror MirrorMode => _mapper.MirrorMode;

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_prgRam);
            writer.Write(_chrMemory); // CHR RAM might have changed
            _mapper.SaveState(writer);
        }

        public void LoadState(BinaryReader reader)
        {
            _prgRam = reader.ReadBytes(8192);
            _chrMemory = reader.ReadBytes(_chrMemory.Length);
            _mapper.LoadState(reader);
        }

        public Cartridge(string fileName)
        {
            FileName = Path.GetFileName(fileName);
            if (!File.Exists(fileName))
                throw new FileNotFoundException("ROM file not found", fileName);

            using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            // Read Header
            byte[] header = br.ReadBytes(16);
            if (header[0] != 'N' || header[1] != 'E' || header[2] != 'S' || header[3] != 0x1A)
                throw new Exception("Invalid iNES header");

            PrgBanks = header[4];
            ChrBanks = header[5];

            byte mapperLo = (byte)((header[6] >> 4) & 0x0F);
            byte mapperHi = (byte)((header[7] >> 4) & 0x0F);
            MapperId = (byte)((mapperHi << 4) | mapperLo);

            Mirror initialMirror = (header[6] & 0x01) != 0 ? Mirror.Vertical : Mirror.Horizontal;

            // Skip trainer if present
            if ((header[6] & 0x04) != 0)
            {
                fs.Seek(512, SeekOrigin.Current);
            }

            // Read PRG ROM
            _prgMemory = br.ReadBytes(PrgBanks * 16384);

            // Read CHR ROM
            if (ChrBanks == 0)
            {
                // CHR RAM
                _chrMemory = new byte[8192];
            }
            else
            {
                _chrMemory = br.ReadBytes(ChrBanks * 8192);
            }

            // Initialize Mapper
            _mapper = MapperId switch
            {
                0 => new Mapper0(PrgBanks, ChrBanks, initialMirror),
                1 => new Mapper1(PrgBanks, ChrBanks, initialMirror),
                2 => new Mapper2(PrgBanks, ChrBanks, initialMirror),
                3 => new Mapper3(PrgBanks, ChrBanks, initialMirror),
                4 => new Mapper4(PrgBanks, ChrBanks, initialMirror),
                7 => new Mapper7(PrgBanks, ChrBanks, initialMirror),
                9 => new Mapper9(PrgBanks, ChrBanks, initialMirror),
                10 => new Mapper10(PrgBanks, ChrBanks, initialMirror),
                _ => throw new NotImplementedException($"Mapper {MapperId} not implemented")
            };
        }

        public bool CpuRead(ushort address, out byte data)
        {
            data = 0;
            if (_mapper.CpuMapRead(address, out uint mappedAddress))
            {
                if (address >= 0x6000 && address <= 0x7FFF)
                {
                    data = _prgRam[mappedAddress];
                }
                else
                {
                    data = _prgMemory[mappedAddress];
                }
                return true;
            }
            return false;
        }

        public bool CpuWrite(ushort address, byte data)
        {
            if (_mapper.CpuMapWrite(address, out uint mappedAddress, data))
            {
                if (address >= 0x6000 && address <= 0x7FFF)
                {
                    _prgRam[mappedAddress] = data;
                }
                else
                {
                    _prgMemory[mappedAddress] = data;
                }
                return true;
            }
            return false;
        }

        public bool PpuRead(ushort address, out byte data)
        {
            data = 0;
            if (_mapper.PpuMapRead(address, out uint mappedAddress))
            {
                data = _chrMemory[mappedAddress];
                return true;
            }
            return false;
        }

        public bool PpuWrite(ushort address, byte data)
        {
            if (_mapper.PpuMapWrite(address, out uint mappedAddress))
            {
                _chrMemory[mappedAddress] = data;
                return true;
            }
            return false;
        }

        public byte Peek(ushort address)
        {
            if (_mapper.CpuMapRead(address, out uint mappedAddress))
            {
                if (address >= 0x6000 && address <= 0x7FFF)
                {
                    return _prgRam[mappedAddress];
                }
                else
                {
                    return _prgMemory[mappedAddress];
                }
            }
            return 0;
        }

        public void NotifyPpuAddress(ushort address, int cycle)
        {
            _mapper.NotifyPpuAddress(address, cycle);
        }

        public byte ReadChrByte(int address)
        {
            if (address < 0 || address >= _chrMemory.Length) return 0;
            return _chrMemory[address];
        }

        public int ChrRomLength => _chrMemory.Length;

        public bool IrqActive => _mapper.IrqActive;
        public void IrqClear() => _mapper.IrqClear();
    }
}
