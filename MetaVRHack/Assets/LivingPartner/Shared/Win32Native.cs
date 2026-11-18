using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LivingPartner.Shared
{
    /// <summary>
    /// Win32 interop shared by the Companion Host and the Windows character client (only ever called on Windows).
    /// Everything is in PHYSICAL pixels: callers must make the process/thread per-monitor DPI aware first.
    /// No code is injected into other programs: these are read-only queries plus control of our own windows.
    /// </summary>
    public static class Win32
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; public RectI ToRectI() => new RectI(left, top, right - left, bottom - top); }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; public POINT(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public int colors; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        public static readonly IntPtr DPI_PER_MONITOR_V2 = new IntPtr(-4);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2), HWND_TOP = IntPtr.Zero;
        public const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_CAPTION = 0x00C00000, WS_MINIMIZE = 0x20000000;
        public const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_APPWINDOW = 0x40000;
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400, SWP_ASYNCWINDOWPOS = 0x4000;
        public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_SHOWNA = 8, SW_MINIMIZE = 6, SW_RESTORE = 9;
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const uint GA_ROOT = 2, GA_ROOTOWNER = 3, GW_HWNDNEXT = 2, GW_HWNDPREV = 3, GW_OWNER = 4;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        public const uint ULW_ALPHA = 2;

        public static void EnsureDpiAware()
        {
            try { SetProcessDpiAwarenessContext(DPI_PER_MONITOR_V2); } catch { }
            try { SetThreadDpiAwarenessContext(DPI_PER_MONITOR_V2); } catch { }
        }

        /// <summary>Enumerates displays in physical pixels with DPI scale, rotation and EDID physical size.</summary>
        public static List<MonitorDesc> EnumerateMonitors()
        {
            var list = new List<MonitorDesc>();
            MonitorEnumProc cb = (hMon, hdc, rc, data) =>
            {
                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (!GetMonitorInfo(hMon, ref mi)) return true;
                var m = new MonitorDesc { id = mi.szDevice, bounds = mi.rcMonitor.ToRectI(), work = mi.rcWork.ToRectI(), primary = (mi.dwFlags & 1) != 0 };
                try { if (GetDpiForMonitor(hMon, 0, out uint dx, out uint _) == 0) m.dpiScale = dx / 96f; } catch { }
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                if (EnumDisplaySettings(mi.szDevice, -1, ref dm)) { m.rotation = dm.dmDisplayOrientation * 90; m.refreshHz = dm.dmDisplayFrequency; }
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (EnumDisplayDevices(mi.szDevice, 0, ref dd, 1)) { m.stableId = dd.DeviceID ?? ""; m.label = dd.DeviceString ?? ""; }
                try
                {
                    IntPtr dc = CreateDC("DISPLAY", mi.szDevice, null, IntPtr.Zero);
                    if (dc != IntPtr.Zero)
                    {
                        float wmm = GetDeviceCaps(dc, 4), hmm = GetDeviceCaps(dc, 6);
                        DeleteDC(dc);
                        if (wmm > 10 && hmm > 10)
                        {
                            bool landscapePx = m.bounds.w >= m.bounds.h, landscapeMm = wmm >= hmm;
                            if (landscapePx != landscapeMm) { float t = wmm; wmm = hmm; hmm = t; }
                            m.physWidthM = wmm / 1000f; m.physHeightM = hmm / 1000f;
                        }
                    }
                }
                catch { }
                list.Add(m);
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            for (int i = 0; i < list.Count; i++) list[i].index = i;
            return list;
        }

        public static string WindowTitle(IntPtr hwnd) { var sb = new StringBuilder(512); GetWindowText(hwnd, sb, sb.Capacity); return sb.ToString(); }
        public static string WindowClass(IntPtr hwnd) { var sb = new StringBuilder(256); GetClassName(hwnd, sb, sb.Capacity); return sb.ToString(); }

        /// <summary>Visible frame of a top-level window (without the invisible resize border), physical pixels.</summary>
        public static bool FrameBounds(IntPtr hwnd, out RectI rect)
        {
            rect = default;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf(typeof(RECT))) == 0 && r.right > r.left) { rect = r.ToRectI(); return true; }
            if (GetWindowRect(hwnd, out r)) { rect = r.ToRectI(); return true; }
            return false;
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        /// <summary>True when the interactive input desktop cannot be opened (UAC secure desktop / lock screen): we pause instead of bypassing.</summary>
        public static bool IsSecureDesktopActive()
        {
            IntPtr h = OpenInputDesktop(0, false, 0x0100 /* DESKTOP_SWITCHDESKTOP */);
            if (h == IntPtr.Zero) return true;
            CloseDesktop(h);
            return false;
        }

        /// <summary>Is this an ordinary application window the partner may use as a workspace (not the desktop, taskbar or our own windows)?</summary>
        public static bool IsWorkspaceCandidate(IntPtr hwnd, uint ownPid)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsCloaked(hwnd)) return false;
            if (hwnd == GetShellWindow()) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return false;
            string cls = WindowClass(hwnd);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "LivingPartnerOverlay" ||
                cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow" || cls == "TopLevelWindowForOverflowXamlIsland") return false;
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
            return true;
        }

        /// <summary>Top-level workspace window under a physical pixel, skipping our own overlay windows.</summary>
        public static IntPtr WorkspaceWindowAt(int px, int py, uint ownPid)
        {
            // Walk the z-order instead of WindowFromPoint so that our click-through overlay never hides the real target.
            for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) continue;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == ownPid) continue;
                long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
                if ((ex & WS_EX_TRANSPARENT) != 0 && (ex & WS_EX_LAYERED) != 0) continue; // other click-through overlays
                if (!FrameBounds(h, out RectI r) || !r.Contains(px, py)) continue;
                return IsWorkspaceCandidate(h, ownPid) ? h : IntPtr.Zero; // first real window under the point decides
            }
            return IntPtr.Zero;
        }
    }
}
