using System;
using System.IO;

namespace OGNES.Components
{
    public class Joypad
    {
        private byte _buttonStates; // A, B, Select, Start, Up, Down, Left, Right
        private byte _shiftRegister;
        private bool _strobe;

        public bool ZapperEnabled { get; set; }
        public int ZapperX { get; set; }
        public int ZapperY { get; set; }
        public bool Trigger { get; set; }
        private long _lightDetectedCycle = -1;

        public void DetectLight(long currentCycle)
        {
            _lightDetectedCycle = currentCycle;
        }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_buttonStates);
            writer.Write(_shiftRegister);
            writer.Write(_strobe);
            writer.Write(ZapperEnabled);
            writer.Write(ZapperX);
            writer.Write(ZapperY);
            writer.Write(Trigger);
            writer.Write(_lightDetectedCycle);
        }

        public void LoadState(BinaryReader reader)
        {
            _buttonStates = reader.ReadByte();
            _shiftRegister = reader.ReadByte();
            _strobe = reader.ReadBoolean();
            ZapperEnabled = reader.ReadBoolean();
            ZapperX = reader.ReadInt32();
            ZapperY = reader.ReadInt32();
            Trigger = reader.ReadBoolean();
            _lightDetectedCycle = reader.ReadInt64();
        }

        public enum Button
        {
            A = 0,
            B = 1,
            Select = 2,
            Start = 3,
            Up = 4,
            Down = 5,
            Left = 6,
            Right = 7
        }

        public void Write(byte data)
        {
            _strobe = (data & 0x01) != 0;
            if (_strobe)
            {
                _shiftRegister = _buttonStates;
            }
        }

        public byte Read(byte openBusValue, long currentCycle = 0)
        {
            if (ZapperEnabled)
            {
                byte data = 0;
                // D3: Light sensor (0: detected, 1: not detected)
                // D4: Trigger (0: pulled, 1: released)
                
                // Light sensor
                // Signal lasts for ~2000 cycles (approx 20 scanlines)
                bool lightDetected = _lightDetectedCycle != -1 && (currentCycle - _lightDetectedCycle) < 2000;
                if (!lightDetected) data |= 0x08; // 1: not detected

                // Trigger
                if (!Trigger) data |= 0x10; // 1: released

                // Bits 0-2 are not driven by Zapper, usually open bus or 0?
                // Wiki says: "Serial data (Vs.)" on D0. But for NES Zapper, D0-D2 are not used.
                // Usually they return 0 or open bus.
                // Let's assume 0 for D0-D2, and merge with open bus for D5-D7.
                return (byte)((openBusValue & 0xE0) | data);
            }

            byte serialData;
            if (_strobe)
            {
                serialData = (byte)(_buttonStates & 0x01);
            }
            else
            {
                serialData = (byte)(_shiftRegister & 0x01);
                _shiftRegister >>= 1;
                _shiftRegister |= 0x80; // Most NES controllers return 1s after 8 reads
            }
            
            // Bits 5-7 are open bus
            return (byte)((openBusValue & 0xE0) | serialData);
        }

        public void SetButtonState(Button button, bool pressed)
        {
            if (pressed)
                _buttonStates |= (byte)(1 << (int)button);
            else
                _buttonStates &= (byte)~(1 << (int)button);
        }
    }
}
