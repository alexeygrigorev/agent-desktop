// agent-desktop Windows helper. Compiled at runtime by Windows PowerShell 5.1
// (Add-Type), so this file must stay C# 5: no string interpolation, no "?.",
// no expression-bodied members, no "out var".
//
// Entry point: AgentDesktop.Helper.Run(requestJson) -> responseJson.
// All coordinates are physical virtual-screen pixels.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AgentDesktop
{
    public static class Helper
    {
        // ---- user32 / gdi32 / shcore / dwmapi -------------------------------

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; public uint dwFlags;
            public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags;
            public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }

        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] static extern uint GetClassLong32(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")] static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent, IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);
        [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint pid, out uint sessionId);
        [DllImport("kernel32.dll")] static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)] static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr memory);

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO
        {
            public int cbSize; public uint flags;
            public IntPtr hwndActive; public IntPtr hwndFocus; public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner; public IntPtr hwndMoveSize; public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        const uint PW_RENDERFULLCONTENT = 2;
        const int GWL_EXSTYLE = -20, GCL_STYLE = -26;
        const int WS_EX_TRANSPARENT = 0x20, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        const uint CS_DBLCLKS = 0x0008;
        const uint GW_CHILD = 5, GW_HWNDNEXT = 2;
        const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;
        const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;
        const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209;
        const uint WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
        const uint MK_LBUTTON = 0x0001, MK_RBUTTON = 0x0002, MK_MBUTTON = 0x0010;
        const uint DESKTOP_READOBJECTS = 0x0001;
        const int UOI_NAME = 2;
        const int SM_REMOTESESSION = 0x1000;
        const int WTSWinStationName = 6, WTSConnectState = 8;

        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
        const uint MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
        const uint MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
        const int WHEEL_DELTA = 120;
        const int SW_RESTORE = 9;
        const uint GW_OWNER = 4;
        const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        const uint MONITORINFOF_PRIMARY = 1;

        static string dpiAwareness = "unknown";
        static readonly JavaScriptSerializer Json = CreateSerializer();

        static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        // ---- entry point ----------------------------------------------------

        public static string Run(string requestJson)
        {
            Dictionary<string, object> result;
            try
            {
                EnsureDpiAwareness();
                Dictionary<string, object> req = (Dictionary<string, object>)Json.DeserializeObject(requestJson);
                string cmd = Str(req, "command", null);
                result = Dispatch(cmd, req);
                result["ok"] = true;
            }
            catch (Exception e)
            {
                result = new Dictionary<string, object>();
                result["ok"] = false;
                result["error"] = e.Message;
            }
            result["dpiAwareness"] = dpiAwareness;
            return Json.Serialize(result);
        }

        static Dictionary<string, object> Dispatch(string cmd, Dictionary<string, object> req)
        {
            switch (cmd)
            {
                case "screenshot": return Screenshot(req);
                case "click": return Click(req);
                case "move": return Move(req);
                case "drag": return Drag(req);
                case "scroll": return Scroll(req);
                case "type": return TypeText(req);
                case "key": return Key(req);
                case "windows": return Windows(req);
                case "focus": return Focus(req);
                case "monitors": return Monitors();
                case "cursor": return Cursor();
                case "doctor": return Doctor();
                default: throw new ArgumentException("unknown command: " + cmd);
            }
        }

        // Per-monitor v2 first so every API below sees physical pixels.
        static void EnsureDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) { dpiAwareness = "per-monitor-v2"; return; } } catch (EntryPointNotFoundException) { }
            try { if (SetThreadDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) { dpiAwareness = "per-monitor-v2 (thread)"; return; } } catch (EntryPointNotFoundException) { }
            try { if (SetProcessDpiAwareness(2) == 0) { dpiAwareness = "per-monitor"; return; } } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            if (SetProcessDPIAware()) dpiAwareness = "system";
        }

        // ---- request helpers ------------------------------------------------

        static object Get(Dictionary<string, object> req, string key)
        {
            object v;
            return req.TryGetValue(key, out v) ? v : null;
        }

        static string Str(Dictionary<string, object> req, string key, string dflt)
        {
            object v = Get(req, key);
            return v == null ? dflt : Convert.ToString(v);
        }

        static int Int(Dictionary<string, object> req, string key, int dflt)
        {
            object v = Get(req, key);
            return v == null ? dflt : Convert.ToInt32(v);
        }

        static bool Bool(Dictionary<string, object> req, string key)
        {
            object v = Get(req, key);
            return v != null && Convert.ToBoolean(v);
        }

        static bool Has(Dictionary<string, object> req, string key)
        {
            return Get(req, key) != null;
        }

        static Dictionary<string, object> Point(int x, int y)
        {
            Dictionary<string, object> p = new Dictionary<string, object>();
            p["x"] = x; p["y"] = y;
            return p;
        }

        static Dictionary<string, object> Rect(int x, int y, int w, int h)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["x"] = x; r["y"] = y; r["width"] = w; r["height"] = h;
            return r;
        }

        static Dictionary<string, object> Rect(RECT r)
        {
            return Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        // ---- screen geometry ------------------------------------------------

        static Rectangle VirtualScreen()
        {
            return new Rectangle(
                GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        }

        class MonitorData
        {
            public IntPtr Handle;
            public string Device;
            public bool Primary;
            public RECT Bounds;
            public RECT Work;
        }

        // Primary first, then left-to-right, top-to-bottom. Index is 1-based.
        static List<MonitorData> EnumMonitors()
        {
            List<MonitorData> list = new List<MonitorData>();
            MonitorEnumProc proc = delegate(IntPtr h, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFOEX info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(h, ref info))
                {
                    MonitorData m = new MonitorData();
                    m.Handle = h;
                    m.Device = info.szDevice;
                    m.Primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                    m.Bounds = info.rcMonitor;
                    m.Work = info.rcWork;
                    list.Add(m);
                }
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            list.Sort(delegate(MonitorData a, MonitorData b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                if (a.Bounds.Left != b.Bounds.Left) return a.Bounds.Left.CompareTo(b.Bounds.Left);
                return a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            return list;
        }

        static Dictionary<string, object> Monitors()
        {
            List<object> items = new List<object>();
            List<MonitorData> monitors = EnumMonitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorData m = monitors[i];
                Dictionary<string, object> d = Rect(m.Bounds);
                d["index"] = i + 1;
                d["device"] = m.Device;
                d["primary"] = m.Primary;
                d["workArea"] = Rect(m.Work);
                uint dx = 96, dy = 96;
                try { GetDpiForMonitor(m.Handle, 0, out dx, out dy); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
                d["dpi"] = (int)dx;
                d["scale"] = Math.Round(dx / 96.0, 2);
                items.Add(d);
            }
            Rectangle vs = VirtualScreen();
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["monitors"] = items;
            result["virtualScreen"] = Rect(vs.X, vs.Y, vs.Width, vs.Height);
            return result;
        }

        // ---- screenshot -----------------------------------------------------

        // method: "auto" (BitBlt of the screen, falling back to PrintWindow
        // compositing when the screen cannot be read, e.g. a minimized RDP
        // client), "screen" (BitBlt only) or "printwindow".
        // With a --window target, auto means PrintWindow of that window.
        static Dictionary<string, object> Screenshot(Dictionary<string, object> req)
        {
            string output = Str(req, "output", null);
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("output path is required");
            output = Path.GetFullPath(output);
            string method = Str(req, "method", "auto").ToLowerInvariant();
            if (method != "auto" && method != "screen" && method != "printwindow")
                throw new ArgumentException("unknown capture method: " + method);
            WindowData target = ResolveTarget(req);
            int monitorIndex = Int(req, "monitor", 0);
            List<object> warnings = new List<object>();
            string used;
            string screenError = null;
            int composited = 0;
            Rectangle area;
            Bitmap bmp = null;

            if (target != null)
            {
                if (method == "screen")
                {
                    RECT f = WindowBounds(target.Handle);
                    area = new Rectangle(f.Left, f.Top, f.Right - f.Left, f.Bottom - f.Top);
                    bmp = CaptureScreen(area);
                    used = "screen";
                }
                else
                {
                    bmp = CaptureWindow(target.Handle, out area);
                    used = "printwindow";
                }
            }
            else
            {
                area = monitorIndex > 0 ? MonitorArea(monitorIndex) : VirtualScreen();
                if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("no screen area to capture");
                used = "screen";
                if (method != "printwindow")
                {
                    try { bmp = CaptureScreen(area); }
                    catch (Exception e)
                    {
                        if (method == "screen") throw new InvalidOperationException("screen capture failed: " + e.Message +
                            " (no interactive display? run `agent-desktop doctor`)");
                        screenError = e.Message;
                    }
                }
                if (bmp == null)
                {
                    bmp = Composite(area, out composited);
                    used = "printwindow-composite";
                    warnings.Add("screen capture unavailable" + (screenError != null ? " (" + screenError + ")" : "") +
                        "; composited " + composited + " windows with PrintWindow. Menus, tooltips and the taskbar may be missing.");
                }
            }

            string dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            bool blank;
            using (bmp)
            {
                blank = IsBlank(bmp);
                bmp.Save(output, ImageFormat.Png);
            }
            if (blank) warnings.Add("image is a single flat colour; the desktop may not be rendering (run `agent-desktop doctor`)");

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["path"] = output;
            result["width"] = area.Width;
            result["height"] = area.Height;
            result["origin"] = Point(area.X, area.Y);
            result["monitor"] = monitorIndex > 0 ? (object)monitorIndex : null;
            result["method"] = used;
            if (target != null) result["window"] = WindowDict(target, GetForegroundWindow());
            if (screenError != null) result["screenError"] = screenError;
            if (composited > 0) result["composited"] = composited;
            result["blank"] = blank;
            result["warnings"] = warnings;
            Rectangle vs = VirtualScreen();
            result["virtualScreen"] = Rect(vs.X, vs.Y, vs.Width, vs.Height);
            return result;
        }

        static Rectangle MonitorArea(int monitorIndex)
        {
            List<MonitorData> monitors = EnumMonitors();
            if (monitorIndex > monitors.Count)
                throw new ArgumentException("monitor " + monitorIndex + " not found (have " + monitors.Count + ")");
            RECT b = monitors[monitorIndex - 1].Bounds;
            return new Rectangle(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
        }

        static Bitmap CaptureScreen(Rectangle area)
        {
            Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }

        // PrintWindow(PW_RENDERFULLCONTENT) works for windows that are covered
        // or not in the foreground, and when nothing is being displayed. The
        // image is cropped to the visible frame (DWM extended frame bounds);
        // `frame` receives its screen rectangle. This is the coordinate space
        // of --window input.
        static Bitmap CaptureWindow(IntPtr hWnd, out Rectangle frame)
        {
            if (IsIconic(hWnd)) throw new InvalidOperationException("window is minimized; restore it first (agent-desktop focus ...)");
            RECT wr;
            GetWindowRect(hWnd, out wr);
            int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
            if (w <= 0 || h <= 0) throw new InvalidOperationException("window has no area");
            Bitmap full = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(full))
            {
                IntPtr hdc = g.GetHdc();
                try { ok = PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }
            if (!ok)
            {
                full.Dispose();
                throw new InvalidOperationException("PrintWindow failed for window " + hWnd.ToInt64());
            }
            RECT fb = WindowBounds(hWnd);
            Rectangle crop = Rectangle.Intersect(
                new Rectangle(fb.Left - wr.Left, fb.Top - wr.Top, fb.Right - fb.Left, fb.Bottom - fb.Top),
                new Rectangle(0, 0, w, h));
            if (crop.Width <= 0 || crop.Height <= 0 || (crop.Width == w && crop.Height == h))
            {
                frame = new Rectangle(wr.Left, wr.Top, w, h);
                return full;
            }
            frame = new Rectangle(wr.Left + crop.X, wr.Top + crop.Y, crop.Width, crop.Height);
            Bitmap cropped = full.Clone(crop, PixelFormat.Format32bppRgb);
            full.Dispose();
            return cropped;
        }

        static List<IntPtr> TopLevelHandles()
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindowsProc proc = delegate(IntPtr h, IntPtr l) { list.Add(h); return true; };
            EnumWindows(proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return list;
        }

        // Paint every visible top-level window bottom-up onto a canvas of
        // `area`. Used when BitBlt of the screen fails.
        static Bitmap Composite(Rectangle area, out int count)
        {
            count = 0;
            Bitmap canvas = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Black);
                List<IntPtr> handles = TopLevelHandles();
                for (int i = handles.Count - 1; i >= 0; i--)
                {
                    IntPtr h = handles[i];
                    if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) continue;
                    int ex = GetWindowLong(h, GWL_EXSTYLE);
                    if ((ex & (WS_EX_TRANSPARENT | WS_EX_NOREDIRECTIONBITMAP)) != 0) continue;
                    RECT fb = WindowBounds(h);
                    Rectangle r = new Rectangle(fb.Left, fb.Top, fb.Right - fb.Left, fb.Bottom - fb.Top);
                    if (r.Width <= 0 || r.Height <= 0 || !r.IntersectsWith(area)) continue;
                    try
                    {
                        Rectangle frame;
                        using (Bitmap wb = CaptureWindow(h, out frame))
                        {
                            g.DrawImage(wb, new Rectangle(frame.X - area.X, frame.Y - area.Y, frame.Width, frame.Height));
                        }
                        count++;
                    }
                    catch (Exception) { }
                }
            }
            return canvas;
        }

        static bool IsBlank(Bitmap bmp)
        {
            int first = bmp.GetPixel(0, 0).ToArgb();
            for (int i = 0; i < 16; i++)
                for (int j = 0; j < 16; j++)
                {
                    int x = (int)((long)(bmp.Width - 1) * i / 15);
                    int y = (int)((long)(bmp.Height - 1) * j / 15);
                    if (bmp.GetPixel(x, y).ToArgb() != first) return false;
                }
            return true;
        }

        // ---- input mode and window targets ----------------------------------

        class SendInputBlocked : InvalidOperationException
        {
            public SendInputBlocked(string message) : base(message) { }
        }

        delegate Dictionary<string, object> InputAction(Dictionary<string, object> req, WindowData target);

        static WindowData ResolveTarget(Dictionary<string, object> req)
        {
            Dictionary<string, object> t = Get(req, "window") as Dictionary<string, object>;
            if (t == null) return null;
            List<WindowData> matches = FindWindows(t);
            if (matches.Count == 0) throw new ArgumentException("no visible window matches --window");
            return matches[0];
        }

        // Interactive = something has the foreground and we can open the input
        // desktop. A minimized RDP client or a lock screen fails this.
        static bool Interactive()
        {
            if (GetForegroundWindow() == IntPtr.Zero) return false;
            IntPtr d = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
            if (d == IntPtr.Zero) return false;
            CloseDesktop(d);
            return true;
        }

        // Runs `send` (SendInput) or `post` (PostMessage to the --window
        // target). input=auto picks post when there is no interactive display
        // and falls back to post when SendInput is blocked.
        static Dictionary<string, object> RunInput(Dictionary<string, object> req, InputAction send, InputAction post)
        {
            WindowData target = ResolveTarget(req);
            string mode = Str(req, "input", "auto").ToLowerInvariant();
            if (mode != "auto" && mode != "sendinput" && mode != "post")
                throw new ArgumentException("unknown input mode: " + mode);
            if (mode == "post" && target == null) throw new ArgumentException("--input post needs --window");
            string fallbackReason = null;
            if (mode == "sendinput" || (mode == "auto" && (target == null || Interactive())))
            {
                try
                {
                    Dictionary<string, object> r = send(req, target);
                    r["input"] = "sendinput";
                    if (target != null) r["window"] = WindowDict(target, GetForegroundWindow());
                    else if (!Interactive())
                        r["warnings"] = new List<object> { "no interactive display (no foreground window or input desktop): this input probably had no effect. Use --window <title|pid|hwnd> to post it to a window; see `agent-desktop doctor`." };
                    return r;
                }
                catch (SendInputBlocked e)
                {
                    if (mode != "auto" || target == null)
                        throw new InvalidOperationException(e.Message +
                            (target == null ? " Pass --window <title|pid|hwnd> to post input to a window instead, or run `agent-desktop doctor`." : ""));
                    fallbackReason = e.Message;
                }
            }
            else if (mode == "auto")
            {
                fallbackReason = "no interactive display (no foreground window or input desktop)";
            }
            Dictionary<string, object> res = post(req, target);
            res["input"] = "postmessage";
            res["window"] = WindowDict(target, GetForegroundWindow());
            if (fallbackReason != null) res["fallbackReason"] = fallbackReason;
            return res;
        }

        // --window coordinates are pixels of `screenshot --window`: relative to
        // the window's visible frame. Without --window they are screen pixels.
        static POINT ScreenPoint(Dictionary<string, object> req, WindowData target)
        {
            POINT p = new POINT();
            p.X = Int(req, "x", 0);
            p.Y = Int(req, "y", 0);
            if (target != null)
            {
                RECT f = WindowBounds(target.Handle);
                p.X += f.Left;
                p.Y += f.Top;
            }
            return p;
        }

        static POINT FrameCenter(WindowData target)
        {
            RECT f = WindowBounds(target.Handle);
            POINT p = new POINT();
            p.X = (f.Left + f.Right) / 2;
            p.Y = (f.Top + f.Bottom) / 2;
            return p;
        }

        static POINT TargetPoint(Dictionary<string, object> req, WindowData target)
        {
            return Has(req, "x") ? ScreenPoint(req, target) : FrameCenter(target);
        }

        static string ClassOf(IntPtr hWnd)
        {
            StringBuilder cls = new StringBuilder(256);
            GetClassName(hWnd, cls, cls.Capacity);
            return cls.ToString();
        }

        // Deepest visible child of `top` under the screen point, skipping
        // click-through overlays (e.g. Chrome's "Intermediate D3D Window"), so
        // Chrome input lands on Chrome_RenderWidgetHostHWND.
        static IntPtr DeepestChild(IntPtr top, POINT pt)
        {
            IntPtr cur = top;
            for (int depth = 0; depth < 32; depth++)
            {
                IntPtr next = IntPtr.Zero;
                for (IntPtr c = GetWindow(cur, GW_CHILD); c != IntPtr.Zero; c = GetWindow(c, GW_HWNDNEXT))
                {
                    if (!IsWindowVisible(c)) continue;
                    if ((GetWindowLong(c, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0) continue;
                    if (ClassOf(c) == "Intermediate D3D Window") continue;
                    RECT r;
                    GetWindowRect(c, out r);
                    if (pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom) { next = c; break; }
                }
                if (next == IntPtr.Zero) break;
                cur = next;
            }
            return cur;
        }

        static IntPtr MakeLParam(int lo, int hi)
        {
            return new IntPtr(unchecked((hi << 16) | (lo & 0xFFFF)));
        }

        static void Post(IntPtr hWnd, uint msg, uint wParam, IntPtr lParam)
        {
            if (!PostMessage(hWnd, msg, new IntPtr(unchecked((int)wParam)), lParam))
                throw new InvalidOperationException("PostMessage failed (error " + Marshal.GetLastWin32Error() + ")");
        }

        static Dictionary<string, object> PostTargetInfo(IntPtr child, POINT screen, POINT client)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["point"] = Point(screen.X, screen.Y);
            r["client"] = Point(client.X, client.Y);
            r["targetHwnd"] = child.ToInt64();
            r["targetClass"] = ClassOf(child);
            return r;
        }

        static bool HasDoubleClicks(IntPtr hWnd)
        {
            uint style = IntPtr.Size == 8 ? (uint)GetClassLongPtr64(hWnd, GCL_STYLE).ToInt64() : GetClassLong32(hWnd, GCL_STYLE);
            return (style & CS_DBLCLKS) != 0;
        }

        // ---- mouse ----------------------------------------------------------

        // Same formula as src/coords.js toAbsolute: exact round trip through
        // Windows' floor(n * width / 65536).
        static int Normalise(int offset, int size)
        {
            if (size <= 1) return 0;
            long v = (long)Math.Ceiling(offset * 65536.0 / size);
            return (int)Math.Max(0, Math.Min(65535, v));
        }

        static INPUT MouseInput(int dx, int dy, uint data, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_MOUSE;
            input.u.mi.dx = dx;
            input.u.mi.dy = dy;
            input.u.mi.mouseData = data;
            input.u.mi.dwFlags = flags;
            return input;
        }

        static void Send(params INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
                throw new SendInputBlocked("SendInput was blocked (error " + Marshal.GetLastWin32Error() +
                    "): the workstation is locked, there is no interactive display, or the target window is elevated.");
        }

        static void MoveTo(int x, int y)
        {
            Rectangle vs = VirtualScreen();
            int nx = Normalise(x - vs.X, vs.Width);
            int ny = Normalise(y - vs.Y, vs.Height);
            Send(MouseInput(nx, ny, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK));
            // Pin the exact pixel; SetCursorPos takes physical coordinates
            // in a per-monitor-aware process.
            SetCursorPos(x, y);
        }

        static void ButtonFlags(string button, out uint down, out uint up)
        {
            switch ((button ?? "left").ToLowerInvariant())
            {
                case "left": down = MOUSEEVENTF_LEFTDOWN; up = MOUSEEVENTF_LEFTUP; break;
                case "right": down = MOUSEEVENTF_RIGHTDOWN; up = MOUSEEVENTF_RIGHTUP; break;
                case "middle": down = MOUSEEVENTF_MIDDLEDOWN; up = MOUSEEVENTF_MIDDLEUP; break;
                default: throw new ArgumentException("unknown button: " + button);
            }
        }

        static void ButtonMessages(string button, out uint down, out uint up, out uint dbl, out uint mk)
        {
            switch ((button ?? "left").ToLowerInvariant())
            {
                case "left": down = WM_LBUTTONDOWN; up = WM_LBUTTONUP; dbl = WM_LBUTTONDBLCLK; mk = MK_LBUTTON; break;
                case "right": down = WM_RBUTTONDOWN; up = WM_RBUTTONUP; dbl = WM_RBUTTONDBLCLK; mk = MK_RBUTTON; break;
                case "middle": down = WM_MBUTTONDOWN; up = WM_MBUTTONUP; dbl = WM_MBUTTONDBLCLK; mk = MK_MBUTTON; break;
                default: throw new ArgumentException("unknown button: " + button);
            }
        }

        static Dictionary<string, object> CursorResult()
        {
            POINT p;
            GetCursorPos(out p);
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["cursor"] = Point(p.X, p.Y);
            return result;
        }

        static Dictionary<string, object> Cursor()
        {
            return CursorResult();
        }

        static Dictionary<string, object> Move(Dictionary<string, object> req)
        {
            return RunInput(req, MoveSend, MovePost);
        }

        static Dictionary<string, object> MoveSend(Dictionary<string, object> req, WindowData target)
        {
            POINT p = ScreenPoint(req, target);
            MoveTo(p.X, p.Y);
            return CursorResult();
        }

        static Dictionary<string, object> MovePost(Dictionary<string, object> req, WindowData target)
        {
            POINT screen = ScreenPoint(req, target);
            IntPtr child = DeepestChild(target.Handle, screen);
            POINT client = screen;
            ScreenToClient(child, ref client);
            Post(child, WM_MOUSEMOVE, 0, MakeLParam(client.X, client.Y));
            return PostTargetInfo(child, screen, client);
        }

        static Dictionary<string, object> Click(Dictionary<string, object> req)
        {
            Dictionary<string, object> result = RunInput(req, ClickSend, ClickPost);
            result["button"] = Str(req, "button", "left");
            result["count"] = Math.Max(1, Int(req, "count", 1));
            return result;
        }

        static Dictionary<string, object> ClickSend(Dictionary<string, object> req, WindowData target)
        {
            if (Has(req, "x"))
            {
                POINT p = ScreenPoint(req, target);
                MoveTo(p.X, p.Y);
            }
            uint down, up;
            ButtonFlags(Str(req, "button", "left"), out down, out up);
            int count = Math.Max(1, Int(req, "count", 1));
            for (int i = 0; i < count; i++)
            {
                if (i > 0) Thread.Sleep(40);
                Send(MouseInput(0, 0, 0, down), MouseInput(0, 0, 0, up));
            }
            return CursorResult();
        }

        static Dictionary<string, object> ClickPost(Dictionary<string, object> req, WindowData target)
        {
            POINT screen = TargetPoint(req, target);
            IntPtr child = DeepestChild(target.Handle, screen);
            POINT client = screen;
            ScreenToClient(child, ref client);
            IntPtr lp = MakeLParam(client.X, client.Y);
            uint down, up, dbl, mk;
            ButtonMessages(Str(req, "button", "left"), out down, out up, out dbl, out mk);
            bool dblClicks = HasDoubleClicks(child);
            int count = Math.Max(1, Int(req, "count", 1));
            Post(child, WM_MOUSEMOVE, 0, lp);
            Thread.Sleep(20);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) Thread.Sleep(40);
                Post(child, i > 0 && dblClicks ? dbl : down, mk, lp);
                Thread.Sleep(20);
                Post(child, up, 0, lp);
            }
            return PostTargetInfo(child, screen, client);
        }

        static Dictionary<string, object> Drag(Dictionary<string, object> req)
        {
            int x1 = Int(req, "x1", 0), y1 = Int(req, "y1", 0), x2 = Int(req, "x2", 0), y2 = Int(req, "y2", 0);
            int steps = Math.Max(1, Int(req, "steps", 10));
            int duration = Math.Max(0, Int(req, "duration", 200));
            uint down, up;
            ButtonFlags(Str(req, "button", "left"), out down, out up);
            MoveTo(x1, y1);
            Thread.Sleep(30);
            Send(MouseInput(0, 0, 0, down));
            try
            {
                for (int i = 1; i <= steps; i++)
                {
                    Thread.Sleep(duration / steps);
                    MoveTo(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
                }
                Thread.Sleep(30);
            }
            finally
            {
                Send(MouseInput(0, 0, 0, up));
            }
            return CursorResult();
        }

        // dy > 0 scrolls down (content moves up), like a browser deltaY;
        // dx > 0 scrolls right. Units are wheel notches.
        static Dictionary<string, object> Scroll(Dictionary<string, object> req)
        {
            Dictionary<string, object> result = RunInput(req, ScrollSend, ScrollPost);
            result["dy"] = Int(req, "dy", 0);
            result["dx"] = Int(req, "dx", 0);
            return result;
        }

        static Dictionary<string, object> ScrollSend(Dictionary<string, object> req, WindowData target)
        {
            if (Has(req, "x"))
            {
                POINT p = ScreenPoint(req, target);
                MoveTo(p.X, p.Y);
            }
            int dy = Int(req, "dy", 0), dx = Int(req, "dx", 0);
            if (dy != 0) Send(MouseInput(0, 0, unchecked((uint)(-dy * WHEEL_DELTA)), MOUSEEVENTF_WHEEL));
            if (dx != 0) Send(MouseInput(0, 0, unchecked((uint)(dx * WHEEL_DELTA)), MOUSEEVENTF_HWHEEL));
            return CursorResult();
        }

        static Dictionary<string, object> ScrollPost(Dictionary<string, object> req, WindowData target)
        {
            POINT screen = TargetPoint(req, target);
            IntPtr child = DeepestChild(target.Handle, screen);
            POINT client = screen;
            ScreenToClient(child, ref client);
            IntPtr lp = MakeLParam(screen.X, screen.Y); // wheel messages carry screen coordinates
            int dy = Int(req, "dy", 0), dx = Int(req, "dx", 0);
            if (dy != 0) Post(child, WM_MOUSEWHEEL, unchecked((uint)((-dy * WHEEL_DELTA) << 16)), lp);
            if (dx != 0) Post(child, WM_MOUSEHWHEEL, unchecked((uint)((dx * WHEEL_DELTA) << 16)), lp);
            return PostTargetInfo(child, screen, client);
        }

        // ---- keyboard -------------------------------------------------------

        static INPUT KeyInput(ushort vk, ushort scan, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = flags;
            return input;
        }

        static void TapVk(ushort vk)
        {
            Send(KeyInput(vk, 0, 0), KeyInput(vk, 0, KEYEVENTF_KEYUP));
        }

        // SendInput keyboard needs the target in the foreground.
        static void FocusForKeys(WindowData target)
        {
            if (target == null) return;
            if (IsIconic(target.Handle)) ShowWindow(target.Handle, SW_RESTORE);
            if (GetForegroundWindow() == target.Handle) return;
            if (TryForeground(target.Handle)) return;
            try { TapVk(0x12); } catch (SendInputBlocked) { }
            if (!TryForeground(target.Handle))
                throw new SendInputBlocked("could not bring the --window target to the foreground for keyboard input.");
        }

        // Window that receives posted keyboard messages: the deepest child at
        // --at if given, else the thread's focus window, else the deepest
        // child at the window centre.
        static IntPtr KeyTarget(Dictionary<string, object> req, WindowData target)
        {
            if (Has(req, "x")) return DeepestChild(target.Handle, ScreenPoint(req, target));
            uint ignored;
            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            if (GetGUIThreadInfo(GetWindowThreadProcessId(target.Handle, out ignored), ref info) &&
                info.hwndFocus != IntPtr.Zero &&
                (info.hwndFocus == target.Handle || IsChild(target.Handle, info.hwndFocus)))
                return info.hwndFocus;
            return DeepestChild(target.Handle, FrameCenter(target));
        }

        static Dictionary<string, object> KeyTargetInfo(IntPtr child)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["targetHwnd"] = child.ToInt64();
            r["targetClass"] = ClassOf(child);
            return r;
        }

        static IntPtr KeyLParam(ushort vk, bool extended, bool up, bool alt)
        {
            uint scan = MapVirtualKey(vk, 0) & 0xFF;
            uint lp = 1u | (scan << 16);
            if (extended) lp |= 1u << 24;
            if (alt) lp |= 1u << 29;
            if (up) lp |= (1u << 30) | (1u << 31);
            return new IntPtr(unchecked((int)lp));
        }

        static void PostKey(IntPtr hWnd, ushort vk, bool extended, bool up, bool alt)
        {
            uint msg = alt ? (up ? WM_SYSKEYUP : WM_SYSKEYDOWN) : (up ? WM_KEYUP : WM_KEYDOWN);
            Post(hWnd, msg, vk, KeyLParam(vk, extended, up, alt));
        }

        static Dictionary<string, object> TypeText(Dictionary<string, object> req)
        {
            Dictionary<string, object> result = RunInput(req, TypeSend, TypePost);
            result["typed"] = Str(req, "text", "").Replace("\r\n", "\n").Length;
            return result;
        }

        static Dictionary<string, object> TypeSend(Dictionary<string, object> req, WindowData target)
        {
            FocusForKeys(target);
            string text = Str(req, "text", "").Replace("\r\n", "\n");
            int delay = Math.Max(0, Int(req, "delay", 0));
            foreach (char c in text)
            {
                if (c == '\n') TapVk(0x0D);
                else if (c == '\t') TapVk(0x09);
                else Send(KeyInput(0, c, KEYEVENTF_UNICODE), KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                if (delay > 0) Thread.Sleep(delay);
            }
            return new Dictionary<string, object>();
        }

        static Dictionary<string, object> TypePost(Dictionary<string, object> req, WindowData target)
        {
            IntPtr child = KeyTarget(req, target);
            string text = Str(req, "text", "").Replace("\r\n", "\n");
            int delay = Math.Max(0, Int(req, "delay", 0));
            foreach (char c in text)
            {
                if (c == '\n' || c == '\t')
                {
                    ushort vk = c == '\n' ? (ushort)0x0D : (ushort)0x09;
                    PostKey(child, vk, false, false, false);
                    PostKey(child, vk, false, true, false);
                }
                else
                {
                    Post(child, WM_CHAR, c, new IntPtr(1));
                }
                Thread.Sleep(Math.Max(delay, 2));
            }
            return KeyTargetInfo(child);
        }

        // combos: [[{vk, extended}, ...], ...]; each combo pressed in order
        // and released in reverse.
        static Dictionary<string, object> Key(Dictionary<string, object> req)
        {
            ArrayList combos = Get(req, "combos") as ArrayList;
            if (combos == null || combos.Count == 0) throw new ArgumentException("no key combos given");
            Dictionary<string, object> result = RunInput(req, KeySend, KeyPost);
            result["combos"] = combos.Count;
            return result;
        }

        static Dictionary<string, object> KeySend(Dictionary<string, object> req, WindowData target)
        {
            FocusForKeys(target);
            ArrayList combos = (ArrayList)Get(req, "combos");
            int delay = Math.Max(0, Int(req, "delay", 30));
            for (int c = 0; c < combos.Count; c++)
            {
                if (c > 0 && delay > 0) Thread.Sleep(delay);
                ArrayList keys = (ArrayList)combos[c];
                List<INPUT> inputs = new List<INPUT>();
                for (int i = 0; i < keys.Count; i++)
                {
                    Dictionary<string, object> k = (Dictionary<string, object>)keys[i];
                    uint flags = Bool(k, "extended") ? KEYEVENTF_EXTENDEDKEY : 0;
                    inputs.Add(KeyInput((ushort)Int(k, "vk", 0), 0, flags));
                }
                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    Dictionary<string, object> k = (Dictionary<string, object>)keys[i];
                    uint flags = (Bool(k, "extended") ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP;
                    inputs.Add(KeyInput((ushort)Int(k, "vk", 0), 0, flags));
                }
                Send(inputs.ToArray());
            }
            return new Dictionary<string, object>();
        }

        static bool IsModifierVk(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x5B || vk == 0x5C;
        }

        // Posted key messages do not change the target's keyboard state, so
        // apps that read modifiers with GetKeyState may ignore ctrl/shift.
        static Dictionary<string, object> KeyPost(Dictionary<string, object> req, WindowData target)
        {
            IntPtr child = KeyTarget(req, target);
            ArrayList combos = (ArrayList)Get(req, "combos");
            int delay = Math.Max(0, Int(req, "delay", 30));
            bool modifiers = false;
            for (int c = 0; c < combos.Count; c++)
            {
                if (c > 0 && delay > 0) Thread.Sleep(delay);
                ArrayList keys = (ArrayList)combos[c];
                bool alt = false;
                foreach (object o in keys) if (Int((Dictionary<string, object>)o, "vk", 0) == 0x12) alt = true;
                if (keys.Count > 1) foreach (object o in keys) if (IsModifierVk(Int((Dictionary<string, object>)o, "vk", 0))) modifiers = true;
                for (int i = 0; i < keys.Count; i++)
                {
                    Dictionary<string, object> k = (Dictionary<string, object>)keys[i];
                    PostKey(child, (ushort)Int(k, "vk", 0), Bool(k, "extended"), false, alt);
                }
                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    Dictionary<string, object> k = (Dictionary<string, object>)keys[i];
                    PostKey(child, (ushort)Int(k, "vk", 0), Bool(k, "extended"), true, alt);
                }
            }
            Dictionary<string, object> r = KeyTargetInfo(child);
            if (modifiers)
                r["warnings"] = new List<object> { "posted modifier combos are best effort: apps that read modifier state with GetKeyState (Chrome shortcuts) may see only the plain key." };
            return r;
        }

        // ---- windows --------------------------------------------------------

        class WindowData
        {
            public IntPtr Handle;
            public string Title;
            public string ClassName;
            public uint Pid;
            public string Process;
            public RECT Bounds;
            public bool Minimized;
            public bool Maximized;
        }

        static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked = 0;
            try { if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, 4) != 0) return false; }
            catch (DllNotFoundException) { return false; }
            return cloaked != 0;
        }

        static RECT WindowBounds(IntPtr hWnd)
        {
            RECT r;
            try
            {
                if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                    return r;
            }
            catch (DllNotFoundException) { }
            GetWindowRect(hWnd, out r);
            return r;
        }

        // Visible, titled, uncloaked top-level windows in Z-order (topmost first).
        static List<WindowData> EnumTopWindows(bool includeAll)
        {
            List<WindowData> list = new List<WindowData>();
            Dictionary<uint, string> names = new Dictionary<uint, string>();
            EnumWindowsProc proc = delegate(IntPtr h, IntPtr l)
            {
                int len = GetWindowTextLength(h);
                if (!includeAll)
                {
                    if (!IsWindowVisible(h) || len == 0 || IsCloaked(h)) return true;
                }
                StringBuilder sb = new StringBuilder(len + 1);
                GetWindowText(h, sb, sb.Capacity);
                StringBuilder cls = new StringBuilder(256);
                GetClassName(h, cls, cls.Capacity);
                WindowData w = new WindowData();
                w.Handle = h;
                w.Title = sb.ToString();
                w.ClassName = cls.ToString();
                GetWindowThreadProcessId(h, out w.Pid);
                string name;
                if (!names.TryGetValue(w.Pid, out name))
                {
                    try { name = Process.GetProcessById((int)w.Pid).ProcessName; } catch (Exception) { name = null; }
                    names[w.Pid] = name;
                }
                w.Process = name;
                w.Bounds = WindowBounds(h);
                w.Minimized = IsIconic(h);
                w.Maximized = IsZoomed(h);
                list.Add(w);
                return true;
            };
            EnumWindows(proc, IntPtr.Zero);
            GC.KeepAlive(proc);
            return list;
        }

        static Dictionary<string, object> WindowDict(WindowData w, IntPtr foreground)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["hwnd"] = w.Handle.ToInt64();
            d["pid"] = (long)w.Pid;
            d["process"] = w.Process;
            d["title"] = w.Title;
            d["class"] = w.ClassName;
            d["rect"] = Rect(w.Bounds);
            d["minimized"] = w.Minimized;
            d["maximized"] = w.Maximized;
            d["foreground"] = w.Handle == foreground;
            return d;
        }

        static Dictionary<string, object> Windows(Dictionary<string, object> req)
        {
            IntPtr fg = GetForegroundWindow();
            List<object> items = new List<object>();
            foreach (WindowData w in EnumTopWindows(Bool(req, "all")))
                items.Add(WindowDict(w, fg));
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["windows"] = items;
            return result;
        }

        static List<WindowData> FindWindows(Dictionary<string, object> req)
        {
            List<WindowData> matches = new List<WindowData>();
            List<WindowData> all = EnumTopWindows(false);
            if (Has(req, "id"))
            {
                // A bare number from --window: a window handle if one matches,
                // otherwise a process id.
                long id = Convert.ToInt64(Get(req, "id"));
                foreach (WindowData w in all) if (w.Handle.ToInt64() == id) matches.Add(w);
                if (matches.Count > 0) return matches;
                Dictionary<string, object> byPid = new Dictionary<string, object>();
                byPid["pid"] = id;
                return FindWindows(byPid);
            }
            if (Has(req, "hwnd"))
            {
                long hwnd = Convert.ToInt64(Get(req, "hwnd"));
                foreach (WindowData w in all) if (w.Handle.ToInt64() == hwnd) matches.Add(w);
                return matches;
            }
            if (Has(req, "pid"))
            {
                long pid = Convert.ToInt64(Get(req, "pid"));
                // Prefer unowned (main) windows of the process.
                foreach (WindowData w in all) if (w.Pid == pid && GetWindow(w.Handle, GW_OWNER) == IntPtr.Zero) matches.Add(w);
                foreach (WindowData w in all) if (w.Pid == pid && !matches.Contains(w)) matches.Add(w);
                return matches;
            }
            string title = Str(req, "title", "");
            foreach (WindowData w in all)
                if (w.Title.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(w);
            return matches;
        }

        static bool TryForeground(IntPtr hWnd)
        {
            uint ignored;
            uint self = GetCurrentThreadId();
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint target = GetWindowThreadProcessId(hWnd, out ignored);
            bool attachedFg = fgThread != 0 && fgThread != self && AttachThreadInput(self, fgThread, true);
            bool attachedTarget = target != 0 && target != self && target != fgThread && AttachThreadInput(self, target, true);
            try
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attachedTarget) AttachThreadInput(self, target, false);
                if (attachedFg) AttachThreadInput(self, fgThread, false);
            }
            Thread.Sleep(50);
            return GetForegroundWindow() == hWnd;
        }

        static Dictionary<string, object> Focus(Dictionary<string, object> req)
        {
            List<WindowData> matches = FindWindows(req);
            if (matches.Count == 0) throw new ArgumentException("no visible window matches");
            WindowData w = matches[0];
            if (IsIconic(w.Handle)) ShowWindow(w.Handle, SW_RESTORE);
            bool ok = TryForeground(w.Handle);
            if (!ok)
            {
                // Foreground lock: a process may only take the foreground if it
                // sent the last input event. A bare Alt tap satisfies that.
                try { TapVk(0x12); } catch (SendInputBlocked) { }
                ok = TryForeground(w.Handle);
            }
            Dictionary<string, object> result = WindowDict(w, GetForegroundWindow());
            result["focused"] = ok;
            result["matches"] = matches.Count;
            if (!ok) throw new InvalidOperationException("window found (and restored) but Windows refused to bring it to the foreground: " + w.Title +
                (Interactive() ? "" : ". There is no interactive display; use --window with screenshot/click/type/key instead, and run `agent-desktop doctor`."));
            Dictionary<string, object> outer = new Dictionary<string, object>();
            outer["window"] = result;
            outer["focused"] = ok;
            outer["matches"] = matches.Count;
            return outer;
        }

        // ---- doctor ---------------------------------------------------------

        static readonly string[] ConnectStates = { "active", "connected", "connectquery", "shadow", "disconnected", "idle", "listen", "reset", "down", "init" };

        static object WtsInfo(int infoClass, bool asString)
        {
            IntPtr buf;
            int bytes;
            try
            {
                if (!WTSQuerySessionInformation(IntPtr.Zero, -1, infoClass, out buf, out bytes)) return null;
            }
            catch (DllNotFoundException) { return null; }
            try
            {
                if (asString) return Marshal.PtrToStringUni(buf);
                int v = Marshal.ReadInt32(buf);
                return v >= 0 && v < ConnectStates.Length ? ConnectStates[v] : v.ToString();
            }
            finally { WTSFreeMemory(buf); }
        }

        static Dictionary<string, object> Check(bool ok, string error)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ok"] = ok;
            if (error != null) d["error"] = error;
            return d;
        }

        // Raw facts about the session; src/doctor.js turns them into hints.
        static Dictionary<string, object> Doctor()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            uint session;
            r["sessionId"] = ProcessIdToSessionId((uint)Process.GetCurrentProcess().Id, out session) ? (object)(long)session : null;
            r["consoleSessionId"] = (long)WTSGetActiveConsoleSessionId();
            r["sessionName"] = WtsInfo(WTSWinStationName, true);
            r["connectState"] = WtsInfo(WTSConnectState, false);
            r["remote"] = GetSystemMetrics(SM_REMOTESESSION) != 0;

            IntPtr fg = GetForegroundWindow();
            r["foregroundHwnd"] = fg.ToInt64();
            if (fg != IntPtr.Zero)
            {
                StringBuilder sb = new StringBuilder(512);
                GetWindowText(fg, sb, sb.Capacity);
                r["foregroundTitle"] = sb.ToString();
            }

            IntPtr desk = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
            if (desk == IntPtr.Zero)
            {
                r["inputDesktop"] = null;
                r["inputDesktopError"] = "OpenInputDesktop failed (error " + Marshal.GetLastWin32Error() + ")";
            }
            else
            {
                StringBuilder name = new StringBuilder(256);
                int needed;
                GetUserObjectInformation(desk, UOI_NAME, name, name.Capacity * 2, out needed);
                r["inputDesktop"] = name.ToString();
                CloseDesktop(desk);
            }

            Rectangle vs = VirtualScreen();
            r["virtualScreen"] = Rect(vs.X, vs.Y, vs.Width, vs.Height);

            Dictionary<string, object> capture = new Dictionary<string, object>();
            try
            {
                using (Bitmap b = CaptureScreen(new Rectangle(vs.X, vs.Y, Math.Min(64, vs.Width), Math.Min(64, vs.Height)))) { }
                capture["screen"] = Check(true, null);
            }
            catch (Exception e) { capture["screen"] = Check(false, e.Message); }

            Dictionary<string, object> pw = Check(false, "no visible, non-minimized window to test with");
            foreach (WindowData w in EnumTopWindows(false))
            {
                if (w.Minimized) continue;
                try
                {
                    Rectangle frame;
                    using (Bitmap b = CaptureWindow(w.Handle, out frame))
                    {
                        pw = Check(true, null);
                        pw["blank"] = IsBlank(b);
                    }
                }
                catch (Exception e) { pw = Check(false, e.Message); }
                pw["hwnd"] = w.Handle.ToInt64();
                pw["title"] = w.Title;
                break;
            }
            capture["printWindow"] = pw;
            r["capture"] = capture;

            try
            {
                // A zero-length relative mouse move: harmless, but tells us
                // whether SendInput is accepted.
                Send(MouseInput(0, 0, 0, MOUSEEVENTF_MOVE));
                r["sendInput"] = Check(true, null);
            }
            catch (SendInputBlocked e) { r["sendInput"] = Check(false, e.Message); }
            r["interactive"] = Interactive();
            return r;
        }
    }
}
