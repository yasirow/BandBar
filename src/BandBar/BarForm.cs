using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using BandBar.Api;
using static BandBar.NativeMethods;

namespace BandBar
{
    /// <summary>
    /// The bar: a borderless, never-activated, topmost, per-pixel-alpha tool window on the taskbar surface
    /// (docs/DESIGN.md). Registers as an AppBar without reserving space, only to receive the shell's taskbar and
    /// full-screen notifications.
    /// </summary>
    internal sealed class BarForm : Form
    {
        private const int AppBarCallbackMessage = WM_APP + 1;
        private const int TrayGapDip = 4;
        private const int FallbackOffsetDip = 220;
        private const string AnchorOffsetKey = "anchorOffsetDip";
        private const int HotkeyId = 1;
        // After a shell event, check about once per frame (the Windows timer resolution, ~16 ms) for ~600 ms whether
        // the taskbar has raised itself over the bar.
        private const int ZOrderCheckIntervalMs = 16;
        private const int ZOrderCheckCount = 38;
        // settings.json is read again this long after the last change notification; editors write in several steps.
        private const int SettingsReloadDelayMs = 300;
        private const int SettingsReloadRetries = 3;
        private static readonly Color InactiveArrowColor = Color.FromArgb(0x8A, 0x8A, 0x8A);
        // Invisible, but keeps every pixel of the bar clickable; fully transparent pixels pass clicks through.
        private static readonly Color HitTestableClear = Color.FromArgb(1, 0, 0, 0);

        private readonly NlMonitor monitor;
        private readonly ToolTip toolTip;
        private readonly ContextMenu menu;
        private readonly MenuItem resetPositionItem;
        private readonly MenuItem openSettingsItem;
        private readonly MenuItem openLogFolderItem;
        private readonly MenuItem zoneMenu;
        private readonly MenuItem limiterItem;
        private readonly MenuItem blockerItem;
        private readonly MenuItem prioritiesItem;
        // Translucent amber: the Blocker has connections waiting for approval (docs/DESIGN.md §4).
        private static readonly Color BlockerAlertBackground = Color.FromArgb(0x80, 0xF3, 0x9C, 0x12);
        private readonly Timer repositionTimer;
        private readonly Timer zOrderTimer;
        private readonly Timer settingsReloadTimer;
        private readonly uint taskbarCreatedMessage;
        // Referenced for the hooks' lifetime: native code calls them, so the GC must not collect them.
        private readonly WinEventProc shellEventProc;
        private readonly WinEventProc taskbarLocationProc;

        private Settings settings;
        private BarLayout layout;
        private Bitmap canvas;
        private FileSystemWatcher settingsWatcher;
        private int settingsReloadAttempts;
        private IntPtr appBarHandle;
        private IntPtr shellEventHook;
        private IntPtr taskbarLocationHook;
        private IntPtr taskbarWindow;
        private TaskbarInfo? currentTaskbar;
        private int zOrderChecksLeft;
        private readonly System.Diagnostics.Stopwatch zOrderClock = new System.Diagnostics.Stopwatch();
        private string zOrderReason = "start-up";
        private int zOrderLogged;
        private IntPtr notificationArea;
        private Rectangle lastNotificationArea;
        private bool started;
        // Set once WinForms has shown the window; hiding it before that would be undone by the first Show.
        private bool ready;
        private bool placed;
        private bool shown = true;
        private bool fullscreen;
        private bool autoHide;
        private bool hiddenWithTaskbar;
        // Hidden at start-up because the shell said a full-screen application was in front (no notification yet).
        private bool fullscreenAssumed;
        // The shell reported full screen, but on another monitor; the bar stayed visible.
        private bool fullscreenElsewhere;
        private bool listedAppInFront;
        private uint listedCachePid;
        private bool listedCacheResult;
        private bool userHidden;
        private bool hotkeyRegistered;
        private bool sessionLocked;
        private bool dragArmed;
        private bool dragging;
        private Point dragStartCursor;
        private Point dragStartLocation;
        private TaskbarInfo dragTaskbar;
        private Reading reading = Reading.Connecting;
        private SpeedText upText = SpeedText.Status("…");
        private SpeedText downText = SpeedText.Status("…");
        private string toolTipText;
        private Color textColor;

