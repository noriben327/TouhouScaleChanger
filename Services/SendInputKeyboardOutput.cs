using System.ComponentModel;
using System.Runtime.InteropServices;
using TouhouScalePad.Core;

namespace TouhouScalePad.Services;

public sealed class SendInputKeyboardOutput : IKeyboardOutput
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    private static readonly (DpadButtons Button, ushort ScanCode)[] KeyMappings =
    [
        (DpadButtons.Up, 0x48),
        (DpadButtons.Down, 0x50),
        (DpadButtons.Left, 0x4B),
        (DpadButtons.Right, 0x4D)
    ];

    public void ApplyTransition(DpadButtons previous, DpadButtons current)
    {
        foreach (var (button, scanCode) in KeyMappings)
        {
            var wasPressed = (previous & button) != 0;
            var isPressed = (current & button) != 0;
            if (wasPressed != isPressed) SendKey(scanCode, !isPressed);
        }
    }

    public void ReleaseAll(DpadButtons pressed) => ApplyTransition(pressed, DpadButtons.None);

    private static void SendKey(ushort scanCode, bool keyUp)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = scanCode,
                    Flags = KeyEventScanCode | KeyEventExtendedKey | (keyUp ? KeyEventKeyUp : 0)
                }
            }
        };

        if (SendInput(1, ref input, Marshal.SizeOf<Input>()) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "矢印キー入力の送信に失敗しました。");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, ref Input input, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput { public uint Message; public ushort ParameterLow; public ushort ParameterHigh; }
}
