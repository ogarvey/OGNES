namespace OGNES.Components
{
    public class Ppu
    {
        public int Scanline { get; private set; } = 0;
        public int Cycle { get; private set; } = 0;

        public void Tick()
        {
            Cycle++;
            if (Cycle >= 341)
            {
                Cycle = 0;
                Scanline++;
                if (Scanline >= 261)
                {
                    Scanline = -1; // Pre-render line
                }
                if (Scanline > 260)
                {
                    Scanline = 0;
                }
            }
        }

        public void Reset()
        {
            Scanline = 0;
            Cycle = 0;
        }
    }
}
