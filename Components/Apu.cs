using System;
using System.Collections.Generic;
using System.IO;

namespace OGNES.Components
{
    public class Apu
    {
        private PulseChannel _pulse1 = new(1);
        private PulseChannel _pulse2 = new(2);
        private TriangleChannel _triangle = new();
        private NoiseChannel _noise = new();
        private DmcChannel _dmc = new();

        private int _frameCounterMode;
        private bool _irqDisable;
        private int _frameCounter;
        private long _totalCycles;

        public bool FrameIrq { get; private set; }
        public bool DmcIrq => _dmc.IrqActive;
        public bool IrqActive => FrameIrq || DmcIrq;

        public void SaveState(BinaryWriter writer)
        {
            _pulse1.SaveState(writer);
            _pulse2.SaveState(writer);
            _triangle.SaveState(writer);
            _noise.SaveState(writer);
            _dmc.SaveState(writer);
            writer.Write(_frameCounterMode);
            writer.Write(_irqDisable);
            writer.Write(_frameCounter);
            writer.Write(_totalCycles);
            writer.Write(_sampleAccumulator);
            writer.Write(_prevSample);
            writer.Write(_prevOutput);
            writer.Write(FrameIrq);
            writer.Write(_outputAccumulator);
            writer.Write(_outputCount);
        }

        public void LoadState(BinaryReader reader)
        {
            _pulse1.LoadState(reader);
            _pulse2.LoadState(reader);
            _triangle.LoadState(reader);
            _noise.LoadState(reader);
            _dmc.LoadState(reader);
            _frameCounterMode = reader.ReadInt32();
            _irqDisable = reader.ReadBoolean();
            _frameCounter = reader.ReadInt32();
            _totalCycles = reader.ReadInt64();
            _sampleAccumulator = reader.ReadDouble();
            _prevSample = reader.ReadSingle();
            _prevOutput = reader.ReadSingle();
            FrameIrq = reader.ReadBoolean();
            _outputAccumulator = reader.ReadSingle();
            _outputCount = reader.ReadInt32();
        }

        private double _sampleAccumulator;
        private double _cyclesPerSample = 1789773.0 / 44100.0;
        private IAudioSink? _sink;
        private readonly short[] _mixBuffer = new short[1024];
        private int _mixBufferIndex;
        
        // Audio averaging
        private float _outputAccumulator;
        private int _outputCount;

        // High-pass filter
        private float _prevSample = 0;
        private float _prevOutput = 0;

        public Memory? Memory { get; set; }

        public void SetSink(IAudioSink? sink) => _sink = sink;

        public void Tick(Memory? memory)
        {
            _totalCycles++;

            // Pulse channels are clocked every CPU cycle
            _pulse1.Tick();
            _pulse2.Tick();
            _noise.Tick();
            _dmc.Tick(memory);

            // Triangle is clocked every CPU cycle (it has a higher resolution timer)
            _triangle.Tick();

            // Frame Sequencer
            _frameCounter++;
            
            // Mode 0: 4-step sequence
            // Mode 1: 5-step sequence
            if (_frameCounterMode == 0)
            {
                if (_frameCounter == 7458) ClockEnvelopes();
                else if (_frameCounter == 14915) { ClockEnvelopes(); ClockLengthAndSweep(); }
                else if (_frameCounter == 22372) ClockEnvelopes();
                else if (_frameCounter == 29831) 
                { 
                    ClockEnvelopes(); 
                    ClockLengthAndSweep(); 
                    if (!_irqDisable) FrameIrq = true;
                    _frameCounter = 0; 
                }
            }
            else
            {
                if (_frameCounter == 7458) ClockEnvelopes();
                else if (_frameCounter == 14915) { ClockEnvelopes(); ClockLengthAndSweep(); }
                else if (_frameCounter == 22372) ClockEnvelopes();
                else if (_frameCounter == 29831) { /* Nothing */ }
                else if (_frameCounter == 37282) { ClockEnvelopes(); ClockLengthAndSweep(); _frameCounter = 0; }
            }

            // Audio Downsampling with Averaging
            _outputAccumulator += GetOutput();
            _outputCount++;

            _sampleAccumulator += 1.0;
            if (_sampleAccumulator >= _cyclesPerSample)
            {
                _sampleAccumulator -= _cyclesPerSample;
                
                float sample = 0;
                if (_outputCount > 0)
                {
                    sample = _outputAccumulator / _outputCount;
                    _outputAccumulator = 0;
                    _outputCount = 0;
                }
                
                // High-pass filter to remove DC offset (90Hz cutoff at 44100Hz)
                // y[i] = 0.996 * (y[i-1] + x[i] - x[i-1])
                float output = 0.996f * (_prevOutput + sample - _prevSample);
                _prevSample = sample;
                _prevOutput = output;

                // Scale to short (amplify slightly as NES audio is quiet)
                float s = output * 4.0f;
                if (s > 1.0f) s = 1.0f;
                if (s < -1.0f) s = -1.0f;
                _mixBuffer[_mixBufferIndex++] = (short)(s * 32767);

                if (_mixBufferIndex >= _mixBuffer.Length)
                {
                    _sink?.WriteSamples(_mixBuffer);
                    _mixBufferIndex = 0;
                }
            }
        }

