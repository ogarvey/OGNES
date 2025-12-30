using NAudio.Wave;
using System;
using System.Runtime.InteropServices;

namespace OGNES.Components
{
    public class AudioOutput : IAudioSink, IDisposable
    {
        private readonly WaveOutEvent _waveOut;
        private readonly BufferedWaveProvider _waveProvider;
        private byte[] _scratch = Array.Empty<byte>();
        private float _volume = 1.0f;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                _waveOut.Volume = _volume;
            }
        }

        public AudioOutput(int sampleRate = 44100, int latencyMs = 100)
        {
            var format = new WaveFormat(sampleRate, 16, 1);
            _waveProvider = new BufferedWaveProvider(format)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(500)
            };

            _waveOut = new WaveOutEvent
            {
                DesiredLatency = latencyMs,
                NumberOfBuffers = 3
            };
            _waveOut.Init(_waveProvider);
            _waveOut.Play();
        }

        public void WriteSamples(ReadOnlySpan<short> samples)
        {
            int bytesLen = samples.Length * sizeof(short);
            if (_scratch.Length < bytesLen)
            {
                _scratch = new byte[bytesLen];
            }

            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples);
            bytes.CopyTo(_scratch);
            _waveProvider.AddSamples(_scratch, 0, bytesLen);
        }

        public void Dispose()
        {
            _waveOut.Stop();
            _waveOut.Dispose();
        }
    }
}
