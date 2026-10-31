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

        static Dictionary<string, object> Screenshot(Dictionary<string, object> req)
        {
            string output = Str(req, "output", null);
            if (string.IsNullOrEmpty(output)) throw new ArgumentException("output path is required");
            output = Path.GetFullPath(output);
            Rectangle area;
            int monitorIndex = Int(req, "monitor", 0);
            if (monitorIndex > 0)
            {
                List<MonitorData> monitors = EnumMonitors();
                if (monitorIndex > monitors.Count)
                    throw new ArgumentException("monitor " + monitorIndex + " not found (have " + monitors.Count + ")");
                RECT b = monitors[monitorIndex - 1].Bounds;
                area = new Rectangle(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top);
            }
            else
            {
                area = VirtualScreen();
            }
            if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("no screen area to capture");

            string dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(area.X, area.Y, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
                }
                bmp.Save(output, ImageFormat.Png);
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["path"] = output;
            result["width"] = area.Width;
            result["height"] = area.Height;
            result["origin"] = Point(area.X, area.Y);
            result["monitor"] = monitorIndex > 0 ? (object)monitorIndex : null;
            Rectangle vs = VirtualScreen();
            result["virtualScreen"] = Rect(vs.X, vs.Y, vs.Width, vs.Height);
            return result;
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
                throw new InvalidOperationException("SendInput was blocked (error " + Marshal.GetLastWin32Error() +
                    "); is the target window elevated or the workstation locked?");
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
            MoveTo(Int(req, "x", 0), Int(req, "y", 0));
            return CursorResult();
        }

        static Dictionary<string, object> Click(Dictionary<string, object> req)
        {
            if (Has(req, "x")) MoveTo(Int(req, "x", 0), Int(req, "y", 0));
            uint down, up;
            ButtonFlags(Str(req, "button", "left"), out down, out up);
            int count = Math.Max(1, Int(req, "count", 1));
            for (int i = 0; i < count; i++)
            {
                if (i > 0) Thread.Sleep(40);
                Send(MouseInput(0, 0, 0, down), MouseInput(0, 0, 0, up));
            }
            Dictionary<string, object> result = CursorResult();
            result["button"] = Str(req, "button", "left");
            result["count"] = count;
            return result;
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
            if (Has(req, "x")) MoveTo(Int(req, "x", 0), Int(req, "y", 0));
            int dy = Int(req, "dy", 0), dx = Int(req, "dx", 0);
            if (dy != 0) Send(MouseInput(0, 0, unchecked((uint)(-dy * WHEEL_DELTA)), MOUSEEVENTF_WHEEL));
            if (dx != 0) Send(MouseInput(0, 0, unchecked((uint)(dx * WHEEL_DELTA)), MOUSEEVENTF_HWHEEL));
            Dictionary<string, object> result = CursorResult();
            result["dy"] = dy;
            result["dx"] = dx;
            return result;
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

        static Dictionary<string, object> TypeText(Dictionary<string, object> req)
        {
            string text = Str(req, "text", "");
            int delay = Math.Max(0, Int(req, "delay", 0));
            text = text.Replace("\r\n", "\n");
            foreach (char c in text)
            {
                if (c == '\n') TapVk(0x0D);
                else if (c == '\t') TapVk(0x09);
                else Send(KeyInput(0, c, KEYEVENTF_UNICODE), KeyInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                if (delay > 0) Thread.Sleep(delay);
            }
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["typed"] = text.Length;
            return result;
        }

        // combos: [[{vk, extended}, ...], ...]; each combo pressed in order
        // and released in reverse.
        static Dictionary<string, object> Key(Dictionary<string, object> req)
        {
            ArrayList combos = (ArrayList)Get(req, "combos");
            if (combos == null || combos.Count == 0) throw new ArgumentException("no key combos given");
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
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["combos"] = combos.Count;
            return result;
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
                TapVk(0x12);
                ok = TryForeground(w.Handle);
            }
            Dictionary<string, object> result = WindowDict(w, GetForegroundWindow());
            result["focused"] = ok;
            result["matches"] = matches.Count;
            if (!ok) throw new InvalidOperationException("window found but Windows refused to bring it to the foreground: " + w.Title);
            Dictionary<string, object> outer = new Dictionary<string, object>();
            outer["window"] = result;
            outer["focused"] = ok;
            outer["matches"] = matches.Count;
            return outer;
        }
    }
}