        private void ClockEnvelopes()
        {
            _pulse1.ClockEnvelope();
            _pulse2.ClockEnvelope();
            _triangle.ClockLinearCounter();
            _noise.ClockEnvelope();
        }

        private void ClockLengthAndSweep()
        {
            _pulse1.ClockLength();
            _pulse1.ClockSweep();
            _pulse2.ClockLength();
            _pulse2.ClockSweep();
            _triangle.ClockLength();
            _noise.ClockLength();
        }

        public void Write(ushort address, byte data)
        {
            if (address >= 0x4000 && address <= 0x4003) _pulse1.Write(address, data);
            else if (address >= 0x4004 && address <= 0x4007) _pulse2.Write(address, data);
            else if (address >= 0x4008 && address <= 0x400B) _triangle.Write(address, data);
            else if (address >= 0x400C && address <= 0x400F) _noise.Write(address, data);
            else if (address >= 0x4010 && address <= 0x4013) _dmc.Write(address, data);
            else if (address == 0x4015)
            {
                _pulse1.Enabled = (data & 0x01) != 0;
                _pulse2.Enabled = (data & 0x02) != 0;
                _triangle.Enabled = (data & 0x04) != 0;
                _noise.Enabled = (data & 0x08) != 0;
                _dmc.Enabled = (data & 0x10) != 0;
                _dmc.ClearIrq(); // Writing to $4015 clears DMC IRQ

                if (!_pulse1.Enabled) _pulse1.LengthCounter = 0;
                if (!_pulse2.Enabled) _pulse2.LengthCounter = 0;
                if (!_triangle.Enabled) _triangle.LengthCounter = 0;
                if (!_noise.Enabled) _noise.LengthCounter = 0;
                
                if (!_dmc.Enabled) _dmc.BytesRemaining = 0;
                else _dmc.Start(Memory);
            }
            else if (address == 0x4017)
            {
                _frameCounterMode = (data >> 7) & 0x01;
                _irqDisable = (data & 0x40) != 0;
                if (_irqDisable) FrameIrq = false;
                
                _frameCounter = 0;
                if (_totalCycles % 2 == 1) // Jitter
                {
                    _frameCounter = -1;
                }

                if (_frameCounterMode == 1)
                {
                    ClockEnvelopes();
                    ClockLengthAndSweep();
                }
            }
        }

        public byte PeekStatus()
        {
            byte status = 0;
            if (_pulse1.LengthCounter > 0) status |= 0x01;
            if (_pulse2.LengthCounter > 0) status |= 0x02;
            if (_triangle.LengthCounter > 0) status |= 0x04;
            if (_noise.LengthCounter > 0) status |= 0x08;
            if (_dmc.BytesRemaining > 0) status |= 0x10;
            
            if (FrameIrq) status |= 0x40;
            if (DmcIrq) status |= 0x80;
            
            return status;
        }

        public byte ReadStatus()
        {
            byte status = PeekStatus();
            FrameIrq = false; // Reading $4015 clears Frame IRQ
            return status;
        }

        public float GetOutput()
        {
            float pulse1 = _pulse1.GetOutput();
            float pulse2 = _pulse2.GetOutput();
            float triangle = _triangle.GetOutput();
            float noise = _noise.GetOutput();
            float dmc = _dmc.GetOutput();

            // Mixer formulas from NESdev wiki
            // These formulas expect channel outputs in the range:
            // pulse1, pulse2, triangle, noise: 0-15
            // dmc: 0-127
            
            float p1 = pulse1;
            float p2 = pulse2;
            float tri = triangle;
            float n = noise;
            float d = dmc;

            float pulseOut = 0;
            if (p1 + p2 > 0)
                pulseOut = 95.88f / ((8128.0f / (p1 + p2)) + 100.0f);

            float tndOut = 0;
            float tndSum = (tri / 8227.0f) + (n / 12241.0f) + (d / 22638.0f);
            if (tndSum > 0)
                tndOut = 159.79f / ((1.0f / tndSum) + 100.0f);

            return pulseOut + tndOut;
        }

