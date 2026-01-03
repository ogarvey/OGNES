using System;

namespace OGNES.Components
{
    public static class Crc32
    {
        private static readonly uint[] Table;

        static Crc32()
        {
            const uint polynomial = 0xedb88320;
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (uint j = 8; j > 0; j--)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ polynomial;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        public static uint Compute(byte[] bytes)
        {
            return Update(0, bytes);
        }

        public static uint Update(uint crc, byte[] bytes)
        {
            crc = crc ^ 0xffffffff;
            foreach (byte b in bytes)
            {
                byte index = (byte)(((crc) & 0xff) ^ b);
                crc = (crc >> 8) ^ Table[index];
            }
            return crc ^ 0xffffffff;
        }
        
        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            return Update(0, bytes);
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> bytes)
        {
            crc = crc ^ 0xffffffff;
            foreach (byte b in bytes)
            {
                byte index = (byte)(((crc) & 0xff) ^ b);
                crc = (crc >> 8) ^ Table[index];
            }
            return crc ^ 0xffffffff;
        }
    }
}
