using System.Runtime.InteropServices;
using System.Windows;

namespace CodexTaskMonitor.Windows.Interop;

internal interface INativeSidebarWheelApi
{
    bool GetCursorPosition(out Point point);
    bool SetCursorPosition(Point point);
    nint GetForegroundWindow();
    bool SetForegroundWindow(nint handle);
    nint WindowFromPoint(Point point);
    bool IsWindowOwnedBy(nint root, nint window);
    bool PostWheel(nint handle, Point point, int delta);
    bool SendWheel(int delta);
}

internal sealed class WindowsNativeSidebarWheelApi : INativeSidebarWheelApi
{
    public bool GetCursorPosition(out Point point) => NativeMethods.GetCursorPosition(out point);
    public bool SetCursorPosition(Point point) => NativeMethods.SetCursorPosition(point);
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public bool SetForegroundWindow(nint handle) => NativeMethods.SetForegroundWindow(handle);
    public nint WindowFromPoint(Point point) => NativeMethods.WindowFromPoint(point);
    public bool IsWindowOwnedBy(nint root, nint window) => NativeMethods.IsWindowOwnedBy(root, window);
    public bool PostWheel(nint handle, Point point, int delta) => NativeMethods.PostWheel(handle, point, delta);
    public bool SendWheel(int delta) => NativeMethods.SendWheel(delta);
}

internal static class NativeMethods
{
    private const uint WmMouseWheel = 0x020A;
    private const uint InputMouse = 0;
    private const uint MouseeventfWheel = 0x0800;
    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
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
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static bool GetCursorPosition(out Point point)
    {
        if (!GetCursorPos(out var nativePoint))
        {
            point = default;
            return false;
        }

        point = new Point(nativePoint.X, nativePoint.Y);
        return true;
    }

    public static bool SetCursorPosition(Point point) => SetCursorPos((int)point.X, (int)point.Y);

    public static nint WindowFromPoint(Point point) => WindowFromPoint(new NativePoint((int)point.X, (int)point.Y));

    public static bool IsWindowOwnedBy(nint root, nint window) =>
        root != 0 && window != 0 && GetAncestor(window, GaRoot) == root;

    public static bool PostWheel(nint handle, Point point, int delta)
    {
        var wParam = (nint)(delta << 16);
        var x = (int)point.X;
        var y = (int)point.Y;
        var lParam = (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
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