        private static readonly byte[] LengthTable = {
            10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
            12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
        };

        private class PulseChannel
        {
            public bool Enabled;
            public int LengthCounter;
            private int _timer;
            private int _timerReload;
            private int _duty;
            private int _dutyIndex;
            private bool _lengthHalt;
            private bool _constantVolume;
            private int _volume;
            
            // Envelope
            private int _envelopeTimer;
            private int _envelopeCounter;
            private bool _envelopeStart;

            // Sweep
            private bool _sweepEnabled;
            private int _sweepPeriod;
            private bool _sweepNegate;
            private int _sweepShift;
            private int _sweepTimer;
            private bool _sweepReload;

            private int _id;

            public PulseChannel(int id) { _id = id; }

            public void SaveState(BinaryWriter writer)
            {
                writer.Write(Enabled);
                writer.Write(LengthCounter);
                writer.Write(_timer);
                writer.Write(_timerReload);
                writer.Write(_duty);
                writer.Write(_dutyIndex);
                writer.Write(_lengthHalt);
                writer.Write(_constantVolume);
                writer.Write(_volume);
                writer.Write(_envelopeTimer);
                writer.Write(_envelopeCounter);
                writer.Write(_envelopeStart);
                writer.Write(_sweepEnabled);
                writer.Write(_sweepPeriod);
                writer.Write(_sweepNegate);
                writer.Write(_sweepShift);
                writer.Write(_sweepTimer);
                writer.Write(_sweepReload);
            }

            public void LoadState(BinaryReader reader)
            {
                Enabled = reader.ReadBoolean();
                LengthCounter = reader.ReadInt32();
                _timer = reader.ReadInt32();
                _timerReload = reader.ReadInt32();
                _duty = reader.ReadInt32();
                _dutyIndex = reader.ReadInt32();
                _lengthHalt = reader.ReadBoolean();
                _constantVolume = reader.ReadBoolean();
                _volume = reader.ReadInt32();
                _envelopeTimer = reader.ReadInt32();
                _envelopeCounter = reader.ReadInt32();
                _envelopeStart = reader.ReadBoolean();
                _sweepEnabled = reader.ReadBoolean();
                _sweepPeriod = reader.ReadInt32();
                _sweepNegate = reader.ReadBoolean();
                _sweepShift = reader.ReadInt32();
                _sweepTimer = reader.ReadInt32();
                _sweepReload = reader.ReadBoolean();
            }

            public void Tick()
            {
                if (_timer > 0)
                {
                    _timer--;
                }
                else
                {
                    _timer = (_timerReload + 1) * 2 - 1; // Pulse timers are clocked every 2 CPU cycles
                    _dutyIndex = (_dutyIndex + 1) % 8;
                }
            }

            public void Write(ushort address, byte data)
            {
                int reg = address % 4;
                if (reg == 0)
                {
                    _duty = (data >> 6) & 0x03;
                    _lengthHalt = (data & 0x20) != 0;
                    _constantVolume = (data & 0x10) != 0;
                    _volume = data & 0x0F;
                }
                else if (reg == 1)
                {
                    _sweepEnabled = (data & 0x80) != 0;
                    _sweepPeriod = (data >> 4) & 0x07;
                    _sweepNegate = (data & 0x08) != 0;
                    _sweepShift = data & 0x07;
                    _sweepReload = true;
                }
                else if (reg == 2)
                {
                    _timerReload = (_timerReload & 0x0700) | data;
                }
                else if (reg == 3)
                {
                    _timerReload = (_timerReload & 0x00FF) | ((data & 0x07) << 8);
                    if (Enabled) LengthCounter = LengthTable[data >> 3];
                    _timer = (_timerReload + 1) * 2 - 1;
                    _dutyIndex = 0;
                    _envelopeStart = true;
                }
            }

            public void ClockEnvelope()
            {
                if (!_envelopeStart)
                {
                    if (_envelopeTimer > 0) _envelopeTimer--;
                    else
                    {
                        _envelopeTimer = _volume;
                        if (_envelopeCounter > 0) _envelopeCounter--;
                        else if (_lengthHalt) _envelopeCounter = 15;
                    }
                }
                else
                {
                    _envelopeStart = false;
                    _envelopeCounter = 15;
                    _envelopeTimer = _volume;
                }
            }