        public BarForm(Settings settings)
        {
            this.settings = settings;

            Text = "BandBar";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;

            taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            toolTip = new ToolTip { ShowAlways = true, InitialDelay = 400, AutoPopDelay = 10000 };

            resetPositionItem = new MenuItem(Strings.ResetPosition, (s, e) => ResetPosition());
            openSettingsItem = new MenuItem(Strings.OpenSettings, (s, e) => Launcher.OpenSettingsFile(AppPaths.DataDir));
            openLogFolderItem = new MenuItem(Strings.OpenLogFolder, (s, e) => Launcher.OpenLogFolder(AppPaths.DataDir));
            zoneMenu = new MenuItem(Strings.Zone);
            zoneMenu.MenuItems.Add(new MenuItem(Strings.NotConnected) { Enabled = false });
            // Information only: BandBar never switches NetLimiter's features (docs/DESIGN.md §7.1).
            limiterItem = new MenuItem(Strings.Limiter) { Enabled = false };
            blockerItem = new MenuItem(Strings.Blocker) { Enabled = false };
            prioritiesItem = new MenuItem(Strings.Priorities) { Enabled = false };
            menu = new ContextMenu(new[]
            {
                zoneMenu,
                new MenuItem("-"),
                limiterItem,
                blockerItem,
                prioritiesItem,
                new MenuItem(Strings.OpenNetLimiter, (s, e) => Launcher.OpenNetLimiter()),
                new MenuItem("-"),
                resetPositionItem,
                openSettingsItem,
                openLogFolderItem,
                new MenuItem(Strings.ReleasesPage, (s, e) => Launcher.OpenReleasesPage()),
                new MenuItem("-"),
                new MenuItem(Strings.Exit, (s, e) => Close()),
            });
            menu.Popup += (s, e) => UpdateMenu();

            // Coalesces bursts of shell notifications into one reposition; not a polling timer.
            repositionTimer = new Timer { Interval = 100 };
            repositionTimer.Tick += (s, e) =>
            {
                repositionTimer.Stop();
                Reposition();
            };

            // Runs only for a short while after a shell event: the taskbar may raise itself a little after the
            // click that triggered the event (docs/DESIGN.md). Stops by itself; never a standing poll.
            zOrderTimer = new Timer { Interval = ZOrderCheckIntervalMs };
            zOrderTimer.Tick += (s, e) =>
            {
                RaiseIfCoveredByTaskbar();
                if (--zOrderChecksLeft <= 0)
                {
                    zOrderTimer.Stop();
                    LogIfStillCovered();
                }
            };

            // One-shot, started by a change notification for settings.json; never a standing poll.
            settingsReloadTimer = new Timer { Interval = SettingsReloadDelayMs };
            settingsReloadTimer.Tick += (s, e) =>
            {
                settingsReloadTimer.Stop();
                ReloadSettings();
            };

            shellEventProc = OnShellEvent;
            taskbarLocationProc = OnTaskbarLocationChanged;
            textColor = settings.ResolveTextColor();
            monitor = new NlMonitor(settings.ZoneFilterId, PostReading);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // A popup, not WinForms' overlapped window: overlapped windows get Windows' minimum size (39 px tall
                // at 100 %), which would overflow a thin taskbar.
                cp.Style = (cp.Style & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)) | WS_POPUP;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterAppBar();
            Reposition();
            UpdateToolTip();

            if (!started)
            {
                started = true;
                SystemEvents.SessionSwitch += OnSessionSwitch;

                // Out of context: Windows posts the events to this thread's message loop; nothing is loaded into
                // any other process (docs/DESIGN.md).
                shellEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_CAPTUREEND, IntPtr.Zero,
                    shellEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (shellEventHook == IntPtr.Zero)
                    Log.Warn("Shell event notifications unavailable; the taskbar may cover the bar after a click");

                StartSettingsWatcher();
                monitor.Start();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ready = true;
            // Both may hide the bar at once, so only now that the window is shown.
            UpdateAutoHide();
            CheckFullscreenAtStart();
            UpdateHotkey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterAppBar();
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            if (shellEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(shellEventHook);
                shellEventHook = IntPtr.Zero;
            }
            RemoveTaskbarLocationHook();
            UnregisterOwnHotkey();
            StopSettingsWatcher();
            repositionTimer.Stop();
            zOrderTimer.Stop();
            settingsReloadTimer.Stop();
            monitor.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                monitor.Dispose();
                StopSettingsWatcher();
                repositionTimer.Dispose();
                zOrderTimer.Dispose();
                settingsReloadTimer.Dispose();
                toolTip.Dispose();
                menu.Dispose();
                layout?.Dispose();
                canvas?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)MA_NOACTIVATE;
                    return;
                case AppBarCallbackMessage:
                    OnAppBarNotification((int)m.WParam.ToInt64(), m.LParam);
                    return;
                case WM_DPICHANGED:
                    // We size ourselves from the taskbar; ignore the suggested rectangle.
                    ScheduleReposition();
                    m.Result = IntPtr.Zero;
                    return;
                case WM_DISPLAYCHANGE:
                    ScheduleReposition();
                    break;
                case WM_SETTINGCHANGE:
                    if (m.WParam.ToInt64() == SPI_SETWORKAREA)
                        ScheduleReposition();
                    else if (m.WParam.ToInt64() == SPI_SETHIGHCONTRAST)
                        OnThemeChanged(); // contrast theme switched on or off
                    else if (m.LParam != IntPtr.Zero && Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                        OnThemeChanged(); // light/dark mode switched
                    break;
                case WM_SYSCOLORCHANGE:
                    OnThemeChanged(); // contrast theme colours changed
                    break;
                case WM_DWMCOLORIZATIONCOLORCHANGED:
                    OnThemeChanged(); // accent colour changed
                    break;
                case WM_HOTKEY:
                    if (m.WParam.ToInt64() == HotkeyId)
                    {
                        userHidden = !userHidden;
                        Log.Info(userHidden ? "Hidden with the hotkey" : "Shown with the hotkey");
                        ApplyVisibility();
                    }
                    return;
            }

            if (taskbarCreatedMessage != 0 && m.Msg == (int)taskbarCreatedMessage)
            {
                // Explorer restarted: the old AppBar registration, window handles and taskbar thread are gone with it.
                Log.Info("Taskbar recreated; registering again");
                appBarHandle = IntPtr.Zero;
                RemoveTaskbarLocationHook();
                taskbarWindow = IntPtr.Zero;
                currentTaskbar = null; // the new taskbar may be elsewhere; judge it by its own rectangle
                notificationArea = IntPtr.Zero;
                RegisterAppBar();
                fullscreenAssumed = fullscreenElsewhere = false;
                SetFullscreen(false);
                UpdateAutoHide();
                CheckFullscreenAtStart(); // the new taskbar reports only later changes
                ScheduleReposition();
            }

            base.WndProc(ref m);
        }

