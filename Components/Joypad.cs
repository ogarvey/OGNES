using System;
using System.IO;

namespace OGNES.Components
{
    public class Joypad
    {
        private byte _buttonStates; // A, B, Select, Start, Up, Down, Left, Right
        private byte _shiftRegister;
        private bool _strobe;

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_buttonStates);
            writer.Write(_shiftRegister);
            writer.Write(_strobe);
        }

        public void LoadState(BinaryReader reader)
        {
            _buttonStates = reader.ReadByte();
            _shiftRegister = reader.ReadByte();
            _strobe = reader.ReadBoolean();
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

        public byte Read(byte openBusValue)
        {
            byte data;
            if (_strobe)
            {
                data = (byte)(_buttonStates & 0x01);
            }
            else
            {
                data = (byte)(_shiftRegister & 0x01);
                _shiftRegister >>= 1;
                _shiftRegister |= 0x80; // Most NES controllers return 1s after 8 reads
            }
            
            // Bits 5-7 are open bus
            return (byte)((openBusValue & 0xE0) | data);
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