            public void ClockLength()
            {
                if (!_lengthHalt && LengthCounter > 0) LengthCounter--;
            }

            public void ClockSweep()
            {
                // Calculate target period to check for mute/update validity
                int delta = _timerReload >> _sweepShift;
                int targetPeriod;
                if (_sweepNegate)
                {
                    targetPeriod = _timerReload - delta;
                    if (_id == 1) targetPeriod--;
                }
                else
                {
                    targetPeriod = _timerReload + delta;
                }

                bool muted = _timerReload < 8 || targetPeriod > 0x7FF;

                if (_sweepReload)
                {
                    _sweepTimer = _sweepPeriod + 1;
                    _sweepReload = false;
                    return;
                }

                if (_sweepTimer > 0) _sweepTimer--;
                else
                {
                    _sweepTimer = _sweepPeriod + 1;
                    if (_sweepEnabled && _sweepShift > 0 && !muted)
                    {
                        // Only update if target is valid (which !muted implies, but let's be safe)
                        if (targetPeriod >= 0)
                        {
                            _timerReload = targetPeriod;
                        }
                    }
                }
            }

            private bool IsMuted()
            {
                if (_timerReload < 8) return true;
                
                // Calculate target period to check for overflow
                int delta = _timerReload >> _sweepShift;
                int targetPeriod;
                if (_sweepNegate)
                {
                    targetPeriod = _timerReload - delta;
                    if (_id == 1) targetPeriod--;
                }
                else
                {
                    targetPeriod = _timerReload + delta;
                }
                
                if (targetPeriod > 0x7FF) return true;
                
                return false;
            }

            private static readonly byte[,] DutyTable = {
                {0, 1, 0, 0, 0, 0, 0, 0},
                {0, 1, 1, 0, 0, 0, 0, 0},
                {0, 1, 1, 1, 1, 0, 0, 0},
                {1, 0, 0, 1, 1, 1, 1, 1}
            };

            public float GetOutput()
            {
                if (!Enabled || LengthCounter == 0 || IsMuted() || DutyTable[_duty, _dutyIndex] == 0) return 0;
                return _constantVolume ? _volume : _envelopeCounter;
            }
        }

        private class TriangleChannel
        {
            public bool Enabled;
            public int LengthCounter;
            private int _timer;
            private int _timerReload;
            private int _step;
            private bool _lengthHalt;
            private int _linearCounter;
            private int _linearCounterReload;
            private bool _linearCounterReloadFlag;

            public void SaveState(BinaryWriter writer)
            {
                writer.Write(Enabled);
                writer.Write(LengthCounter);
                writer.Write(_timer);
                writer.Write(_timerReload);
                writer.Write(_step);
                writer.Write(_lengthHalt);
                writer.Write(_linearCounter);
                writer.Write(_linearCounterReload);
                writer.Write(_linearCounterReloadFlag);
            }

            public void LoadState(BinaryReader reader)
            {
                Enabled = reader.ReadBoolean();
                LengthCounter = reader.ReadInt32();
                _timer = reader.ReadInt32();
                _timerReload = reader.ReadInt32();
                _step = reader.ReadInt32();
                _lengthHalt = reader.ReadBoolean();
                _linearCounter = reader.ReadInt32();
                _linearCounterReload = reader.ReadInt32();
                _linearCounterReloadFlag = reader.ReadBoolean();
            }

            public void Tick()
            {
                if (_timer > 0)
                {
                    _timer--;
                }
                else
                {
                    _timer = _timerReload; // Triangle timer is clocked every CPU cycle
                    if (LengthCounter > 0 && _linearCounter > 0)
                        _step = (_step + 1) % 32;
                }
            }

            public void Write(ushort address, byte data)
            {
                if (address == 0x4008)
                {
                    _lengthHalt = (data & 0x80) != 0;
                    _linearCounterReload = data & 0x7F;
                }
                else if (address == 0x400A)
                {
                    _timerReload = (_timerReload & 0x0700) | data;
                }
                else if (address == 0x400B)
                {
                    _timerReload = (_timerReload & 0x00FF) | ((data & 0x07) << 8);
                    if (Enabled) LengthCounter = LengthTable[data >> 3];
                    _timer = _timerReload;
                    _linearCounterReloadFlag = true;
                }
            }

            public void ClockLength()
            {
                if (!_lengthHalt && LengthCounter > 0) LengthCounter--;
            }

