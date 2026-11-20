using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace QuotaWidget.Host;

public class TaskbarHost
{
    private readonly Window _window;
    private int _offsetX;
    private readonly DispatcherTimer _timer;

    public TaskbarHost(Window window, int offsetX)
    {
        _window = window;
        _offsetX = offsetX;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => CheckAndPosition();
    }

    public void UpdateOffsetX(int offsetX)
    {
        _offsetX = offsetX;
        CheckAndPosition();
    }

    public void Start()
    {
        ApplyWindowStyles();
        CheckAndPosition();
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    private void ApplyWindowStyles()
    {
        var helper = new WindowInteropHelper(_window);
        var hwnd = helper.Handle;

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        long newExStyle = exStyle.ToInt64() |
                          NativeMethods.WS_EX_TOOLWINDOW |
                          NativeMethods.WS_EX_NOACTIVATE |
                          NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(newExStyle));
    }

    public void CheckAndPosition()
    {
        var helper = new WindowInteropHelper(_window);
        var widgetHwnd = helper.Handle;
        if (widgetHwnd == IntPtr.Zero)
            return;

        IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        PositionOverlay(widgetHwnd, taskbarHwnd);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    /// <summary>True when the foreground window covers its whole monitor (video, game, presentation).</summary>
    private static bool IsFullscreenAppInFront(IntPtr widgetHwnd)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == widgetHwnd) return false;
        var cls = new System.Text.StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var mi = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(fg, 2 /* NEAREST */), ref mi)) return false;
        return r.L <= mi.Monitor.L && r.T <= mi.Monitor.T && r.R >= mi.Monitor.R && r.B >= mi.Monitor.B;
    }

    private void PositionOverlay(IntPtr widgetHwnd, IntPtr taskbarHwnd)
    {
        if (IsFullscreenAppInFront(widgetHwnd))
        {
            ShowWindow(widgetHwnd, 0 /* SW_HIDE */);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        double w = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
        double h = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
        int widgetWidth = (int)(w * dpi.DpiScaleX);
        int widgetHeight = (int)(h * dpi.DpiScaleY);

        int x;
        int y;

        if (taskbarHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(taskbarHwnd, out var taskbarRect))
        {
            int trayHeight = taskbarRect.Bottom - taskbarRect.Top;
            y = taskbarRect.Top + Math.Max(0, (trayHeight - widgetHeight) / 2);
            x = taskbarRect.Left + (int)(_offsetX * dpi.DpiScaleX);
        }
        else
        {
            y = (int)(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY) - widgetHeight;
            x = (int)(_offsetX * dpi.DpiScaleX);
        }

        NativeMethods.SetWindowPos(widgetHwnd, NativeMethods.HWND_TOPMOST, x, y, widgetWidth, widgetHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
