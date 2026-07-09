using System.Runtime.InteropServices;
using TouhouScalePad.Core;

namespace TouhouScalePad.Services;

public sealed class XInputControllerSource : IControllerSource
{
    private const int MaxControllers = 4;
    private const uint ErrorSuccess = 0;
    private int _preferredControllerIndex = -1;

    public bool TryGetState(out ControllerSnapshot snapshot)
    {
        if (_preferredControllerIndex >= 0 && TryReadController(_preferredControllerIndex, out snapshot))
        {
            return true;
        }

        for (var index = 0; index < MaxControllers; index++)
        {
            if (index != _preferredControllerIndex && TryReadController(index, out snapshot))
            {
                _preferredControllerIndex = index;
                return true;
            }
        }

        _preferredControllerIndex = -1;
        snapshot = default;
        return false;
    }

    private static bool TryReadController(int index, out ControllerSnapshot snapshot)
    {
        if (XInputGetState((uint)index, out var state) != ErrorSuccess)
        {
            snapshot = default;
            return false;
        }

        var buttons = DpadButtons.None;
        if ((state.Gamepad.Buttons & 0x0001) != 0) buttons |= DpadButtons.Up;
        if ((state.Gamepad.Buttons & 0x0002) != 0) buttons |= DpadButtons.Down;
        if ((state.Gamepad.Buttons & 0x0004) != 0) buttons |= DpadButtons.Left;
        if ((state.Gamepad.Buttons & 0x0008) != 0) buttons |= DpadButtons.Right;
        snapshot = new ControllerSnapshot(index, buttons);
        return true;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint PacketNumber; public XInputGamepad Gamepad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
}
