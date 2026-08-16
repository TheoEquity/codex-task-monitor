using System.Runtime.InteropServices;

namespace CodexTaskMonitor.Windows.Interop;

internal static class NativeMethods
{
    private const uint WmMouseWheel = 0x020A;
    private const uint InputMouse = 0;
    private const uint MouseeventfWheel = 0x0800;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static bool IsPointInWindow(nint handle, Point point)
    {
        var pointWindow = WindowFromPoint(point);
        return pointWindow != 0 && GetAncestor(pointWindow, GaRoot) == handle;
    }

    public static bool PostWheel(nint handle, Point point, int delta)
    {
        var wParam = (nint)(delta << 16);
        var lParam = (nint)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
        return PostMessage(handle, WmMouseWheel, wParam, lParam);
    }

    public static bool SendWheel(int delta)
    {
        Input[] inputs =
        [
            new Input
            {
                Type = InputMouse,
                Data = new InputUnion
                {
                    Mouse = new MouseInput
                    {
                        MouseData = unchecked((uint)delta),
                        Flags = MouseeventfWheel
                    }
                }
            }
        ];
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }
}
