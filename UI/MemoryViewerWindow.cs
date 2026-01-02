using Hexa.NET.ImGui;
using OGNES.Components;
using OGNES.UI.ImGuiHexEditor;
using System;

namespace OGNES.UI
{
    public class MemoryViewerWindow
    {
        private Memory _memory;
        private HexEditorState _hexEditorState;
        private bool _visible = false;
        public bool Visible 
        { 
            get => _visible; 
            set => _visible = value; 
        }

        public MemoryViewerWindow(Memory memory)
        {
            _memory = memory;
            _hexEditorState = new HexEditorState
            {
                Bytes = null, // We use callbacks
                MaxBytes = 0x10000, // 64KB
                ReadCallback = ReadMemory,
                WriteCallback = WriteMemory,
                BytesPerLine = 16,
                ShowAddress = true,
                ShowAscii = true,
                AddressChars = 4,
                UserData = this
            };
        }

        public void SetMemory(Memory memory)
        {
            _memory = memory;
        }

        public void Draw()
        {
            if (!_visible) return;

            if (ImGui.Begin("Memory Viewer", ref _visible))
            {
                HexEditor.BeginHexEditor("##MemoryEditor", _hexEditorState);
                HexEditor.EndHexEditor();
            }
            ImGui.End();
        }

        public void GoToAddress(int address)
        {
            _hexEditorState.RequestScrollToByte = address;
            _hexEditorState.SelectStartByte = address;
            _hexEditorState.SelectEndByte = address;
            _visible = true;
        }

        private int ReadMemory(HexEditorState state, int offset, byte[] buffer, int size)
        {
            if (_memory == null)
            {
                Array.Fill(buffer, (byte)0);
                return size;
            }

            for (int i = 0; i < size; i++)
            {
                int addr = offset + i;
                if (addr >= 0 && addr <= 0xFFFF)
                {
                    buffer[i] = _memory.Peek((ushort)addr);
                }
                else
                {
                    buffer[i] = 0;
                }
            }
            return size;
        }

        private int WriteMemory(HexEditorState state, int offset, byte[] buffer, int size)
        {
            if (_memory == null) return 0;

            for (int i = 0; i < size; i++)
            {
                int addr = offset + i;
                if (addr >= 0 && addr <= 0xFFFF)
                {
                    _memory.Write((ushort)addr, buffer[i]);
                }
            }
            return size;
        }
    }
}
