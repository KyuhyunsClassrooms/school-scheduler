using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SchoolWidget
{
    sealed class MemoEditorForm : Form
    {
        internal readonly TextBox Editor = new TextBox();
        readonly string original;
        readonly Action<string> save;
        readonly Font inputFont;
        bool commit = true, closing;
        public MemoEditorForm(DateTime day, string text, Rectangle bounds, int fontSize, Action<string> onSave)
        {
            original = text; save = onSave;
            Text = day.ToString("M월 d일 메모");
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; ControlBox = false;
            Bounds = bounds;
            BackColor = Color.FromArgb(255, 252, 226);
            var heading = new Label { Text = day.ToString("M/d") + " · Ctrl+Enter 저장", Dock=DockStyle.Top, Height=24, Padding=new Padding(3), ForeColor=Color.DimGray, AutoEllipsis=true };
            Editor.Multiline = true; Editor.AcceptsReturn = true; Editor.ScrollBars=ScrollBars.Vertical;
            Editor.BorderStyle=BorderStyle.None; Editor.Dock=DockStyle.Fill; Editor.BackColor=BackColor;
            inputFont=new Font("Malgun Gothic",fontSize,FontStyle.Regular,GraphicsUnit.Pixel);Editor.Font=inputFont;
            Editor.ImeMode=ImeMode.NoControl; Editor.Text=text; Editor.MaxLength=20000;
            Controls.Add(Editor); Controls.Add(heading); Padding=new Padding(5);
            Editor.KeyDown+=(s,e)=>{
                if(e.KeyCode==Keys.Escape){commit=false;e.SuppressKeyPress=true;Close();}
                else if(e.Control&&e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Close();}
            };
            Shown+=(s,e)=>{Editor.Focus();Editor.SelectionStart=Editor.TextLength;};
            Deactivate+=(s,e)=>{if(!closing&&IsHandleCreated)BeginInvoke((Action)(()=>{if(!IsDisposed&&!closing)Close();}));};
            FormClosing+=(s,e)=>{
                closing=true;
                if(commit&&Editor.Text!=original)try{save(Editor.Text);}catch(Exception ex){closing=false;e.Cancel=true;MessageBox.Show(this,ex.Message,"메모를 저장하지 못했습니다",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            };
        }
        protected override CreateParams CreateParams { get { var p=base.CreateParams;p.ExStyle|=Native.WS_EX_TOOLWINDOW;return p; } }
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&inputFont!=null)inputFont.Dispose();}
    }

    sealed class EventEditorDialog : Form
    {
        public PersonalEvent Value;
        public bool Deleted;
        public EventEditorDialog(DateTime day, PersonalEvent existing)
        {
            Text=existing==null?"개인 일정 추가":"개인 일정 수정";
            Font=new Font("Malgun Gothic",9f);ClientSize=new Size(420,328);FormBorderStyle=FormBorderStyle.FixedDialog;
            StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=false;MaximizeBox=false;MinimizeBox=false;
            var title=new TextBox {Left=80,Top=18,Width=320,MaxLength=300,Text=existing==null?"":existing.title};
            var start=new DateTimePicker {Left=80,Top=56,Width=150,Format=DateTimePickerFormat.Short,Value=existing==null?day:CalendarStore.Date(existing.start)};
            var end=new DateTimePicker {Left=250,Top=56,Width=150,Format=DateTimePickerFormat.Short,Value=existing==null?day:CalendarStore.Date(existing.end)};
            var note=new TextBox {Left=80,Top=96,Width=320,Height=165,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,Text=existing==null?"":existing.note};
            Controls.AddRange(new Control[]{new Label {Text="제목",Left=18,Top=21},new Label {Text="날짜",Left=18,Top=60},new Label {Text="메모",Left=18,Top=100},title,start,end,note});
            var save=new Button {Text="저장",Left=240,Top=281,Width=76};
            var cancel=new Button {Text="취소",Left=324,Top=281,Width=76,DialogResult=DialogResult.Cancel};
            var remove=new Button {Text="삭제",Left=18,Top=281,Width=76,Visible=existing!=null};
            save.Click+=(s,e)=>{
                if(string.IsNullOrWhiteSpace(title.Text)){title.Focus();return;}
                if(end.Value.Date<start.Value.Date){MessageBox.Show(this,"종료일은 시작일 이후로 선택하세요.");return;}
                Value=new PersonalEvent {id=existing==null?Guid.NewGuid().ToString("N"):existing.id,title=title.Text.Trim(),start=CalendarStore.DateKey(start.Value),end=CalendarStore.DateKey(end.Value),note=note.Text,
                    schoolYear=start.Value.Month<3?start.Value.Year-1:start.Value.Year};
                DialogResult=DialogResult.OK;
            };
            remove.Click+=(s,e)=>{if(MessageBox.Show(this,"이 개인 일정을 삭제할까요?","일정 삭제",MessageBoxButtons.YesNo)==DialogResult.Yes){Deleted=true;DialogResult=DialogResult.OK;}};
            Controls.AddRange(new Control[]{save,cancel,remove});AcceptButton=save;CancelButton=cancel;
        }
    }

    sealed class TodoEditorDialog : Form
    {
        public TodoItem Value;
        public bool Deleted;
        public TodoEditorDialog(DateTime day, TodoItem existing)
        {
            Text=existing==null?"할 일 추가":"할 일 수정";Font=new Font("Malgun Gothic",9f);
            ClientSize=new Size(420,310);FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=false;MaximizeBox=false;MinimizeBox=false;
            var title=new TextBox {Left=80,Top=18,Width=320,MaxLength=300,Text=existing==null?"":existing.title};
            var due=new DateTimePicker {Left=80,Top=56,Width=170,Format=DateTimePickerFormat.Short,ShowCheckBox=true,Value=existing!=null&&!string.IsNullOrEmpty(existing.due)?CalendarStore.Date(existing.due):day,Checked=existing==null||!string.IsNullOrEmpty(existing.due)};
            var important=new CheckBox {Left=270,Top=58,Width=120,Text="중요",Checked=existing!=null&&existing.important};
            var note=new TextBox {Left=80,Top=96,Width=320,Height=150,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,Text=existing==null?"":existing.note};
            Controls.AddRange(new Control[]{new Label {Text="할 일",Left=18,Top=21},new Label {Text="기한",Left=18,Top=60},new Label {Text="메모",Left=18,Top=100},title,due,important,note});
            var save=new Button {Text="저장",Left=240,Top=265,Width=76};
            var cancel=new Button {Text="취소",Left=324,Top=265,Width=76,DialogResult=DialogResult.Cancel};
            var remove=new Button {Text="삭제",Left=18,Top=265,Width=76,Visible=existing!=null};
            save.Click+=(s,e)=>{
                if(string.IsNullOrWhiteSpace(title.Text)){title.Focus();return;}
                Value=new TodoItem{id=existing==null?Guid.NewGuid().ToString("N"):existing.id,title=title.Text.Trim(),due=due.Checked?CalendarStore.DateKey(due.Value):null,
                    important=important.Checked,note=note.Text,done=existing!=null&&existing.done,doneAt=existing==null?null:existing.doneAt,created=existing==null?DateTime.Now.ToString("yyyy-MM-dd HH:mm"):existing.created};
                DialogResult=DialogResult.OK;
            };
            remove.Click+=(s,e)=>{if(MessageBox.Show(this,"이 할 일을 삭제할까요?","할 일 삭제",MessageBoxButtons.YesNo)==DialogResult.Yes){Deleted=true;DialogResult=DialogResult.OK;}};
            Controls.AddRange(new Control[]{save,cancel,remove});AcceptButton=save;CancelButton=cancel;
        }
    }

    static class NativeDialogs
    {
        public static void Safe(Action action)
        {
            try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"처리하지 못했습니다",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        }
        public static void EditEvent(CalendarStore store, DateTime day, PersonalEvent existing)
        {
            using(var dialog=new EventEditorDialog(day,existing))if(dialog.ShowDialog()==DialogResult.OK)Safe(()=>{
                if(existing!=null)store.Data.personal.RemoveAll(x=>x.id==existing.id);
                if(!dialog.Deleted)store.Data.personal.Add(dialog.Value);
                store.Save();
            });
        }
        public static void EditTodo(CalendarStore store,DateTime day,TodoItem existing)
        {
            using(var dialog=new TodoEditorDialog(day,existing))if(dialog.ShowDialog()==DialogResult.OK)Safe(()=>{
                if(existing!=null)store.Data.todos.RemoveAll(x=>x.id==existing.id);
                if(!dialog.Deleted)store.Data.todos.Add(dialog.Value);
                store.Save();
            });
        }
        public static void Export(CalendarStore store)
        {
            using(var dialog=new SaveFileDialog{Filter="JSON 백업|*.json",FileName="학사일정-백업-"+DateTime.Today.ToString("yyyyMMdd")+".json"})
                if(dialog.ShowDialog()==DialogResult.OK)Safe(()=>File.WriteAllText(dialog.FileName,store.Export(),new UTF8Encoding(false)));
        }
        public static void Import(CalendarStore store)
        {
            using(var dialog=new OpenFileDialog{Filter="JSON 백업|*.json"})
                if(dialog.ShowDialog()==DialogResult.OK)Safe(()=>{store.Import(File.ReadAllText(dialog.FileName,Encoding.UTF8));MessageBox.Show("백업을 현재 데이터에 합쳤습니다.","가져오기 완료");});
        }
        public static void Settings(Config cfg,CalendarStore store,Action apply)
        {
            using(var dialog=new Form{Text="달력 설정",ClientSize=new Size(360,280),Font=new Font("Malgun Gothic",9f),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=false,MaximizeBox=false,MinimizeBox=false})
            {
                var font=new NumericUpDown {Left=180,Top=22,Width=135,Minimum=9,Maximum=24,Value=cfg.FontSize};
                var alpha=new NumericUpDown {Left=180,Top=67,Width=135,Minimum=0,Maximum=240,Value=cfg.CellAlpha};
                var opacity=new NumericUpDown {Left=180,Top=112,Width=135,Minimum=30,Maximum=100,Value=cfg.Opacity};
                var grade=new ComboBox {Left=180,Top=157,Width=135,DropDownStyle=ComboBoxStyle.DropDownList};
                grade.Items.AddRange(new object[]{"전체 학년","1학년","2학년","3학년"});grade.SelectedIndex=store.Data.prefs.grade;
                var save=new Button{Left=180,Top=218,Width=65,Text="적용"};
                var cancel=new Button{Left=255,Top=218,Width=65,Text="취소",DialogResult=DialogResult.Cancel};
                dialog.Controls.AddRange(new Control[]{new Label{Text="글자 크기 (픽셀)",Left=24,Top=25,Width=150},font,new Label{Text="배경 진하기 (0~240)",Left=24,Top=70,Width=150},alpha,
                    new Label{Text="전체 불투명도 (%)",Left=24,Top=115,Width=150},opacity,new Label{Text="학사일정 학년 필터",Left=24,Top=160,Width=150},grade,save,cancel});
                save.Click+=(s,e)=>{cfg.FontSize=(int)font.Value;cfg.CellAlpha=(int)alpha.Value;cfg.Opacity=(int)opacity.Value;store.Data.prefs.grade=grade.SelectedIndex;cfg.Save();Safe(store.Save);apply();dialog.DialogResult=DialogResult.OK;};
                dialog.AcceptButton=save;dialog.CancelButton=cancel;dialog.ShowDialog();
            }
        }
    }

    sealed class DetailsForm : Form
    {
        readonly CalendarStore store;
        readonly DateTimePicker date=new DateTimePicker();
        readonly ListView events=new ListView(),todos=new ListView();
        readonly TabControl tabs=new TabControl();
        bool updating;
        public DetailsForm(CalendarStore data,DateTime selected)
        {
            store=data;Text="일정 · 할 일";Font=new Font("Malgun Gothic",9f);Size=new Size(640,480);StartPosition=FormStartPosition.CenterScreen;
            date.Format=DateTimePickerFormat.Long;date.Value=selected;date.Dock=DockStyle.Top;
            tabs.Dock=DockStyle.Fill;var eventTab=new TabPage("선택 날짜 일정");var todoTab=new TabPage("모든 할 일");tabs.TabPages.AddRange(new[]{eventTab,todoTab});
            SetupList(events,false);SetupList(todos,true);
            events.Columns.Add("분류",100);events.Columns.Add("제목",400);
            todos.Columns.Add("기한",110);todos.Columns.Add("할 일",390);
            eventTab.Controls.Add(events);todoTab.Controls.Add(todos);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,Padding=new Padding(5)};
            var addEvent=new Button{Text="개인 일정 추가",AutoSize=true};addEvent.Click+=(s,e)=>NativeDialogs.EditEvent(store,date.Value.Date,null);
            var addTodo=new Button{Text="할 일 추가",AutoSize=true};addTodo.Click+=(s,e)=>NativeDialogs.EditTodo(store,date.Value.Date,null);
            var edit=new Button{Text="선택 항목 열기",AutoSize=true};edit.Click+=(s,e)=>OpenSelection();
            bottom.Controls.AddRange(new Control[]{addEvent,addTodo,edit});
            Controls.Add(tabs);Controls.Add(bottom);Controls.Add(date);
            date.ValueChanged+=(s,e)=>RefreshLists();
            events.ItemActivate+=(s,e)=>OpenSelection();todos.ItemActivate+=(s,e)=>OpenSelection();
            todos.ItemChecked+=(s,e)=>{
                if(updating||!(e.Item.Tag is TodoItem))return;
                var item=(TodoItem)e.Item.Tag;item.done=e.Item.Checked;item.doneAt=item.done?DateTime.Now.ToString("yyyy-MM-dd HH:mm"):null;
                NativeDialogs.Safe(store.Save);
            };
            store.Changed+=RefreshLists;FormClosed+=(s,e)=>store.Changed-=RefreshLists;
            RefreshLists();
        }
        static void SetupList(ListView list,bool check)
        {
            list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.CheckBoxes=check;
        }
        public void SelectDate(DateTime day){date.Value=day.Date;RefreshLists();}
        void RefreshLists()
        {
            if(IsDisposed)return;updating=true;events.BeginUpdate();todos.BeginUpdate();
            try{
                events.Items.Clear();todos.Items.Clear();
                foreach(var entry in store.Entries(date.Value.Date)){
                    var item=new ListViewItem(new[]{entry.Category,entry.Title}){Tag=entry};events.Items.Add(item);
                }
                foreach(var todo in store.Data.todos.OrderBy(x=>x.done).ThenBy(x=>x.due??"9999")){
                    var item=new ListViewItem(new[]{todo.due??"기한 없음",(todo.important?"★ ":"")+todo.title}){Tag=todo,Checked=todo.done};todos.Items.Add(item);
                }
            }finally{events.EndUpdate();todos.EndUpdate();updating=false;}
        }
        void OpenSelection()
        {
            if(tabs.SelectedIndex==0&&events.SelectedItems.Count>0){
                var entry=(CalendarEntry)events.SelectedItems[0].Tag;
                if(entry.IsPersonal)NativeDialogs.EditEvent(store,entry.Start,entry.Personal);
                else MessageBox.Show(this,entry.Start.ToString("yyyy-MM-dd")+(entry.End==entry.Start?"":" ~ "+entry.End.ToString("yyyy-MM-dd"))+"\n"+entry.Category+" · "+entry.Grades+"\n\n"+entry.Note,entry.Title);
            }else if(tabs.SelectedIndex==1&&todos.SelectedItems.Count>0)
                NativeDialogs.EditTodo(store,date.Value.Date,(TodoItem)todos.SelectedItems[0].Tag);
        }
    }
}