            public void ClockLinearCounter()
            {
                if (_linearCounterReloadFlag) _linearCounter = _linearCounterReload;
                else if (_linearCounter > 0) _linearCounter--;
                if (!_lengthHalt) _linearCounterReloadFlag = false;
            }

            private static readonly byte[] TriangleTable = {
                15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
                0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
            };

            public float GetOutput()
            {
                return TriangleTable[_step];
            }
        }

        private class NoiseChannel
        {
            public bool Enabled;
            public int LengthCounter;
            private int _timer;
            private int _timerReload;
            private bool _lengthHalt;
            private bool _constantVolume;
            private int _volume;
            private int _shiftRegister = 1;
            private bool _mode;

            // Envelope
            private int _envelopeTimer;
            private int _envelopeCounter;
            private bool _envelopeStart;

            public void SaveState(BinaryWriter writer)
            {
                writer.Write(Enabled);
                writer.Write(LengthCounter);
                writer.Write(_timer);
                writer.Write(_timerReload);
                writer.Write(_lengthHalt);
                writer.Write(_constantVolume);
                writer.Write(_volume);
                writer.Write(_shiftRegister);
                writer.Write(_mode);
                writer.Write(_envelopeTimer);
                writer.Write(_envelopeCounter);
                writer.Write(_envelopeStart);
            }

            public void LoadState(BinaryReader reader)
            {
                Enabled = reader.ReadBoolean();
                LengthCounter = reader.ReadInt32();
                _timer = reader.ReadInt32();
                _timerReload = reader.ReadInt32();
                _lengthHalt = reader.ReadBoolean();
                _constantVolume = reader.ReadBoolean();
                _volume = reader.ReadInt32();
                _shiftRegister = reader.ReadInt32();
                _mode = reader.ReadBoolean();
                _envelopeTimer = reader.ReadInt32();
                _envelopeCounter = reader.ReadInt32();
                _envelopeStart = reader.ReadBoolean();
            }

            private static readonly int[] NoisePeriodTable = {
                4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068
            };

            public void Tick()
            {
                if (_timer > 0)
                {
                    _timer--;
                }
                else
                {
                    _timer = _timerReload - 1; // Noise timer is clocked every CPU cycle
                    int bit0 = _shiftRegister & 0x01;
                    int bit1 = (_shiftRegister >> (_mode ? 6 : 1)) & 0x01;
                    int feedback = bit0 ^ bit1;
                    _shiftRegister = (_shiftRegister >> 1) | (feedback << 14);
                }
            }

            public void Write(ushort address, byte data)
            {
                if (address == 0x400C)
                {
                    _lengthHalt = (data & 0x20) != 0;
                    _constantVolume = (data & 0x10) != 0;
                    _volume = data & 0x0F;
                }
                else if (address == 0x400E)
                {
                    _mode = (data & 0x80) != 0;
                    _timerReload = NoisePeriodTable[data & 0x0F];
                    _timer = _timerReload - 1;
                }
                else if (address == 0x400F)
                {
                    if (Enabled) LengthCounter = LengthTable[data >> 3];
                    _envelopeStart = true;
                }
            }

            public void ClockEnvelope()
            {
                if (!_envelopeStart)
                {
                    if (_envelopeTimer > 0) _envelopeTimer--;
                    else
                    {
                        _envelopeTimer = _volume;
                        if (_envelopeCounter > 0) _envelopeCounter--;
                        else if (_lengthHalt) _envelopeCounter = 15;
                    }
                }
                else
                {
                    _envelopeStart = false;
                    _envelopeCounter = 15;
                    _envelopeTimer = _volume;
                }
            }

            public void ClockLength()
            {
                if (!_lengthHalt && LengthCounter > 0) LengthCounter--;
            }

            public float GetOutput()
            {
                if (LengthCounter == 0 || (_shiftRegister & 0x01) != 0) return 0;
                return _constantVolume ? _volume : _envelopeCounter;
            }
        }

        private class DmcChannel
        {
            public bool Enabled;
            public int BytesRemaining;
            private int _timer;
            private int _timerReload;
            private int _outputLevel;
            private ushort _sampleAddress;
            private ushort _currentAddress;
            private int _sampleLength;
            private bool _loop;
            private bool _irqEnable;
            private byte _sampleBuffer;
            private bool _bufferEmpty = true;
            private int _shiftRegister;
            private int _bitsRemaining = 8;
            private bool _silence = true;
            public bool IrqActive { get; private set; }

