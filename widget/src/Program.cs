// Native desktop calendar. GDI+ draws the shared surface; Explorer forwards input.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("학사일정 위젯")]
[assembly: System.Reflection.AssemblyProduct("학사일정 위젯")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

namespace SchoolWidget
{
    static class Native
    {
        public const int GWLP_HWNDPARENT = -8;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_POPUP = 0x80000000L, WS_CHILD = 0x40000000L,
                          WS_CLIPSIBLINGS = 0x04000000L, WS_CLIPCHILDREN = 0x02000000L;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WM_NCHITTEST = 0x84;
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                         HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20, SWP_NOZORDER = 0x4;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        public delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr h);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT p, int n);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr rect, bool erase);
        [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint processId);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void EnablePixelCoordinates()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            SetProcessDPIAware();
        }

        // 바탕화면 아이콘 창(SysListView32)을 찾는다.
        // Progman 밑에 없으면 (배경 슬라이드쇼 등) WorkerW들을 차례로 확인한다.
        public static IntPtr FindDesktopListView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero)
            {
                IntPtr worker = IntPtr.Zero;
                while (true)
                {
                    worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                    if (worker == IntPtr.Zero) return IntPtr.Zero;
                    view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (view != IntPtr.Zero) break;
                }
            }
            return FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        // 배경화면 층(아이콘 뒤) WorkerW를 찾는다.
        // Progman에 0x052C를 보내면 explorer가 배경화면 층 WorkerW를 만든다 (문서화되지 않았지만 널리 쓰이는 방식).
        //  - Windows 11 24H2 이후: WorkerW가 Progman의 자식으로 생긴다.
        //  - 이전 버전: SHELLDLL_DefView를 가진 최상위 WorkerW 바로 다음 WorkerW.
        public static IntPtr FindWallpaperLayer()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            IntPtr res;
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out res);
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out res);

            IntPtr inner = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (inner != IntPtr.Zero) return inner;

            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                if (FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    found = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        public static Rectangle RectOf(IntPtr h)
        {
            RECT r; GetWindowRect(h, out r);
            return Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        }
    }

    // 기존 설정값 번호는 유지한다. 4는 Explorer 훅으로 아이콘 표면에 직접 그리는 모드.
    enum Mode { Desktop = 0, Topmost = 1, Normal = 2, Embed = 3, Dock = 4 }

    class Config
    {
        public int X = -1, Y = -1, W = 470, H = 860;
        public Mode Mode = Mode.Dock;
        public bool Locked = false;
        public int Opacity = 80;
        public int FontSize = 12, CellAlpha = 90;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolScheduler"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "widget.ini"); } }
        public static string WebDataDir { get { return Path.Combine(Dir, "WebView2"); } }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        int n; bool b;
                        switch (k)
                        {
                            case "x": if (int.TryParse(v, out n)) c.X = n; break;
                            case "y": if (int.TryParse(v, out n)) c.Y = n; break;
                            case "w": if (int.TryParse(v, out n)) c.W = n; break;
                            case "h": if (int.TryParse(v, out n)) c.H = n; break;
                            case "mode": if (int.TryParse(v, out n) && n >= 0 && n <= 4) c.Mode = (Mode)n; break;
                            case "locked": if (bool.TryParse(v, out b)) c.Locked = b; break;
                            case "opacity": if (int.TryParse(v, out n) && n >= 30 && n <= 100) c.Opacity = n; break;
                            case "fontSize": if (int.TryParse(v, out n) && n >= 9 && n <= 24) c.FontSize = n; break;
                            case "cellAlpha": if (int.TryParse(v, out n) && n >= 0 && n <= 240) c.CellAlpha = n; break;
                        }
                    }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, string.Format(
                    "x={0}\r\ny={1}\r\nw={2}\r\nh={3}\r\nmode={4}\r\nlocked={5}\r\nopacity={6}\r\nfontSize={7}\r\ncellAlpha={8}\r\n",
                    X, Y, W, H, (int)Mode, Locked, Opacity, FontSize, CellAlpha));
            }
            catch { }
        }
    }

    // 프로세스 전체에서 하나만 있는 숨은 최상위 창.
    // explorer 재시작 알림(TaskbarCreated)을 받는다. 자식으로 붙은 위젯 창은 이 알림을 못 받는다.
    class ShellWatcher : NativeWindow
    {
        readonly int taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        public event Action ExplorerRestarted;

        public ShellWatcher()
        {
            CreateHandle(new CreateParams { Caption = "SchoolWidgetShellWatcher", ExStyle = Native.WS_EX_TOOLWINDOW });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreated && ExplorerRestarted != null) ExplorerRestarted();
            base.WndProc(ref m);
        }
    }

    // x64 WH_CALLWNDPROC 훅: Explorer ListView 스레드에서 공유 프레임을 합성한다.
    // 훅 해제 시 Explorer는 원래 그리기로 돌아간다.
    sealed class DockBridge : IDisposable
    {
        const int MapBytes = 64 * 1024 * 1024, HeaderBytes = 256;
        const int HookType = 4, PageReadWrite = 4, FileMapWrite = 2;
        const int Magic = 0x4B434F44;
        readonly Mutex gate = new Mutex(false, @"Local\SchoolSchedulerDockMutex.v1");
        IntPtr module, hook, mapping, view, list;
        int refresh, sequence;
        int stopMessage;
        int hitTestMessage;
        int frameOpacity = 255;
        Size previousSize;
        int committedBytes;
        Rectangle previousBounds;
        int previousOpacity;
        bool shown = true;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attributes, int protection,
            int maxHigh, int maxLow, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr mapping, int access, int offHigh, int offLow, int bytes);
        [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protection);
        [DllImport("kernel32.dll", EntryPoint="RtlMoveMemory")]
        static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int kind, IntPtr callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);

        public bool Attached { get { return hook != IntPtr.Zero && Native.IsWindow(list) && Native.FindDesktopListView() == list; } }
        public string Error { get; private set; }

        public bool Start(IntPtr host)
        {
            if (Attached) return true;
            Stop();
            list = Native.FindDesktopListView();
            if (list == IntPtr.Zero) { Error = "바탕화면 아이콘 창을 찾을 수 없습니다."; return false; }
            string dll = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "SchoolDockHook.dll");
            if (!File.Exists(dll)) { Error = "SchoolDockHook.dll이 없습니다. build.cmd를 다시 실행하세요."; return false; }
            mapping = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PageReadWrite | 0x4000000 /* SEC_RESERVE */, 0, MapBytes,
                @"Local\SchoolSchedulerDockFrame.v4");
            if (mapping == IntPtr.Zero) { Error = "공유 메모리를 만들지 못했습니다."; Stop(); return false; }
            view = MapViewOfFile(mapping, FileMapWrite, 0, 0, MapBytes);
            if (view == IntPtr.Zero) { Error = "공유 메모리에 접근하지 못했습니다."; Stop(); return false; }
            if (!Commit(HeaderBytes)) { Error = "공유 메모리를 할당하지 못했습니다."; Stop(); return false; }
            Marshal.Copy(new byte[HeaderBytes], 0, view, HeaderBytes);
            module = LoadLibrary(dll);
            if (module == IntPtr.Zero) { Error = "Explorer 훅 DLL을 불러오지 못했습니다."; Stop(); return false; }
            IntPtr callback = GetProcAddress(module, "DockHook");
            uint pid;
            uint thread = Native.GetWindowThreadProcessId(list, out pid);
            if (callback == IntPtr.Zero || thread == 0) { Error = "Explorer 훅 진입점을 찾지 못했습니다."; Stop(); return false; }
            refresh = Native.RegisterWindowMessage("SchoolSchedulerDockRefresh.v1");
            stopMessage = Native.RegisterWindowMessage("SchoolSchedulerDockStop.v2");
            hitTestMessage = Native.RegisterWindowMessage("SchoolSchedulerDockHitTest.v1");
            Marshal.WriteInt64(view, 104, host.ToInt64());
            hook = SetWindowsHookEx(HookType, callback, module, thread);
            if (hook == IntPtr.Zero)
            {
                Error = "Explorer 훅 설치 실패 (Win32 " + Marshal.GetLastWin32Error() + ").";
                Stop(); return false;
            }
            Error = null;
            return true;
        }

        void WriteHeader(int x, int y, int dw, int dh, int w, int h, int opacity)
        {
            int[] values = { Magic, 2, ++sequence, x, y, dw, dh, w, h, w * 4, opacity };
            for (int i = 0; i < values.Length; i++) Marshal.WriteInt32(view, i * 4, values[i]);
            Marshal.WriteInt32(view, 80, unchecked((int)list.ToInt64()));
        }

        bool Commit(int bytes)
        {
            int page = Environment.SystemPageSize;
            int rounded = (bytes + page - 1) / page * page;
            if (rounded <= committedBytes) return true;
            if (VirtualAlloc(view, new UIntPtr((uint)rounded), 0x1000 /* MEM_COMMIT */, PageReadWrite) == IntPtr.Zero) return false;
            committedBytes = rounded;
            Marshal.WriteInt64(view, 160, committedBytes);
            return true;
        }
        void WriteDamage(Rectangle dirty)
        {
            Marshal.WriteInt32(view,128,dirty.X);Marshal.WriteInt32(view,132,dirty.Y);
            Marshal.WriteInt32(view,136,dirty.Width);Marshal.WriteInt32(view,140,dirty.Height);
        }
        public void Publish(Bitmap image, Rectangle bounds, int opacity, Rectangle? changed = null)
        {
            if (!Attached || view == IntPtr.Zero || image == null) return;
            int w = image.Width, h = image.Height;
            if (w < 1 || h < 1 || w > 4096 || h > 4096 || (long)w * h * 4 > MapBytes - HeaderBytes) return;
            var whole = new Rectangle(0,0,w,h);
            Rectangle dirty = previousSize != image.Size ? whole : Rectangle.Intersect(changed ?? whole, whole);
            if (dirty.IsEmpty && previousBounds == bounds && previousOpacity == opacity) return;
            bool locked = false;
            try
            {
                locked = gate.WaitOne(200);
                if (!locked) return;
                if (!Commit(HeaderBytes + w*h*4)) throw new OutOfMemoryException("달력 공유 메모리를 할당하지 못했습니다.");
                if (!dirty.IsEmpty)
                {
                    var data = image.LockBits(dirty, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try
                    {
                        IntPtr destination = IntPtr.Add(view,HeaderBytes+(dirty.Y*w+dirty.X)*4);
                        if (dirty.Width == w && data.Stride == w*4)
                            CopyMemory(destination,data.Scan0,new UIntPtr((uint)(dirty.Width*dirty.Height*4)));
                        else for(int row=0;row<dirty.Height;row++)
                            CopyMemory(IntPtr.Add(destination,row*w*4),IntPtr.Add(data.Scan0,row*data.Stride),new UIntPtr((uint)(dirty.Width*4)));
                    }
                    finally { image.UnlockBits(data); }
                    Marshal.WriteInt64(view,152,Marshal.ReadInt64(view,152)+(long)dirty.Width*dirty.Height*4);
                    int counter = dirty==whole ? 168 : 172;
                    Marshal.WriteInt32(view,counter,Marshal.ReadInt32(view,counter)+1);
                }
                WriteDamage(dirty);
                previousSize = image.Size;
                previousBounds = bounds;
                previousOpacity = opacity;
                frameOpacity = opacity;
                WriteHeader(bounds.X, bounds.Y, bounds.Width, bounds.Height, w, h, shown ? opacity : 0);
            }
            catch (AbandonedMutexException) { locked = true; }
            finally { if (locked) gate.ReleaseMutex(); }
            Notify();
        }

        public void SetVisible(bool visible)
        {
            shown = visible;
            if (view == IntPtr.Zero) return;
            bool locked = false;
            try
            {
                locked = gate.WaitOne(200);
                if (!locked) return;
                Marshal.WriteInt32(view, 8, ++sequence);
                Marshal.WriteInt32(view, 40, visible ? frameOpacity : 0);
                WriteDamage(Rectangle.Empty);
            }
            catch (AbandonedMutexException) { locked = true; }
            finally { if (locked) gate.ReleaseMutex(); }
            Notify();
        }

        public void SetInteraction(bool locked, uint cursor)
        {
            if (view == IntPtr.Zero) return;
            Marshal.WriteInt32(view, 72, locked ? 1 : 0);
            Marshal.WriteInt32(view, 84, unchecked((int)cursor));
        }

        // 동기 메시지로 Explorer 스레드에서 훅 설치와 갱신을 완료한다.
        void Notify()
        {
            if (Native.IsWindow(list) && refresh != 0)
            {
                IntPtr ignored;
                Marshal.WriteInt32(view, 112,
                    Native.SendMessageTimeout(list, refresh, IntPtr.Zero, IntPtr.Zero,
                        0x2 /* SMTO_ABORTIFHUNG */, 250, out ignored) != IntPtr.Zero ? 1 : -1);
            }
        }

        public bool IsIconAt(Native.POINT screen)
        {
            if (!Attached || view == IntPtr.Zero || hitTestMessage == 0) return false;
            Marshal.WriteInt32(view, 116, screen.X);
            Marshal.WriteInt32(view, 120, screen.Y);
            Marshal.WriteInt32(view, 124, -1);
            IntPtr ignored;
            if (Native.SendMessageTimeout(list, hitTestMessage, IntPtr.Zero, IntPtr.Zero,
                    0x2 /* SMTO_ABORTIFHUNG */, 100, out ignored) == IntPtr.Zero) return false;
            return Marshal.ReadInt32(view, 124) == 1;
        }

        public bool IsWidgetPixelAt(Native.POINT screen)
        {
            if (!Attached || view == IntPtr.Zero) return false;
            bool locked = false;
            try
            {
                locked = gate.WaitOne(0);
                if (!locked) return false;
                int x = Marshal.ReadInt32(view, 12), y = Marshal.ReadInt32(view, 16);
                int dw = Marshal.ReadInt32(view, 20), dh = Marshal.ReadInt32(view, 24);
                int w = Marshal.ReadInt32(view, 28), h = Marshal.ReadInt32(view, 32);
                if (dw < 1 || dh < 1 || w < 1 || h < 1 || w > 1600 || h > 1600 ||
                    screen.X < x || screen.Y < y || screen.X >= x + dw || screen.Y >= y + dh) return false;
                int sx = (screen.X - x) * w / dw, sy = (screen.Y - y) * h / dh;
                int alpha = Marshal.ReadByte(view, HeaderBytes + (sy * w + sx) * 4 + 3);
                return alpha * frameOpacity / 255 >= 32;
            }
            catch (AbandonedMutexException) { locked = true; return false; }
            finally { if (locked) gate.ReleaseMutex(); }
        }

        public void Stop()
        {
            if (hook != IntPtr.Zero)
            {
                SetVisible(false);
                if (Native.IsWindow(list) && stopMessage != 0)
                {
                    IntPtr ignored;
                    Native.SendMessageTimeout(list, stopMessage, IntPtr.Zero, IntPtr.Zero,
                        0x2 /* SMTO_ABORTIFHUNG */, 250, out ignored);
                }
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            if (view != IntPtr.Zero) { UnmapViewOfFile(view); view = IntPtr.Zero; }
            if (mapping != IntPtr.Zero) { CloseHandle(mapping); mapping = IntPtr.Zero; }
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
            list = IntPtr.Zero;
            shown = true;
            previousSize = Size.Empty;
            previousBounds = Rectangle.Empty;
            committedBytes = 0;
        }

        public void Dispose() { Stop(); gate.Dispose(); }
    }

    static class Program
    {
        static Config cfg;
        static CalendarStore store;
        static WidgetForm form;
        static ShellWatcher sink;
        static System.Windows.Forms.Timer watch;
        static EventWaitHandle quitSignal;
        static RegisteredWaitHandle quitRegistration;

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (new Mutex(true, "SchoolSchedulerWidget.single", out created))
            {
                if (!created)
                {
                    if (args.Length > 0 && args[0] == "--quit")
                    {
                        try { using (var signal = EventWaitHandle.OpenExisting(@"Local\SchoolSchedulerWidget.Quit")) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                    }
                    return;
                }
                if (args.Length > 0 && args[0] == "--quit") return;

                // explorer 창과 좌표 단위를 맞추기 위해 DPI 인식으로 실행
                Native.EnablePixelCoordinates();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                cfg = Config.Load();
                try { store = new CalendarStore(Path.Combine(Config.Dir, "calendar-data.json")); }
                catch (Exception ex)
                {
                    MessageBox.Show("저장된 데이터를 읽지 못했습니다. 원본 파일은 보존됩니다.\n" + ex.Message,
                        "학사일정 스케줄러", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                sink = new ShellWatcher();
                sink.ExplorerRestarted += () => Respawn(2000);

                // 바탕화면 속 모드에서 explorer가 배경 층을 다시 만들면 다시 붙인다
                watch = new System.Windows.Forms.Timer { Interval = 3000 };
                watch.Tick += (s, e) =>
                {
                    if (form == null || form.IsDisposed || !form.Visible) return;
                    if (cfg.Mode == Mode.Dock) form.ReattachDock();
                    else if (cfg.Mode == Mode.Embed)
                    {
                        if (form.IsEmbedded && !Native.IsWindow(Native.GetParent(form.Handle))) Respawn(0);
                        else if (!form.IsEmbedded && !form.IsPopped) form.Embed();
                    }
                };
                watch.Start();

                Spawn();
                quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SchoolSchedulerWidget.Quit");
                quitRegistration = ThreadPool.RegisterWaitForSingleObject(quitSignal, (state, timeout) =>
                {
                    var current = form;
                    if (current != null && current.IsHandleCreated && !current.IsDisposed)
                        try { current.BeginInvoke((Action)Quit); } catch (InvalidOperationException) { }
                }, null, -1, false);
                Application.Run();
                quitRegistration.Unregister(null);
                quitSignal.Dispose();
            }
        }

        static void Spawn()
        {
            form = new WidgetForm(cfg, store);
            var f = form;
            // 바탕화면 속 모드에서 explorer가 끝나면 부모 창과 함께 위젯 창도 없어진다 → 새로 만든다
            f.HandleDestroyed += (s, e) => { if (!f.Quitting && !f.RecreatingHandle) Respawn(2000); };
            f.Show();
        }

        static bool respawning;
        static void Respawn(int delay)
        {
            if (respawning) return;
            respawning = true;
            var t = new System.Windows.Forms.Timer { Interval = Math.Max(delay, 1) };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                if (Native.FindWindow("Progman", null) == IntPtr.Zero) { respawning = false; Respawn(1000); return; }
                var old = form;
                if (old != null)
                {
                    old.Quitting = true;
                    old.DisposeTray();
                    if (!old.IsDisposed) old.Dispose();
                }
                respawning = false;
                Spawn();
            };
            t.Start();
        }

        public static void Quit()
        {
            if (form != null)
            {
                form.Quitting = true;
                form.SaveBounds();
                form.DisposeTray();
            }
            Application.Exit();
        }
    }
}
