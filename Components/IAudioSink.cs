using System;

namespace OGNES.Components
{
    public interface IAudioSink
    {
        void WriteSamples(ReadOnlySpan<short> samples);
    }
}