            public void SaveState(BinaryWriter writer)
            {
                writer.Write(Enabled);
                writer.Write(BytesRemaining);
                writer.Write(_timer);
                writer.Write(_timerReload);
                writer.Write(_outputLevel);
                writer.Write(_sampleAddress);
                writer.Write(_currentAddress);
                writer.Write(_sampleLength);
                writer.Write(_loop);
                writer.Write(_irqEnable);
                writer.Write(_sampleBuffer);
                writer.Write(_bufferEmpty);
                writer.Write(_shiftRegister);
                writer.Write(_bitsRemaining);
                writer.Write(_silence);
                writer.Write(IrqActive);
            }

            public void LoadState(BinaryReader reader)
            {
                Enabled = reader.ReadBoolean();
                BytesRemaining = reader.ReadInt32();
                _timer = reader.ReadInt32();
                _timerReload = reader.ReadInt32();
                _outputLevel = reader.ReadInt32();
                _sampleAddress = reader.ReadUInt16();
                _currentAddress = reader.ReadUInt16();
                _sampleLength = reader.ReadInt32();
                _loop = reader.ReadBoolean();
                _irqEnable = reader.ReadBoolean();
                _sampleBuffer = reader.ReadByte();
                _bufferEmpty = reader.ReadBoolean();
                _shiftRegister = reader.ReadInt32();
                _bitsRemaining = reader.ReadInt32();
                _silence = reader.ReadBoolean();
                IrqActive = reader.ReadBoolean();
            }

            private static readonly int[] DmcPeriodTable = {
                428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54
            };

            public void Restart()
            {
                _currentAddress = _sampleAddress;
                BytesRemaining = _sampleLength;
            }

            public void Tick(Memory? memory)
            {
                if (_timer > 0)
                {
                    _timer--;
                }
                else
                {
                    _timer = _timerReload - 1; // DMC timer is clocked every CPU cycle
                    if (_bitsRemaining > 0)
                    {
                        if (!_silence)
                        {
                            if ((_shiftRegister & 0x01) != 0)
                            {
                                if (_outputLevel <= 125) _outputLevel += 2;
                            }
                            else
                            {
                                if (_outputLevel >= 2) _outputLevel -= 2;
                            }
                        }
                        _shiftRegister >>= 1;
                        _bitsRemaining--;
                    }

                    if (_bitsRemaining == 0)
                    {
                        _bitsRemaining = 8;
                        if (_bufferEmpty)
                        {
                            _silence = true;
                        }
                        else
                        {
                            _silence = false;
                            _shiftRegister = _sampleBuffer;
                            _bufferEmpty = true;
                            
                            // Trigger memory fetch for next byte if buffer is now empty
                            FetchNextByte(memory);
                        }
                    }
                }
            }

            private void FetchNextByte(Memory? memory)
            {
                if (_bufferEmpty && BytesRemaining > 0)
                {
                    // Fetch next byte
                    if (memory != null)
                    {
                        memory.Cpu?.Stall(4);
                        _sampleBuffer = memory.Read(_currentAddress);
                        _bufferEmpty = false;
                        _currentAddress++;
                        if (_currentAddress == 0) _currentAddress = 0x8000;
                        BytesRemaining--;
                        if (BytesRemaining == 0)
                        {
                            if (_loop)
                            {
                                Restart();
                            }
                            else if (_irqEnable)
                            {
                                IrqActive = true;
                            }
                        }
                    }
                }
            }

            public void ClearIrq()
            {
                IrqActive = false;
            }

            public void Write(ushort address, byte data)
            {
                if (address == 0x4010)
                {
                    _irqEnable = (data & 0x80) != 0;
                    if (!_irqEnable) IrqActive = false;
                    _loop = (data & 0x40) != 0;
                    _timerReload = DmcPeriodTable[data & 0x0F];
                    _timer = _timerReload - 1;
                }
                else if (address == 0x4011)
                {
                    _outputLevel = data & 0x7F;
                }
                else if (address == 0x4012)
                {
                    _sampleAddress = (ushort)(0xC000 | (data << 6));
                }
                else if (address == 0x4013)
                {
                    _sampleLength = (data << 4) + 1;
                }
            }

            public void Start(Memory? memory)
            {
                if (BytesRemaining == 0)
                {
                    Restart();
                    if (_bufferEmpty)
                    {
                        FetchNextByte(memory);
                    }
                }
            }

            public float GetOutput()
            {
                return _outputLevel;
            }
        }
    }
}
