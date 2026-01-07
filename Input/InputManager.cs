using Hexa.NET.GLFW;
using OGNES.Components;
using System.Collections.Generic;

namespace OGNES.Input
{
    public unsafe class InputManager
    {
        private readonly Hexa.NET.GLFW.GLFWwindowPtr _window;
        private readonly AppSettings _settings;
        
        // Gamepad state
        private bool _gamepadConnected = false;
        private int _gamepadId = -1;

        public JoypadMacroExecutor MacroExecutor { get; } = new();

        public InputManager(Hexa.NET.GLFW.GLFWwindowPtr window, AppSettings settings)
        {
            _window = window;
            _settings = settings;
        }

        public void Update(Memory memory)
        {
            if (memory == null) return;

            // Reset button states for this frame
            // We need to accumulate inputs from all sources
            // Since Joypad.SetButtonState sets/clears bits, we need to be careful.
            // We will calculate the final state for each button and set it once.

            bool[] buttonStates = new bool[8]; // A, B, Select, Start, Up, Down, Left, Right

            // Keyboard Input
            ProcessKeyboard(ref buttonStates);

            // Gamepad Input
            ProcessGamepad(ref buttonStates);

            // Macro Input
            MacroExecutor.Apply(buttonStates);

            // Apply to Joypad
            memory.Joypad1.SetButtonState(Joypad.Button.A, buttonStates[(int)Joypad.Button.A]);
            memory.Joypad1.SetButtonState(Joypad.Button.B, buttonStates[(int)Joypad.Button.B]);
            memory.Joypad1.SetButtonState(Joypad.Button.Select, buttonStates[(int)Joypad.Button.Select]);
            memory.Joypad1.SetButtonState(Joypad.Button.Start, buttonStates[(int)Joypad.Button.Start]);
            memory.Joypad1.SetButtonState(Joypad.Button.Up, buttonStates[(int)Joypad.Button.Up]);
            memory.Joypad1.SetButtonState(Joypad.Button.Down, buttonStates[(int)Joypad.Button.Down]);
            memory.Joypad1.SetButtonState(Joypad.Button.Left, buttonStates[(int)Joypad.Button.Left]);
            memory.Joypad1.SetButtonState(Joypad.Button.Right, buttonStates[(int)Joypad.Button.Right]);
        }

        public void AdvanceMacro()
        {
            MacroExecutor.Advance();
        }

        private void ProcessKeyboard(ref bool[] buttonStates)
        {
             foreach (var mapping in _settings.KeyMappings)
            {
                bool pressed = GLFW.GetKey(_window, mapping.Value) == 1;
                if (!pressed) continue;

                Joypad.Button button = mapping.Key switch
                {
                    "A" => Joypad.Button.A,
                    "B" => Joypad.Button.B,
                    "Select" => Joypad.Button.Select,
                    "Start" => Joypad.Button.Start,
                    "Up" => Joypad.Button.Up,
                    "Down" => Joypad.Button.Down,
                    "Left" => Joypad.Button.Left,
                    "Right" => Joypad.Button.Right,
                    _ => (Joypad.Button)(-1)
                };

                if ((int)button != -1)
                {
                    buttonStates[(int)button] = true;
                }
            }
        }

        private void ProcessGamepad(ref bool[] buttonStates)
        {
            // Check for gamepad presence if not connected or periodically
            if (!_gamepadConnected)
            {
                for (int i = 0; i <= 15; i++)
                {
                    if (GLFW.JoystickPresent(i) == 1 && GLFW.JoystickIsGamepad(i) == 1)
                    {
                        _gamepadId = i;
                        _gamepadConnected = true;
                        break;
                    }
                }
            }

            if (_gamepadConnected)
            {
                if (GLFW.JoystickPresent(_gamepadId) == 0)
                {
                    _gamepadConnected = false;
                    return;
                }

                GLFWgamepadstate state;
                if (GLFW.GetGamepadState(_gamepadId, &state) == 1)
                {
                    byte* ptr = (byte*)&state;
                    byte* buttons = ptr;
                    float* axes = (float*)(ptr + 16); // Assuming 1 byte padding for 4-byte alignment of floats

                    // Map buttons
                    // A -> NES A
                    // X -> NES B
                    
                    if (buttons[0] == 1 || buttons[1] == 1) // A or B -> NES A
                        buttonStates[(int)Joypad.Button.A] = true;

                    if (buttons[2] == 1 || buttons[3] == 1) // X or Y -> NES B
                        buttonStates[(int)Joypad.Button.B] = true;

                    if (buttons[6] == 1) // Back -> Select
                        buttonStates[(int)Joypad.Button.Select] = true;

                    if (buttons[7] == 1) // Start -> Start
                        buttonStates[(int)Joypad.Button.Start] = true;

                    if (buttons[11] == 1 || axes[1] < -0.5f) // DpadUp or LeftY < -0.5
                        buttonStates[(int)Joypad.Button.Up] = true;

                    if (buttons[13] == 1 || axes[1] > 0.5f) // DpadDown or LeftY > 0.5
                        buttonStates[(int)Joypad.Button.Down] = true;

                    if (buttons[14] == 1 || axes[0] < -0.5f) // DpadLeft or LeftX < -0.5
                        buttonStates[(int)Joypad.Button.Left] = true;

                    if (buttons[12] == 1 || axes[0] > 0.5f) // DpadRight or LeftX > 0.5
                        buttonStates[(int)Joypad.Button.Right] = true;
                }
            }
        }
    }
}
