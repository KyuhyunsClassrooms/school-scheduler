using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SchoolWidget
{
    sealed class WidgetForm : Form
    {
        readonly Config cfg;
        internal readonly CalendarStore Store;
        readonly DockBridge dock = new DockBridge();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Timer redraw = new Timer { Interval=16 }, midnight = new Timer { Interval=30000 };
        readonly Timer syncTimer = new Timer { Interval=300000 };
        internal readonly FeedSync Feed;
        int todoOffset;
        readonly int inputMessage = Native.RegisterWindowMessage("SchoolSchedulerDockInput.v1");
        Bitmap frame;
        internal CalendarLayout LayoutInfo { get; private set; }
        internal DateTime Month { get; private set; }
        internal DateTime Selected { get; private set; }
        internal MemoEditorForm MemoEditor { get; private set; }
        DetailsForm details;
        ContextMenuStrip dayMenu;
        int hover = -1, dragHit;
        Point dragOrigin;
        Rectangle dragBounds;
        DateTime today = DateTime.Today;
        bool dockActive, resourcesClosed, calendarVisible = true;
        ToolStripMenuItem lockItem, startupItem;
        public bool Quitting;
        public bool IsEmbedded { get { return false; } }
        public bool IsPopped { get { return false; } }
        public bool DockAttached { get { return dockActive && dock.Attached; } }

        public WidgetForm(Config config, CalendarStore store)
        {
            cfg=config; Store=store;
            Feed=new FeedSync(store);Feed.Changed+=QueueRender;
            syncTimer.Tick+=async(s,e)=>await Feed.RefreshAsync();syncTimer.Start();
            if(cfg.Mode==Mode.Embed)cfg.Mode=Mode.Dock;
            AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;Text="학사일정 스케줄러";
            BackColor=Color.FromArgb(27,35,44);DoubleBuffered=true;KeyPreview=true;
            MinimumSize=new Size(380,480);MaximumSize=new Size(4095,4095);
            Size=new Size(cfg.W,cfg.H);
            var area=Screen.PrimaryScreen.WorkingArea;
            Location=cfg.X<0||cfg.Y<0?new Point(area.Right-Width-24,area.Top+24):new Point(cfg.X,cfg.Y);
            bool onScreen=false;foreach(var screen in Screen.AllScreens)if(screen.WorkingArea.IntersectsWith(Bounds))onScreen=true;
            if(!onScreen)Location=new Point(area.Right-Width-24,area.Top+24);
            Selected=today;Month=new DateTime(today.Year,today.Month,1);
            redraw.Tick+=(s,e)=>{redraw.Stop();RenderNow();};
            midnight.Tick+=(s,e)=>{if(today!=DateTime.Today){today=DateTime.Today;QueueRender();}};midnight.Start();
            SizeChanged+=(s,e)=>{CloseMemo();QueueRender();};
            LocationChanged+=(s,e)=>{CloseMemo();QueueRender();};
            Store.Changed+=QueueRender;
            Load+=async(s,e)=>{ApplyMode();await Feed.RefreshAsync();};
            FormClosing+=(s,e)=>{SaveBounds();Quitting=true;DisposeTray();Application.ExitThread();};
            BuildMenu();
        }
        protected override CreateParams CreateParams { get {var p=base.CreateParams;p.ExStyle|=Native.WS_EX_TOOLWINDOW;return p;} }

        void QueueRender()
        {
            if(resourcesClosed||IsDisposed)return;
            redraw.Stop();redraw.Start();
        }
        internal void RenderNow()
        {
            if(resourcesClosed||IsDisposed||!IsHandleCreated||ClientSize.Width<1||ClientSize.Height<1)return;
            LayoutInfo=new CalendarLayout(ClientSize,Month);
            var todos=CalendarRenderer.VisibleTodos(Store);
            todoOffset=Math.Max(0,Math.Min(todoOffset,Math.Max(0,todos.Count-LayoutInfo.TodoCapacity)));
            var replacement=CalendarRenderer.Render(Store,cfg,LayoutInfo,Selected,hover,todoOffset,
                Feed.Name==null?"학교 일정 연결":Feed.Name+" · 연결 설정");
            var old=frame;frame=replacement;if(old!=null)old.Dispose();
            if(dockActive){dock.SetInteraction(cfg.Locked,32512);dock.Publish(frame,Native.RectOf(Handle),cfg.Opacity*255/100);}
            else Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(!dockActive&&frame!=null)e.Graphics.DrawImageUnscaled(frame,Point.Empty);
        }
        public void ApplyMode()
        {
            CloseMemo();
            if(dockActive){dock.Stop();dockActive=false;}
            SetClickThrough(false);TopMost=false;
            Native.SetWindowLongPtr(Handle,Native.GWLP_HWNDPARENT,
                cfg.Mode==Mode.Dock||cfg.Mode==Mode.Desktop?Native.FindDesktopListView():IntPtr.Zero);
            if(cfg.Mode==Mode.Dock&&dock.Start(Handle))
            {
                dockActive=true;Opacity=0;SetClickThrough(true);dock.SetVisible(calendarVisible);
            }
            else
            {
                Opacity=cfg.Opacity/100.0;TopMost=cfg.Mode==Mode.Topmost;
                if(cfg.Mode==Mode.Dock)tray.ShowBalloonTip(5000,"바탕화면 연결",dock.Error??"달력을 일반 창으로 표시합니다.",ToolTipIcon.Info);
            }
            RenderNow();
        }
        void SetClickThrough(bool on)
        {
            long ex=Native.GetWindowLongPtr(Handle,Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(Handle,Native.GWL_EXSTYLE,new IntPtr(on?ex|Native.WS_EX_TRANSPARENT:ex&~(long)Native.WS_EX_TRANSPARENT));
            Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,Native.SWP_NOACTIVATE|Native.SWP_NOZORDER|Native.SWP_FRAMECHANGED|1|2);
        }
        public void ReattachDock(){if(cfg.Mode==Mode.Dock&&!DockAttached)ApplyMode();}
        public bool Embed(){cfg.Mode=Mode.Dock;ApplyMode();return DockAttached;}
        internal void GoTo(DateTime date)
        {
            CloseMemo();Selected=date.Date;Month=new DateTime(date.Year,date.Month,1);QueueRender();
        }
        void MoveMonth(int count){GoTo(Month.AddMonths(count));}
        void ToggleVisible()
        {
            CloseMemo();calendarVisible=!calendarVisible;
            if(dockActive)dock.SetVisible(calendarVisible);
            else if(calendarVisible)Show();else Hide();
        }
        internal void OpenMemo(DateTime day)
        {
            CloseMemo();Selected=day.Date;RenderNow();
            int index=LayoutInfo.Index(day);if(index<0||index>=42)return;
            Rectangle cell=LayoutInfo.Cell(index);cell.Offset(Left,Top);
            var editor=new MemoEditorForm(day,Store.Memo(day),cell,cfg.FontSize,text=>Store.SetMemo(day,text));
            MemoEditor=editor;editor.FormClosed+=(s,e)=>{if(MemoEditor==editor)MemoEditor=null;QueueRender();};
            editor.Show(this);editor.Activate();
        }
        void CloseMemo(){if(MemoEditor!=null&&!MemoEditor.IsDisposed){var editor=MemoEditor;MemoEditor=null;editor.Close();}}
        void ShowDetails()
        {
            CloseMemo();
            if(details==null||details.IsDisposed)details=new DetailsForm(Store,Selected);
            else details.SelectDate(Selected);
            details.Show();details.Activate();
        }
        void ShowConnection(){CloseMemo();using(var dialog=new FeedConnectionDialog(Feed))dialog.ShowDialog(this);}
        bool HandleTodo(int kind,Point point,int wheel)
        {
            if(!LayoutInfo.TodoPanel.Contains(point))return false;
            if(kind==0x20A){todoOffset=Math.Max(0,todoOffset+(wheel>0?-1:1));QueueRender();return true;}
            if(kind!=0x201&&kind!=0x203)return true;
            if(kind==0x201)
            {
                if(LayoutInfo.Source.Contains(point)){ShowConnection();return true;}
                if(LayoutInfo.TodoAdd.Contains(point)){NativeDialogs.EditTodo(Store,Selected,null);return true;}
                string filter=LayoutInfo.TodoAll.Contains(point)?"all":LayoutInfo.TodoToday.Contains(point)?"today":LayoutInfo.TodoImportant.Contains(point)?"important":null;
                if(filter!=null){Store.Data.prefs.todoFilter=filter;todoOffset=0;NativeDialogs.Safe(Store.Save);return true;}
                if(LayoutInfo.TodoDone.Contains(point)){Store.Data.prefs.showDone=!Store.Data.prefs.showDone;NativeDialogs.Safe(Store.Save);return true;}
                if(LayoutInfo.TodoPrevious.Contains(point)){todoOffset=Math.Max(0,todoOffset-LayoutInfo.TodoCapacity);QueueRender();return true;}
                if(LayoutInfo.TodoNext.Contains(point)){todoOffset+=LayoutInfo.TodoCapacity;QueueRender();return true;}
            }
            int index=LayoutInfo.TodoAt(point);var rows=CalendarRenderer.VisibleTodos(Store);
            if(index>=0&&todoOffset+index<rows.Count)
            {
                var todo=rows[todoOffset+index];
                if(kind==0x201&&point.X<LayoutInfo.TodoRow(index).X+26)
                {
                    todo.done=!todo.done;todo.doneAt=todo.done?DateTime.Now.ToString("yyyy-MM-dd HH:mm"):null;NativeDialogs.Safe(Store.Save);
                }
                else if(kind==0x203&&point.X>=LayoutInfo.TodoRow(index).X+26)NativeDialogs.EditTodo(Store,Selected,todo);
            }
            return true;
        }
        void ShowContext(Point screen)
        {
            if(dayMenu!=null)dayMenu.Dispose();
            var context=new ContextMenuStrip();
            dayMenu=context;
            context.Items.Add(Selected.ToString("yyyy년 M월 d일")).Enabled=false;
            context.Items.Add("메모 편집",null,(s,e)=>OpenMemo(Selected));
            context.Items.Add("개인 일정 추가",null,(s,e)=>NativeDialogs.EditEvent(Store,Selected,null));
            context.Items.Add("할 일 추가",null,(s,e)=>NativeDialogs.EditTodo(Store,Selected,null));
            context.Items.Add("일정 · 할 일 목록",null,(s,e)=>ShowDetails());
            Native.SetForegroundWindow(Handle);context.Show(screen);
        }
        int BorderAt(Point p)
        {
            bool l=p.X<6,r=p.X>=Width-6,t=p.Y<6,b=p.Y>=Height-6;
            if(l&&t)return 13;if(r&&t)return 14;if(l&&b)return 16;if(r&&b)return 17;
            if(l)return 10;if(r)return 11;if(t)return 12;if(b)return 15;return 0;
        }
        internal void HandleMouse(int kind,Point screen,int keys,int data)
        {
            if(resourcesClosed||!calendarVisible||LayoutInfo==null)return;
            Point p=new Point(screen.X-Left,screen.Y-Top);
            if(dragHit!=0)
            {
                if(kind==0x200)DragTo(screen);
                if(kind==0x202||kind==0x2A3){dragHit=0;SaveBounds();}
                return;
            }
            if(kind==0x2A3){if(hover!=-1){hover=-1;QueueRender();}return;}
            int index=LayoutInfo.CellAt(p);
            if(kind==0x200)
            {
                if(hover!=index){hover=index;QueueRender();}
                uint cursor=LayoutInfo.Move.Contains(p)&&!cfg.Locked?32646u:
                    LayoutInfo.TodoPanel.Contains(p)||LayoutInfo.Prev.Contains(p)||LayoutInfo.Next.Contains(p)||LayoutInfo.Today.Contains(p)||LayoutInfo.Details.Contains(p)||LayoutInfo.Menu.Contains(p)?32649u:32512u;
                dock.SetInteraction(cfg.Locked,cursor);return;
            }
            if(kind==0x20A){if(!HandleTodo(kind,p,data))MoveMonth(data>0?-1:1);return;}
            if(kind==0x201)
            {
                CloseMemo();
                if(!cfg.Locked)
                {
                    dragHit=BorderAt(p);if(dragHit==0&&LayoutInfo.Move.Contains(p))dragHit=2;
                    if(dragHit!=0){dragOrigin=screen;dragBounds=Bounds;return;}
                }
                if(HandleTodo(kind,p,data))return;
                if(LayoutInfo.Prev.Contains(p)){MoveMonth(-1);return;}
                if(LayoutInfo.Next.Contains(p)){MoveMonth(1);return;}
                if(LayoutInfo.Today.Contains(p)){GoTo(DateTime.Today);return;}
                if(LayoutInfo.Details.Contains(p)){ShowDetails();return;}
                if(LayoutInfo.Menu.Contains(p)){menu.Show(screen);return;}
                if(index>=0)
                {
                    Selected=LayoutInfo.First.AddDays(index);QueueRender();
                    Native.SetForegroundWindow(Handle);SetFocus(Handle);
                }
            }
            else if(kind==0x203){if(!HandleTodo(kind,p,data)&&index>=0)OpenMemo(LayoutInfo.First.AddDays(index));}
            else if(kind==0x205)
            {
                if(index>=0)Selected=LayoutInfo.First.AddDays(index);
                QueueRender();ShowContext(screen);
            }
        }
        void DragTo(Point screen)
        {
            int dx=screen.X-dragOrigin.X,dy=screen.Y-dragOrigin.Y;Rectangle b=dragBounds;
            if(dragHit==2)b.Offset(dx,dy);
            else
            {
                int l=b.Left,r=b.Right,t=b.Top,bt=b.Bottom;
                if(dragHit==10||dragHit==13||dragHit==16)l=Math.Min(r-MinimumSize.Width,l+dx);
                if(dragHit==11||dragHit==14||dragHit==17)r=Math.Max(l+MinimumSize.Width,r+dx);
                if(dragHit==12||dragHit==13||dragHit==14)t=Math.Min(bt-MinimumSize.Height,t+dy);
                if(dragHit==15||dragHit==16||dragHit==17)bt=Math.Max(t+MinimumSize.Height,bt+dy);
                b=Rectangle.FromLTRB(l,t,r,bt);
            }
            Bounds=b;
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==inputMessage)
            {
                ulong packet=unchecked((ulong)m.WParam.ToInt64());long pos=m.LParam.ToInt64();
                HandleMouse((int)(packet&0xFFFF),new Point((short)pos,(short)(pos>>16)),(int)((packet>>16)&0xFFFF),(short)(packet>>32));return;
            }
            if(m.Msg==0x02E0){QueueRender();m.Result=IntPtr.Zero;return;}
            base.WndProc(ref m);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(dockActive)return;
            HandleMouse(e.Button==MouseButtons.Right?0x204:e.Clicks==2?0x203:0x201,PointToScreen(e.Location),0,0);
            if(dragHit!=0)Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dockActive)HandleMouse(0x200,PointToScreen(e.Location),0,0);}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(!dockActive){HandleMouse(e.Button==MouseButtons.Right?0x205:0x202,PointToScreen(e.Location),0,0);Capture=false;}}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(!dockActive)HandleMouse(0x20A,PointToScreen(e.Location),0,e.Delta);}
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int delta=e.KeyCode==Keys.Left?-1:e.KeyCode==Keys.Right?1:e.KeyCode==Keys.Up?-7:e.KeyCode==Keys.Down?7:0;
            if(delta!=0){GoTo(Selected.AddDays(delta));e.Handled=true;}
            else if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.F2){OpenMemo(Selected);e.Handled=true;}
            else if(e.KeyCode==Keys.PageUp)MoveMonth(-1);
            else if(e.KeyCode==Keys.PageDown)MoveMonth(1);
            else if(e.KeyCode==Keys.Home)GoTo(DateTime.Today);
        }
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr handle);

        void BuildMenu()
        {
            tray.Text="학사일정 스케줄러";tray.Icon=SystemIcons.Application;tray.Visible=true;tray.ContextMenuStrip=menu;
            tray.DoubleClick+=(s,e)=>ToggleVisible();
            menu.Items.Add("달력 보이기/숨기기",null,(s,e)=>ToggleVisible());
            menu.Items.Add("오늘로 이동",null,(s,e)=>GoTo(DateTime.Today));
            menu.Items.Add("일정 · 할 일 목록",null,(s,e)=>ShowDetails());
            menu.Items.Add("선택 날짜 메모",null,(s,e)=>OpenMemo(Selected));
            menu.Items.Add("개인 일정 추가",null,(s,e)=>NativeDialogs.EditEvent(Store,Selected,null));
            menu.Items.Add("할 일 추가",null,(s,e)=>NativeDialogs.EditTodo(Store,Selected,null));
            menu.Items.Add("학교 일정 서버 연결",null,(s,e)=>ShowConnection());
            menu.Items.Add("학교 일정 지금 새로고침",null,async(s,e)=>{await Feed.RefreshAsync();tray.ShowBalloonTip(4000,"학교 일정",Feed.Status,ToolTipIcon.Info);});
            menu.Items.Add(new ToolStripSeparator());
            lockItem=new ToolStripMenuItem("위치 잠금",null,(s,e)=>{cfg.Locked=!cfg.Locked;cfg.Save();QueueRender();});menu.Items.Add(lockItem);
            var modes=new ToolStripMenuItem("표시 방식");
            AddMode(modes,"바탕화면 (아이콘과 함께)",Mode.Dock);AddMode(modes,"바탕화면 위",Mode.Desktop);AddMode(modes,"항상 위",Mode.Topmost);AddMode(modes,"일반 창",Mode.Normal);menu.Items.Add(modes);
            menu.Items.Add("달력 설정",null,(s,e)=>NativeDialogs.Settings(cfg,Store,()=>{if(!dockActive)Opacity=cfg.Opacity/100.0;QueueRender();}));
            startupItem=new ToolStripMenuItem("Windows 시작 시 실행",null,(s,e)=>ToggleStartup());menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("백업 저장",null,(s,e)=>NativeDialogs.Export(Store));
            menu.Items.Add("백업 가져오기",null,(s,e)=>NativeDialogs.Import(Store));
            menu.Items.Add("종료",null,(s,e)=>Program.Quit());
            menu.Opening+=(s,e)=>{lockItem.Checked=cfg.Locked;startupItem.Checked=IsStartupOn();foreach(ToolStripMenuItem item in modes.DropDownItems)item.Checked=(Mode)item.Tag==cfg.Mode;};
        }
        void AddMode(ToolStripMenuItem parent,string text,Mode mode)
        {
            var item=new ToolStripMenuItem(text);item.Tag=mode;item.Click+=(s,e)=>{cfg.Mode=mode;cfg.Save();if(!Visible)Show();ApplyMode();};parent.DropDownItems.Add(item);
        }
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run",RunName="SchoolSchedulerWidget";
        bool IsStartupOn(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null&&key.GetValue(RunName)!=null;}
        void ToggleStartup(){NativeDialogs.Safe(()=>{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(IsStartupOn())key.DeleteValue(RunName,false);else key.SetValue(RunName,"\""+Application.ExecutablePath+"\"");}});}
        public void SaveBounds()
        {
            if(IsHandleCreated&&WindowState==FormWindowState.Normal){var b=Native.RectOf(Handle);cfg.X=b.X;cfg.Y=b.Y;cfg.W=b.Width;cfg.H=b.Height;}cfg.Save();
        }
        public void DisposeTray()
        {
            if(resourcesClosed)return;CloseMemo();resourcesClosed=true;redraw.Stop();midnight.Stop();syncTimer.Stop();redraw.Dispose();midnight.Dispose();syncTimer.Dispose();Feed.Changed-=QueueRender;Feed.Dispose();Store.Changed-=QueueRender;
            if(details!=null&&!details.IsDisposed)details.Close();dock.Dispose();tray.Visible=false;tray.Dispose();menu.Dispose();
            if(dayMenu!=null)dayMenu.Dispose();
            if(frame!=null){frame.Dispose();frame=null;}
        }
    }
}
