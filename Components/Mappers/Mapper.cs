using System;
using System.IO;

namespace OGNES.Components.Mappers
{
    public abstract class Mapper
    {
        protected readonly int PrgBanks;
        protected readonly int ChrBanks;

        public abstract string Name { get; }
        public Cartridge.Mirror MirrorMode { get; protected set; }

        protected Mapper(int prgBanks, int chrBanks, Cartridge.Mirror mirrorMode)
        {
            PrgBanks = prgBanks;
            ChrBanks = chrBanks;
            MirrorMode = mirrorMode;
        }

        public virtual void SaveState(BinaryWriter writer)
        {
            writer.Write((byte)MirrorMode);
        }

        public virtual void LoadState(BinaryReader reader)
        {
            MirrorMode = (Cartridge.Mirror)reader.ReadByte();
        }

        /// <summary>
        /// Maps a CPU address to a PRG ROM/RAM address.
        /// Returns true if the address was mapped, false otherwise.
        /// </summary>
        public abstract bool CpuMapRead(ushort address, out uint mappedAddress);

        /// <summary>
        /// Maps a CPU address to a PRG ROM/RAM address for writing.
        /// Returns true if the address was mapped, false otherwise.
        /// </summary>
        public abstract bool CpuMapWrite(ushort address, out uint mappedAddress, byte data);

        /// <summary>
        /// Maps a PPU address to a CHR ROM/RAM address.
        /// Returns true if the address was mapped, false otherwise.
        /// </summary>
        public abstract bool PpuMapRead(ushort address, out uint mappedAddress);

        /// <summary>
        /// Maps a PPU address to a CHR ROM/RAM address for writing.
        /// Returns true if the address was mapped, false otherwise.
        /// </summary>
        public abstract bool PpuMapWrite(ushort address, out uint mappedAddress);

        public virtual void NotifyPpuAddress(ushort address) { }
        public virtual void NotifyPpuAddress(ushort address, int cycle) { NotifyPpuAddress(address); }
        public virtual bool IrqActive => false;
        public virtual void IrqClear() { }

        public virtual bool Read(ushort address, out byte data) { data = 0; return false; }
        public virtual bool Write(ushort address, byte data) { return false; }
        
        public virtual bool PpuRead(ushort address, out byte data) { data = 0; return false; }
        public virtual bool PpuWrite(ushort address, byte data) { return false; }

        public virtual byte[]? GetBatteryRam() { return null; }
        public virtual void SetBatteryRam(byte[] data) { }
    }
}
