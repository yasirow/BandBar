using System;
using System.Drawing;
using System.Runtime.InteropServices;
using static BandBar.NativeMethods;

namespace BandBar
{
    internal enum TaskbarEdge
    {
        Left = 0,
        Top = 1,
        Right = 2,
        Bottom = 3,
    }

    internal readonly struct TaskbarInfo : IEquatable<TaskbarInfo>
    {
        public TaskbarInfo(Rectangle bounds, TaskbarEdge edge, uint dpi, bool autoHide = false)
        {
            Bounds = bounds;
            Edge = edge;
            Dpi = dpi;
            AutoHide = autoHide;
        }

        /// <summary>Physical pixels. For an auto-hiding taskbar, where it is while shown.</summary>
        public Rectangle Bounds { get; }
        public TaskbarEdge Edge { get; }
        public uint Dpi { get; }
        public bool AutoHide { get; }
        public bool IsVertical => Edge == TaskbarEdge.Left || Edge == TaskbarEdge.Right;

        public bool Equals(TaskbarInfo other) =>
            Bounds == other.Bounds && Edge == other.Edge && Dpi == other.Dpi && AutoHide == other.AutoHide;

        public override bool Equals(object obj) => obj is TaskbarInfo other && Equals(other);

        public override int GetHashCode() => Bounds.GetHashCode() ^ ((int)Edge << 24) ^ (int)Dpi;
    }

    /// <summary>Read-only queries about the taskbars (docs/DESIGN.md §5).</summary>
    internal static class Taskbar
    {
        public static bool TryGet(IntPtr hwnd, out TaskbarInfo info)
        {
            info = default;
            RECT rc;
            TaskbarEdge edge;

            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = hwnd };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != UIntPtr.Zero && IsUsable(abd.rc) && abd.uEdge <= 3)
            {
                rc = abd.rc;
                edge = (TaskbarEdge)abd.uEdge;
            }
            else
            {
                // Fallback: the taskbar window's own rectangle, edge inferred from its monitor.
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero || !GetWindowRect(tray, out rc) || !IsUsable(rc))
                    return false;
                edge = InferEdge(rc);
            }

            info = Describe(rc, edge, IsAutoHide(hwnd));
            return true;
        }

        /// <summary>
        /// Another monitor's taskbar (a <c>Shell_SecondaryTrayWnd</c> window, docs/DESIGN.md §5.3): its rectangle, the
        /// edge inferred from its monitor, and that monitor's DPI. Read only.
        /// </summary>
        public static bool TryGetSecondary(IntPtr taskbarWindow, IntPtr hwnd, out TaskbarInfo info)
        {
            info = default;
            if (taskbarWindow == IntPtr.Zero || !GetWindowRect(taskbarWindow, out RECT rc) || !IsUsable(rc))
                return false;
            info = Describe(rc, InferEdge(rc), IsAutoHide(hwnd));
            return true;
        }

        /// <summary>
        /// The other monitors' taskbars, shown by Windows only when "Show my taskbar on all displays" is on. Top-level
        /// windows found by class name, read only; at most 16.
        /// </summary>
        public static System.Collections.Generic.List<IntPtr> FindSecondaryTaskbars()
        {
            var found = new System.Collections.Generic.List<IntPtr>();
            IntPtr window = IntPtr.Zero;
            while (found.Count < 16)
            {
                window = FindWindowEx(IntPtr.Zero, window, "Shell_SecondaryTrayWnd", null);
                if (window == IntPtr.Zero)
                    break;
                if (GetWindowRect(window, out RECT rc) && IsUsable(rc))
                    found.Add(window);
            }
            return found;
        }

        private static TaskbarInfo Describe(RECT rc, TaskbarEdge edge, bool autoHide)
        {
            IntPtr monitor = MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST);
            uint dpi = 96;
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                dpi = dpiX;

            // An auto-hiding taskbar is placed where it appears, not where it is while hidden. So is a taskbar that is
            // still partly off its monitor (auto-hide was just turned off while it was hidden): a correct place for the
            // bar is never off-screen. A taskbar fully on its monitor is left as it is.
            if (TryGetMonitorBounds(monitor, out RECT m) && (autoHide || !IsInside(rc, m)))
                rc = SnapToEdge(rc, edge, m);

            return new TaskbarInfo(Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom), edge, dpi, autoHide);
        }

        /// <summary>The taskbar's auto-hide setting (ABM_GETSTATE), read only.</summary>
        public static bool IsAutoHide(IntPtr hwnd)
        {
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = hwnd };
            return (SHAppBarMessage(ABM_GETSTATE, ref abd).ToUInt64() & ABS_AUTOHIDE) != 0;
        }

        /// <summary>
        /// Whether the taskbar window is entirely on its monitor, i.e. an auto-hiding taskbar has finished sliding in.
        /// </summary>
        public static bool IsFullyShown(IntPtr taskbar, TaskbarInfo info)
        {
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out RECT rc) || !IsUsable(rc))
                return true; // unknown: do not hide the bar because of it
            Rectangle shown = info.Bounds;
            const int tolerance = 2;
            return rc.Left >= shown.Left - tolerance && rc.Top >= shown.Top - tolerance
                && rc.Right <= shown.Right + tolerance && rc.Bottom <= shown.Bottom + tolerance;
        }

        /// <summary>The notification area's window, or zero. Read-only; used for the default position.</summary>
        public static IntPtr FindNotificationArea()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            return tray == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        }

        public static bool TryGetWindowBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rc) || !IsUsable(rc))
                return false;
            bounds = Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom);
            return true;
        }

        private static bool IsUsable(RECT rc) => rc.Right > rc.Left && rc.Bottom > rc.Top;

        private static bool IsInside(RECT rc, RECT m) =>
            rc.Left >= m.Left && rc.Top >= m.Top && rc.Right <= m.Right && rc.Bottom <= m.Bottom;

        private static bool TryGetMonitorBounds(IntPtr monitor, out RECT bounds)
        {
            bounds = default;
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi))
                return false;
            bounds = mi.rcMonitor;
            return true;
        }

        // Keeps the taskbar's thickness and places it against its monitor edge.
        private static RECT SnapToEdge(RECT rc, TaskbarEdge edge, RECT m)
        {
            int width = rc.Right - rc.Left, height = rc.Bottom - rc.Top;
            switch (edge)
            {
                case TaskbarEdge.Top: return new RECT { Left = rc.Left, Right = rc.Right, Top = m.Top, Bottom = m.Top + height };
                case TaskbarEdge.Bottom: return new RECT { Left = rc.Left, Right = rc.Right, Top = m.Bottom - height, Bottom = m.Bottom };
                case TaskbarEdge.Left: return new RECT { Left = m.Left, Right = m.Left + width, Top = rc.Top, Bottom = rc.Bottom };
                default: return new RECT { Left = m.Right - width, Right = m.Right, Top = rc.Top, Bottom = rc.Bottom };
            }
        }

        private static TaskbarEdge InferEdge(RECT rc)
        {
            IntPtr monitor = MonitorFromRect(ref rc, MONITOR_DEFAULTTONEAREST);
            if (!TryGetMonitorBounds(monitor, out RECT m))
                return TaskbarEdge.Bottom;

            if (rc.Right - rc.Left >= rc.Bottom - rc.Top)
                return rc.Top - m.Top <= m.Bottom - rc.Bottom ? TaskbarEdge.Top : TaskbarEdge.Bottom;
            return rc.Left - m.Left <= m.Right - rc.Right ? TaskbarEdge.Left : TaskbarEdge.Right;
        }
    }
}