        // ---- Mouse: drag along the taskbar (docs/DESIGN.md §5.1), menu on right-click ----

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !shown || currentTaskbar == null)
                return;
            // WinForms captures the mouse for our own window until the button is released; no hook is involved.
            dragArmed = true;
            dragging = false;
            dragTaskbar = currentTaskbar.Value;
            dragStartCursor = Cursor.Position;
            dragStartLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragArmed)
                return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
            {
                CancelDrag();
                return;
            }

            Point cursor = Cursor.Position;
            if (!dragging)
            {
                // Within the system drag threshold it is still a click.
                Size threshold = SystemInformation.DragSize;
                if (Math.Abs(cursor.X - dragStartCursor.X) <= threshold.Width / 2
                    && Math.Abs(cursor.Y - dragStartCursor.Y) <= threshold.Height / 2)
                    return;
                dragging = true;
                toolTip.Hide(this);
                toolTip.Active = false;
                repositionTimer.Stop();
            }

            Cursor.Current = dragTaskbar.IsVertical ? Cursors.SizeNS : Cursors.SizeWE;
            Rectangle tb = dragTaskbar.Bounds;
            Point location = dragStartLocation;
            if (dragTaskbar.IsVertical)
                location.Y = Clamp(dragStartLocation.Y + cursor.Y - dragStartCursor.Y, tb.Top, tb.Bottom - Height);
            else
                location.X = Clamp(dragStartLocation.X + cursor.X - dragStartCursor.X, tb.Left, tb.Right - Width);
            if (location != Location)
                Location = location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                bool moved = dragging;
                bool clicked = dragArmed && !moved;
                EndDrag();
                if (moved)
                    SaveDraggedPosition();
                else if (clicked && reading.BlockerRequests > 0)
                    Launcher.OpenNetLimiter(); // a plain click while the Blocker waits for an answer (docs/DESIGN.md §4)
                return;
            }
            if (e.Button != MouseButtons.Right || dragArmed)
                return;

            toolTip.Hide(this);
            // The one deliberate activation: without it the menu would not close when the user clicks elsewhere.
            SetForegroundWindow(Handle);
            menu.Show(this, e.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // Capture taken away before the button was released (another window took it): put the bar back.
            if (dragArmed && !Capture)
                CancelDrag();
        }

        private void EndDrag()
        {
            dragArmed = false;
            dragging = false;
            toolTip.Active = true;
            Cursor.Current = Cursors.Default;
        }

        private void CancelDrag()
        {
            bool moved = dragging;
            EndDrag();
            if (moved)
                ScheduleReposition();
        }

        private void SaveDraggedPosition()
        {
            Rectangle tb = dragTaskbar.Bounds;
            int fromEnd = dragTaskbar.IsVertical ? tb.Bottom - Bounds.Bottom : tb.Right - Bounds.Right;
            int offsetDip = Math.Max(0, (int)Math.Round(fromEnd * 96.0 / dragTaskbar.Dpi));
            settings.AnchorOffsetDip = offsetDip;
            if (SettingsFile.SetNumber(AppPaths.DataDir, AnchorOffsetKey, offsetDip))
                Log.Info("Position saved: " + offsetDip + " DIP from the end of the taskbar");
            else
                Log.Warn("The new position could not be saved; it applies until BandBar exits");
            Reposition();
        }

        // ---- Menu (docs/DESIGN.md §4) ----

        private void UpdateMenu()
        {
            // The menu shows what is known now; ask for fresh zones and states for the next opening.
            monitor.RequestDetails();
            resetPositionItem.Enabled = settings.AnchorOffsetDip.HasValue;
            openSettingsItem.Enabled = openLogFolderItem.Enabled = AppPaths.DataDir != null;

            NlDetails details = reading.Status == LinkStatus.Connected ? reading.Details : null;
            foreach (MenuItem old in zoneMenu.MenuItems.Cast<MenuItem>().ToList())
                old.Dispose();
            if (details == null || details.Zones.Count == 0)
            {
                zoneMenu.MenuItems.Add(new MenuItem(Strings.NotConnected) { Enabled = false });
            }
            else
            {
                foreach (ZoneInfo zone in details.Zones)
                {
                    ZoneInfo chosen = zone;
                    zoneMenu.MenuItems.Add(new MenuItem((zone.Name ?? zone.Id ?? "?").Replace("&", "&&"), (s, e) => SelectZone(chosen))
                    {
                        RadioCheck = true,
                        Checked = zone.Id == reading.ZoneId,
                    });
                }
            }

            limiterItem.Visible = blockerItem.Visible = prioritiesItem.Visible = details != null;
            limiterItem.Checked = details?.LimiterOn ?? false;
            blockerItem.Checked = details?.BlockerOn ?? false;
            prioritiesItem.Checked = details?.PrioritiesOn ?? false;
        }

        // Saved like a dragged position: only this key changes (docs/DESIGN.md §8.1). The zone's name is not logged.
        private void SelectZone(ZoneInfo zone)
        {
            string id = zone.IsInternet ? null : zone.Id;
            if (id == settings.ZoneFilterId)
                return;
            settings.ZoneFilterId = id;
            monitor.SetZone(id);
            if (SettingsFile.SetString(AppPaths.DataDir, "zoneFilterId", id))
                Log.Info("Zone changed");
            else
                Log.Warn("The zone choice could not be saved; it applies until BandBar exits");
        }

        private void ResetPosition()
        {
            settings.AnchorOffsetDip = null;
            if (SettingsFile.SetNumber(AppPaths.DataDir, AnchorOffsetKey, null))
                Log.Info("Position reset to the default");
            else
                Log.Warn("The position reset could not be saved; it applies until BandBar exits");
            Reposition();
        }

        // ---- Shell notifications ----

        private void RegisterAppBar()
        {
            var abd = new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)),
                hWnd = Handle,
                uCallbackMessage = AppBarCallbackMessage,
            };
            // ABM_NEW only; ABM_SETPOS is never sent, so no screen space is reserved and the taskbar is untouched.
            if (SHAppBarMessage(ABM_NEW, ref abd) != UIntPtr.Zero)
                appBarHandle = Handle;
            else
                Log.Warn("AppBar registration failed; full-screen and taskbar notifications are unavailable");
        }

        private void UnregisterAppBar()
        {
            if (appBarHandle == IntPtr.Zero)
                return;
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf(typeof(APPBARDATA)), hWnd = appBarHandle };
            SHAppBarMessage(ABM_REMOVE, ref abd);
            appBarHandle = IntPtr.Zero;
        }

        private void OnAppBarNotification(int code, IntPtr lParam)
        {
            switch (code)
            {
                case ABN_FULLSCREENAPP:
                    OnFullscreenNotification(lParam != IntPtr.Zero);
                    break;
                case ABN_STATECHANGE:
                    UpdateAutoHide();
                    ScheduleReposition();
                    break;
                case ABN_POSCHANGED:
                    ScheduleReposition();
                    break;
            }
        }

        // ---- Visibility: full screen, listed programs, the hotkey and the auto-hiding taskbar
        //      (docs/DESIGN.md §5.2, §6.1) ----

        private void OnFullscreenNotification(bool opened)
        {
            fullscreenAssumed = false;
            fullscreenElsewhere = false;
            if (opened && !FullscreenOnOurMonitor(GetForegroundWindow()))
            {
                // Another monitor's game: stay visible, but watch foreground changes in case one comes here.
                fullscreenElsewhere = true;
                Log.Info("Full-screen application on another monitor; staying visible");
                opened = false;
            }
            SetFullscreen(opened);
        }

        // The shell reports changes only: a game that is already full screen when the bar starts is found by asking.
        private void CheckFullscreenAtStart()
        {
            if (!settings.HideOnFullscreen || !ShellReportsFullscreen())
                return;
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !CoversItsMonitor(foreground) || !FullscreenOnOurMonitor(foreground))
                return;
            Log.Info("A full-screen application is already in front");
            fullscreenAssumed = true;
            SetFullscreen(true);
        }

        /// <summary>
        /// Whether a full-screen window is on the bar's monitor. When unsure (no window, or one that covers no monitor),
        /// yes: not disturbing a game ranks above being visible (docs/DESIGN.md §1).
        /// </summary>
        private bool FullscreenOnOurMonitor(IntPtr window)
        {
            if (window == IntPtr.Zero || window == GetShellWindow() || !CoversItsMonitor(window))
                return true;
            return MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST) == MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        }

        private static bool ShellReportsFullscreen() =>
            SHQueryUserNotificationState(out int state) == 0
            && (state == QUNS_BUSY || state == QUNS_RUNNING_D3D_FULL_SCREEN);

        // Every foreground change, also while the bar is hidden: listed programs and the full-screen fallbacks.
        private void OnForegroundChanged(IntPtr window)
        {
            if (settings.ListedApps.Length > 0)
            {
                bool listed = IsListedApp(window);
                if (listed != listedAppInFront)
                {
                    listedAppInFront = listed;
                    // The program's name is not logged: the log may be attached to a public issue.
                    Log.Info(listed ? "A program from fullscreenExeList is in front; hiding" : "No program from fullscreenExeList is in front");
                    ApplyVisibility();
                }
            }

            if (fullscreen && fullscreenAssumed)
            {
                // Hidden at start-up without a shell notification: come back once the shell no longer reports one.
                if (!ShellReportsFullscreen())
                {
                    fullscreenAssumed = false;
                    SetFullscreen(false);
                }
            }
            else if (!fullscreen && fullscreenElsewhere && window != GetShellWindow() && CoversItsMonitor(window)
                     && FullscreenOnOurMonitor(window) && ShellReportsFullscreen())
            {
                // A full-screen window came to the bar's monitor without a new notification.
                fullscreenElsewhere = false;
                SetFullscreen(true);
            }
        }

        // The image name from the process list snapshot, cached per process ID; never OpenProcess (docs/DESIGN.md §6.1).
        private bool IsListedApp(IntPtr window)
        {
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint processId) == 0 || processId == 0)
                return false;
            if (processId != listedCachePid)
            {
                listedCachePid = processId;
                listedCacheResult = AppList.Contains(settings.ListedApps, AppList.ImageNameOf(processId));
            }
            return listedCacheResult;
        }

        private void SetFullscreen(bool value)
        {
            if (!settings.HideOnFullscreen)
                value = false;
            if (fullscreen == value)
                return;

            fullscreen = value;
            Log.Info(value ? "Full-screen application opened; hiding" : "Full-screen application closed");
            ApplyVisibility();
        }

        // ---- Show/hide hotkey (docs/DESIGN.md §6.1.1): RegisterHotKey, never a keyboard hook ----

        private void UpdateHotkey()
        {
            if (!ready || !IsHandleCreated || IsDisposed)
                return;
            UnregisterOwnHotkey();

            if (settings.Hotkey != null)
            {
                if (!Hotkey.TryParse(settings.Hotkey, out uint modifiers, out uint key))
                    Log.Warn("The hotkey setting is not understood (example: \"Ctrl+Alt+B\"); no hotkey");
                else if (RegisterHotKey(Handle, HotkeyId, modifiers | MOD_NOREPEAT, key))
                    hotkeyRegistered = true;
                else
                    Log.Warn("The hotkey is already used by another program; no hotkey");
            }

            if (!hotkeyRegistered && userHidden)
            {
                // Without a hotkey there would be no way to bring the bar back.
                userHidden = false;
                ApplyVisibility();
            }
        }

        private void UnregisterOwnHotkey()
        {
            if (!hotkeyRegistered)
                return;
            UnregisterHotKey(Handle, HotkeyId);
            hotkeyRegistered = false;
        }

        private void ApplyVisibility()
        {
            bool show = !fullscreen && !hiddenWithTaskbar && !listedAppInFront && !userHidden;
            if (show != shown)
            {
                shown = show;
                if (!show)
                {
                    toolTip.Hide(this);
                    zOrderTimer.Stop();
                    CancelDrag();
                    // Hidden and out of the topmost band: nothing of ours overlaps a game (docs/DESIGN.md §6.1).
                    SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_HIDEWINDOW);
                }
                else
                {
                    Reposition();
                    if (!shown)
                        return; // the taskbar check inside Reposition hid the bar again; stay hidden
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
                    // The taskbar shows itself at about the same time and may land on top of us.
                    WatchZOrder("the bar was shown again");
                }
            }
            UpdatePaused();
        }

        private void UpdateAutoHide()
        {
            if (!ready || !IsHandleCreated || IsDisposed)
                return;
            bool value = Taskbar.IsAutoHide(Handle);
            if (value != autoHide)
            {
                autoHide = value;
                Log.Info(value ? "The taskbar auto-hides" : "The taskbar no longer auto-hides");
            }

            if (autoHide)
                InstallTaskbarLocationHook();
            else
                RemoveTaskbarLocationHook();
            UpdateTaskbarShown();
        }

        // Only while the taskbar auto-hides, and only for the taskbar's own thread: the shell does not report the
        // taskbar sliding in or out (docs/DESIGN.md §5.2). Out of context, like the shell event range.
        private void InstallTaskbarLocationHook()
        {
            IntPtr taskbar = GetTaskbarWindow();
            if (taskbarLocationHook != IntPtr.Zero || taskbar == IntPtr.Zero)
                return;
            uint thread = GetWindowThreadProcessId(taskbar, out uint process);
            if (thread != 0)
                taskbarLocationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero,
                    taskbarLocationProc, process, thread, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (taskbarLocationHook == IntPtr.Zero)
                Log.Warn("Taskbar movement notifications unavailable; the bar stays visible while the taskbar is hidden");
        }

        private void RemoveTaskbarLocationHook()
        {
            if (taskbarLocationHook == IntPtr.Zero)
                return;
            UnhookWinEvent(taskbarLocationHook);
            taskbarLocationHook = IntPtr.Zero;
        }

        private void OnTaskbarLocationChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
                                              uint eventThread, uint eventTime)
        {
            try
            {
                if (IsDisposed || hwnd == IntPtr.Zero || hwnd != taskbarWindow || idObject != OBJID_WINDOW)
                    return; // other windows of the taskbar's thread
                UpdateTaskbarShown();
                RaiseIfCoveredByTaskbar();
            }
            catch (Exception ex)
            {
                // Called from native code: never let an exception escape.
                Log.Debug("Taskbar location handler failed", ex);
            }
        }

        private void UpdateTaskbarShown()
        {
            bool hide = false;
            // Without the location notification the bar could not come back, so it then never hides with the taskbar.
            if (autoHide && taskbarLocationHook != IntPtr.Zero && settings.FollowAutoHideTaskbar)
            {
                TaskbarInfo info;
                if (currentTaskbar.HasValue)
                    info = currentTaskbar.Value;
                else if (!Taskbar.TryGet(Handle, out info))
                    return;
                hide = !Taskbar.IsFullyShown(GetTaskbarWindow(), info);
            }
            if (hide == hiddenWithTaskbar)
                return;
            hiddenWithTaskbar = hide;
            Log.Debug(hide ? "Taskbar slid out; hiding" : "Taskbar slid in; showing");
            ApplyVisibility();
        }

        // ---- Window order (docs/DESIGN.md §6.2) ----

        // Foreground, menu and mouse-capture events from any process, delivered out of context through our message
        // loop. Pressing a taskbar button raises a capture event at once; the foreground event covers the rest.
        private void OnShellEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
                                  uint eventThread, uint eventTime)
        {
            try
            {
                if (IsDisposed)
                    return;
                if (eventType == EVENT_SYSTEM_FOREGROUND && ready)
                    OnForegroundChanged(hwnd); // also while hidden: this is how the bar comes back
                if (!shown)
                    return;
                if (eventType == EVENT_SYSTEM_FOREGROUND)
                {
                    // A full-screen app is coming forward; ABN_FULLSCREENAPP hides the bar instead. The desktop also
                    // covers the monitor, but clicking it (for example to close Start) is when the taskbar raises itself.
                    if (CoversItsMonitor(hwnd) && hwnd != GetShellWindow())
                        return;
                }
                else if (!BelongsToTaskbar(hwnd))
                {
                    return; // menu and mouse-capture events matter only when they happen on the taskbar
                }
                WatchZOrder(eventType == EVENT_SYSTEM_FOREGROUND ? "a foreground change"
                          : eventType <= EVENT_SYSTEM_MENUPOPUPEND ? "a taskbar menu event" : "a taskbar mouse capture");
                RaiseIfCoveredByTaskbar();
            }
            catch (Exception ex)
            {
                // Called from native code: never let an exception escape.
                Log.Debug("Shell event handler failed", ex);
            }
        }

        private void WatchZOrder(string reason)
        {
            zOrderChecksLeft = ZOrderCheckCount;
            zOrderReason = reason;
            zOrderLogged = 0;
            zOrderClock.Restart();
            zOrderTimer.Stop();
            zOrderTimer.Start();
        }

        /// <param name="foundBy">For the debug log: a check that is not part of an event's watch window.</param>
        private void RaiseIfCoveredByTaskbar(string foundBy = null)
        {
            if (!shown || !IsHandleCreated || IsDisposed || !IsCoveredByTaskbar())
                return;
            if (foundBy != null && Log.IsDebugEnabled)
                Log.Debug("Taskbar above the bar, found by " + foundBy + ", " + zOrderClock.ElapsedMilliseconds
                          + " ms after " + zOrderReason);
            // Directly above the taskbar rather than at the very top, so Start, jump lists and tooltips stay above us.
            IntPtr above = GetWindow(taskbarWindow, GW_HWNDPREV);
            SetWindowPos(Handle, above == IntPtr.Zero ? HWND_TOPMOST : above, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

            // Diagnostics only (logLevel "debug"): when the taskbar got above us, and whether moving up helped.
            if (Log.IsDebugEnabled)
            {
                bool stillCovered = IsCoveredByTaskbar();
                int state = stillCovered ? 2 : 1;
                if (state != zOrderLogged)
                {
                    zOrderLogged = state;
                    Log.Debug("Taskbar above the bar " + zOrderClock.ElapsedMilliseconds + " ms after " + zOrderReason
                              + (stillCovered ? "; still above after moving up (the taskbar is in a higher band)" : "; moved up"));
                }
            }
        }

        // Diagnostics only: the window of checks after an event ended with the taskbar still above the bar.
        private void LogIfStillCovered()
        {
            if (Log.IsDebugEnabled && shown && IsCoveredByTaskbar())
                Log.Debug("Taskbar still above the bar when checking stopped, " + zOrderClock.ElapsedMilliseconds
                          + " ms after " + zOrderReason);
        }

        private IntPtr GetTaskbarWindow()
        {
            if (taskbarWindow == IntPtr.Zero)
                taskbarWindow = FindWindow("Shell_TrayWnd", null);
            return taskbarWindow;
        }

        private bool BelongsToTaskbar(IntPtr hwnd)
        {
            IntPtr taskbar = GetTaskbarWindow();
            return taskbar != IntPtr.Zero && hwnd != IntPtr.Zero && GetAncestor(hwnd, GA_ROOT) == taskbar;
        }

        // Reads the z-order only: is the taskbar somewhere above the bar?
        private bool IsCoveredByTaskbar()
        {
            if (GetTaskbarWindow() == IntPtr.Zero)
                return false;

            IntPtr window = Handle;
            for (int i = 0; i < 1024; i++)
            {
                window = GetWindow(window, GW_HWNDPREV);
                if (window == IntPtr.Zero)
                    return false;
                if (window == taskbarWindow)
                    return true;
            }
            return false;
        }

        private static bool CoversItsMonitor(IntPtr hwnd)
        {
            if (!GetWindowRect(hwnd, out RECT window))
                return false;
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi))
                return false;
            RECT m = mi.rcMonitor;
            return window.Left <= m.Left && window.Top <= m.Top && window.Right >= m.Right && window.Bottom >= m.Bottom;
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            bool? locked = e.Reason == SessionSwitchReason.SessionLock ? true
                         : e.Reason == SessionSwitchReason.SessionUnlock ? false
                         : (bool?)null;
            if (locked == null)
                return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    sessionLocked = locked.Value;
                    UpdatePaused();
                }));
            }
            catch (InvalidOperationException)
            {
                // Window already gone during shutdown.
            }
        }

        private void UpdatePaused() => monitor.SetPaused(!shown || sessionLocked);

        // ---- Placement (docs/DESIGN.md §5) ----

        private void ScheduleReposition()
        {
            repositionTimer.Stop();
            repositionTimer.Start();
        }

        private void Reposition()
        {
            if (IsDisposed || !IsHandleCreated || dragging)
                return;

            if (Taskbar.TryGet(Handle, out TaskbarInfo taskbar))
            {
                bool changed = !currentTaskbar.HasValue || !currentTaskbar.Value.Equals(taskbar);
                currentTaskbar = taskbar;
                if (taskbar.AutoHide != autoHide)
                    UpdateAutoHide();
                else if (changed && autoHide)
                    UpdateTaskbarShown(); // location events may have been judged against the old rectangle
            }
            else if (placed)
            {
                // Explorer is restarting: stay where we are and wait for TaskbarCreated.
                Log.Info("Taskbar position unavailable; keeping the current position");
                notificationArea = IntPtr.Zero;
                return;
            }
            else
            {
                Log.Warn("Taskbar position unavailable; using the bottom of the primary screen");
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                taskbar = new TaskbarInfo(new Rectangle(screen.Left, screen.Bottom - 48, screen.Width, 48), TaskbarEdge.Bottom, 96);
            }

            Rectangle tb = taskbar.Bounds;
            int thickness = taskbar.IsVertical ? tb.Width : tb.Height;
            if (layout == null || !layout.Fits(taskbar.Dpi, taskbar.IsVertical, thickness))
            {
                layout?.Dispose();
                layout = new BarLayout(taskbar.Dpi, taskbar.IsVertical, thickness, SpeedFormat.WidestNumber(settings),
                    SpeedFormat.Units, Enum.GetValues(typeof(LinkStatus)).Cast<LinkStatus>().Select(Strings.Short)
                                                .Concat(new[] { Strings.FilteringOffShort }));
            }

            Size size = layout.Size;
            Rectangle target;
            if (!taskbar.IsVertical)
            {
                int right = FindAnchorEnd(taskbar, layout);
                int x = Math.Max(tb.Left, right - size.Width);
                int y = tb.Top + Math.Max(0, (tb.Height - size.Height) / 2);
                target = new Rectangle(x, y, size.Width, size.Height);
            }
            else
            {
                int bottom = FindAnchorEnd(taskbar, layout);
                int y = Math.Max(tb.Top, bottom - size.Height);
                int x = size.Width <= tb.Width ? tb.Left + (tb.Width - size.Width) / 2
                      : taskbar.Edge == TaskbarEdge.Left ? tb.Left
                      : tb.Right - size.Width;
                target = new Rectangle(x, y, size.Width, size.Height);
            }

            if (Bounds != target)
            {
                Bounds = target;
                Log.Debug("Positioned on the " + taskbar.Edge + " taskbar " + tb + " at dpi " + taskbar.Dpi
                          + (layout.Stacked ? " (stacked)" : "") + ": target " + target + ", actual " + Bounds);
            }
            placed |= currentTaskbar.HasValue;
            RaiseIfCoveredByTaskbar();
            Render();
        }

        /// <summary>
        /// Where the bar ends along the taskbar (right edge on a horizontal taskbar, bottom on a vertical one): just
        /// before the notification area by default, or the saved offset from the taskbar's end.
        /// </summary>
        private int FindAnchorEnd(TaskbarInfo taskbar, BarLayout metrics)
        {
            Rectangle tb = taskbar.Bounds;
            if (settings.AnchorOffsetDip.HasValue)
                return (taskbar.IsVertical ? tb.Bottom : tb.Right) - metrics.Scale(settings.AnchorOffsetDip.Value);

            if (notificationArea == IntPtr.Zero)
                notificationArea = Taskbar.FindNotificationArea();
            if (Taskbar.TryGetWindowBounds(notificationArea, out Rectangle tray))
            {
                Rectangle shownTray = WhereShown(tray, taskbar);
                if (tb.Contains(shownTray))
                {
                    lastNotificationArea = tray; // as FollowNotificationArea will read it
                    return (taskbar.IsVertical ? shownTray.Top : shownTray.Left) - metrics.Scale(TrayGapDip);
                }
            }

            notificationArea = IntPtr.Zero;
            lastNotificationArea = Rectangle.Empty;
            return (taskbar.IsVertical ? tb.Bottom : tb.Right) - metrics.Scale(FallbackOffsetDip);
        }

        // While an auto-hiding taskbar is hidden or sliding, its notification area is off-screen with it: use the place
        // the notification area will have once the taskbar is shown.
        private Rectangle WhereShown(Rectangle tray, TaskbarInfo taskbar)
        {
            IntPtr window = GetTaskbarWindow();
            if (Taskbar.IsFullyShown(window, taskbar) || !Taskbar.TryGetWindowBounds(window, out Rectangle now))
                return tray;
            if (taskbar.IsVertical)
                tray.Offset(taskbar.Bounds.X - now.X, 0);
            else
                tray.Offset(0, taskbar.Bounds.Y - now.Y);
            return tray;
        }

        // The shell sends no notification when the notification area grows (a new tray icon, a wider clock), so in
        // the default position its rectangle is compared on each reading the bar already receives once a second.
        // One GetWindowRect, no extra timer.
        private void FollowNotificationArea()
        {
            if (settings.AnchorOffsetDip.HasValue || notificationArea == IntPtr.Zero || !shown || dragging)
                return;
            if (!Taskbar.TryGetWindowBounds(notificationArea, out Rectangle tray) || tray != lastNotificationArea)
                ScheduleReposition();
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        // ---- settings.json, applied without a restart (docs/DESIGN.md §8.1) ----

        private void StartSettingsWatcher()
        {
            string dir = AppPaths.DataDir;
            if (dir == null)
                return;
            try
            {
                // Our own folder, our own file name only; events arrive on this (the UI) thread.
                settingsWatcher = new FileSystemWatcher(dir, AppPaths.SettingsFileName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    SynchronizingObject = this,
                };
                settingsWatcher.Changed += OnSettingsFileChanged;
                settingsWatcher.Created += OnSettingsFileChanged;
                settingsWatcher.Deleted += OnSettingsFileChanged;
                settingsWatcher.Renamed += OnSettingsFileChanged;
                settingsWatcher.Error += (s, e) =>
                    Log.Warn("Watching settings.json stopped; later changes apply after a restart", e.GetException());
                settingsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is PlatformNotSupportedException)
            {
                Log.Warn("settings.json changes will apply after a restart", ex);
                StopSettingsWatcher();
            }
        }

        private void StopSettingsWatcher()
        {
            if (settingsWatcher == null)
                return;
            settingsWatcher.EnableRaisingEvents = false;
            settingsWatcher.Dispose();
            settingsWatcher = null;
        }

        private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
        {
            settingsReloadAttempts = 0;
            settingsReloadTimer.Stop();
            settingsReloadTimer.Start();
        }

        private void ReloadSettings()
        {
            switch (Settings.TryLoad(AppPaths.DataDir, out Settings fresh))
            {
                case SettingsLoad.Loaded:
                    ApplySettings(fresh);
                    break;
                case SettingsLoad.Unreadable when settingsReloadAttempts++ < SettingsReloadRetries:
                    settingsReloadTimer.Start(); // still being written; try again shortly
                    break;
                default:
                    Log.Warn("Keeping the current settings");
                    break;
            }
        }

        private void ApplySettings(Settings fresh)
        {
            Settings old = settings;
            settings = fresh;
            Log.SetLevel(fresh.LogLevel);
            if (fresh.ZoneFilterId != old.ZoneFilterId)
                monitor.SetZone(fresh.ZoneFilterId);
            textColor = fresh.ResolveTextColor();

            // Units and decimals decide the bar's width: measure again.
            layout?.Dispose();
            layout = null;
            (upText, downText) = TextsFor(reading);

            if (fullscreen && !fresh.HideOnFullscreen)
                SetFullscreen(false);

            // The list may have changed: judge the program in front again.
            listedCachePid = 0;
            if (listedAppInFront || fresh.ListedApps.Length > 0)
            {
                listedAppInFront = fresh.ListedApps.Length > 0 && IsListedApp(GetForegroundWindow());
                ApplyVisibility();
            }
            if (fresh.Hotkey != old.Hotkey)
                UpdateHotkey();

            UpdateTaskbarShown();
            Reposition();
            Log.Info("Settings applied");
        }

        // ---- Drawing and readings ----

        private void Render()
        {
            if (layout == null || !IsHandleCreated || IsDisposed)
                return;

            bool live = reading.Status == LinkStatus.Connected && !reading.FilteringOff;
            Size size = new Size(Math.Max(1, layout.Size.Width), Math.Max(1, layout.Size.Height));
            // The buffer is reused while the size stays the same (docs/DESIGN.md §4); Clear repaints every pixel.
            if (canvas == null || canvas.Size != size)
            {
                canvas?.Dispose();
                canvas = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            }
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(reading.BlockerRequests > 0 ? BlockerAlertBackground : settings.BackgroundColor ?? HitTestableClear);
                layout.Draw(g, upText, downText,
                    live ? settings.UploadColor : InactiveArrowColor,
                    live ? settings.DownloadColor : InactiveArrowColor,
                    textColor);
            }
            int error = LayeredWindow.Update(Handle, canvas, Location);
            if (error != 0)
                Log.Debug("UpdateLayeredWindow failed (" + error + ")");
        }

        private void OnThemeChanged()
        {
            Color color = settings.ResolveTextColor();
            if (color == textColor)
                return;
            textColor = color;
            Log.Info("Taskbar theme changed; text colour updated");
            Render();
        }

        // Called on the monitor thread.
        private void PostReading(Reading r)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke((Action)(() => ApplyReading(r)));
            }
            catch (InvalidOperationException)
            {
                // Handle destroyed during shutdown.
            }
        }

        private (SpeedText Up, SpeedText Down) TextsFor(Reading r)
        {
            switch (r.Status)
            {
                case LinkStatus.Connected when r.FilteringOff:
                    return (SpeedText.Status(Strings.FilteringOffShort), SpeedText.Status(""));
                case LinkStatus.Connected:
                    return (SpeedFormat.Format(r.Up, settings), SpeedFormat.Format(r.Down, settings));
                case LinkStatus.Connecting:
                case LinkStatus.NoService:
                    SpeedText status = SpeedText.Status(Strings.Short(r.Status));
                    return (status, status);
                default:
                    return (SpeedText.Status(Strings.Short(r.Status)), SpeedText.Status(""));
            }
        }

        private void ApplyReading(Reading r)
        {
            (SpeedText up, SpeedText down) = TextsFor(r);
            bool changed = !up.Equals(upText) || !down.Equals(downText) || r.Status != reading.Status
                           || (r.BlockerRequests > 0) != (reading.BlockerRequests > 0);
            if (r.BlockerRequests > 0 && reading.BlockerRequests == 0)
                Log.Info("The Blocker has connections waiting for approval");
            reading = r;
            upText = up;
            downText = down;
            if (changed)
                Render();
            UpdateToolTip();
            FollowNotificationArea();
            // Fallback for a taskbar that got above the bar without any event (docs/DESIGN.md §6.2): the reading
            // arrives once a second anyway, so this adds a few GetWindow calls and no timer.
            if (!dragging)
                RaiseIfCoveredByTaskbar("the once-a-second check");
        }

        private void UpdateToolTip()
        {
            var sb = new StringBuilder("BandBar ").Append(Program.Version);
            if (reading.ZoneName != null)
                sb.Append('\n').Append(Strings.Zone).Append(": ").Append(reading.ZoneName);
            sb.Append('\n').Append(Strings.Describe(reading.Status));
            if (reading.Status == LinkStatus.Connected && reading.FilteringOff)
                sb.Append('\n').Append(Strings.FilteringOff);
            if (reading.ServiceVersion != null)
                sb.Append('\n').Append("NetLimiter ").Append(reading.ServiceVersion);
            if (reading.Status != LinkStatus.Connected && reading.Detail != null)
                sb.Append('\n').Append(reading.Detail);
            if (Program.IsElevated)
                sb.Append('\n').Append(Strings.Elevated);
            if (reading.BlockerRequests > 0)
                sb.Append('\n').Append(Strings.BlockerWaiting(reading.BlockerRequests));
            sb.Append('\n').Append(Strings.DragHint);

            string text = sb.ToString();
            if (text != toolTipText)
            {
                toolTipText = text;
                toolTip.SetToolTip(this, text);
            }
        }
    }
}
