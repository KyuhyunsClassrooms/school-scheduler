using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SchoolWidget
{
    static class NativeSmoke
    {
        static readonly string Root=Path.Combine(Environment.CurrentDirectory,"analysis","native-smoke");
        static WidgetForm form;
        static void Log(string text){File.AppendAllText(Path.Combine(Root,"result.txt"),text+Environment.NewLine);}
        static void Check(bool value,string text){if(!value)throw new Exception(text);Log("PASS "+text);}
        static void Mouse(int msg,Point client,int keys)
        {
            var p=new Native.POINT{X=form.Left+client.X,Y=form.Top+client.Y};
            IntPtr list=Native.FindDesktopListView();
            if(msg!=0x20A)Native.ScreenToClient(list,ref p);
            IntPtr result;Native.SendMessageTimeout(list,msg,new IntPtr(keys),new IntPtr((p.Y<<16)|(p.X&0xffff)),2,1000,out result);
        }
        static Point Center(Rectangle rect){return new Point(rect.X+rect.Width/2,rect.Y+rect.Height/2);}
        static byte[] Pixels(Bitmap image)
        {
            var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{var data=new byte[image.Width*image.Height*4];Marshal.Copy(bits.Scan0,data,0,data.Length);return data;}
            finally{image.UnlockBits(bits);}
        }
        static Bitmap CurrentFrame(){return (Bitmap)typeof(WidgetForm).GetField("frame",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);}
        static async Task Click(Point point)
        {
            Mouse(0x200,point,0);Mouse(0x201,point,1);await Task.Delay(40);Mouse(0x202,point,0);await Task.Delay(90);
        }
        [STAThread]static void Main()
        {
            Directory.CreateDirectory(Root);File.WriteAllText(Path.Combine(Root,"result.txt"),"Native desktop calendar integration\r\n");
            Native.EnablePixelCoordinates();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var cfg=Config.Load();int initialFontSize=cfg.FontSize;var store=new CalendarStore(Path.Combine(Root,"fixture-data.json"));
            store.Data.personal.Clear();store.Data.todos.Clear();store.Data.memos.Clear();
            store.Data.personal.Add(new PersonalEvent{id="smoke-personal",title="개인 일정",start="2026-10-24",end="2026-10-26",note="줄1\n줄2"});
            store.Data.todos.Add(new TodoItem{id="smoke-todo",title="할 일",due="2026-10-24",important=true});
            store.Save();
            form=new WidgetForm(cfg,store);Rectangle initial=Rectangle.Empty;
            form.Shown+=async(s,e)=>{
                try{
                    await Task.Delay(350);initial=Native.RectOf(form.Handle);
                    Check(store.School.Count==127,"127 embedded school events loaded without HTML");
                    Check(form.LayoutInfo.TodoPanel.Width>200,"todo management is always visible inside the native calendar");
                    store.Data.todos.Add(new TodoItem{id="undated",title="기한 없는 할 일"});store.Save();form.RenderNow();
                    Check(CalendarRenderer.VisibleTodos(store).Any(t=>t.id=="undated"),"undated todos are visible without opening a separate window");
                    var row=form.LayoutInfo.TodoRow(0);await Click(new Point(row.X+12,row.Y+12));
                    Check(store.Data.todos.Single(t=>t.id=="smoke-todo").done,"desktop checkbox completes a todo directly");
                    await Click(Center(form.LayoutInfo.TodoDone));
                    int doneIndex=CalendarRenderer.VisibleTodos(store).FindIndex(t=>t.id=="smoke-todo");row=form.LayoutInfo.TodoRow(doneIndex);
                    await Click(new Point(row.X+12,row.Y+12));Check(!store.Data.todos.Single(t=>t.id=="smoke-todo").done,"completed todo can be unchecked in the visible panel");
                    store.Data.todos.RemoveAll(t=>t.id=="undated");store.Data.prefs.showDone=false;store.Save();
                    Check(!Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a=>a.Name.Contains("WebView")),"application has no WebView2 assembly reference");
                    Check(form.DockAttached&&form.Opacity==0,"native surface attached to Explorer with invisible host");
                    using(var map=MemoryMappedFile.OpenExisting(@"Local\SchoolSchedulerDockFrame.v4"))
                    using(var view=map.CreateViewAccessor(0,256)){
                        Check(view.ReadInt32(20)==initial.Width&&view.ReadInt32(24)==initial.Height,"native frame uses physical window dimensions");
                        Check(view.ReadInt32(56)>0&&view.ReadInt32(60)==0,"Explorer blends native bitmap successfully");
                        var before=form.Month;await Click(Center(form.LayoutInfo.Next));
                        Check(form.Month==before.AddMonths(1),"first desktop click moves the month");
                        await Click(Center(form.LayoutInfo.Prev));Check(form.Month==before,"previous month returns through Explorer input");
                        form.GoTo(new DateTime(2026,10,24));form.RenderNow();
                        int index=form.LayoutInfo.Index(new DateTime(2026,10,24));Point cell=Center(form.LayoutInfo.Cell(index));
                        Mouse(0x201,cell,1);await Task.Delay(30);Mouse(0x202,cell,0);await Task.Delay(60);Mouse(0x203,cell,1);Mouse(0x202,cell,0);
                        await Task.Delay(120);
                        Check(form.MemoEditor!=null&&!form.MemoEditor.IsDisposed,"double click opens in-cell native memo editor");
                        var expectedEditor=form.LayoutInfo.Cell(index);expectedEditor.Offset(form.Left,form.Top);
                        Check(Native.RectOf(form.MemoEditor.Handle)==expectedEditor,"native editor occupies the same physical date cell");
                        string inputClass=Native.ClassOf(form.MemoEditor.Editor.Handle);
                        Check(inputClass.Contains("EDIT"),"memo uses a Windows EDIT control for IME input");
                        Log("editor class="+inputClass);
                        IntPtr ignored;
                        Native.SendMessageTimeout(form.MemoEditor.Editor.Handle,0x102,new IntPtr('가'),new IntPtr(1),2,1000,out ignored);
                        Native.SendMessageTimeout(form.MemoEditor.Editor.Handle,0x102,new IntPtr('A'),new IntPtr(1),2,1000,out ignored);
                        Check(form.MemoEditor.Editor.Text=="가A","Unicode characters reach native editor");
                        form.MemoEditor.Close();await Task.Delay(120);
                        Check(store.Memo(new DateTime(2026,10,24))=="가A","native memo saved on editor close");
                        form.OpenMemo(new DateTime(2026,10,24));form.MemoEditor.Editor.Text="취소할 메모";
                        typeof(Control).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(form.MemoEditor.Editor,new object[]{new KeyEventArgs(Keys.Escape)});
                        Check(store.Memo(new DateTime(2026,10,24))=="가A","Escape cancels native memo changes");
                        var roundtrip=new CalendarStore(store.FilePath);
                        Check(roundtrip.Memo(new DateTime(2026,10,24))=="가A"&&roundtrip.Data.personal.Count==1&&roundtrip.Data.todos.Count==1,"JSON round trip preserves memos, personal events and todos");
                        store.Import("{\"personal\":[{\"id\":\"smoke-personal\",\"title\":\"개인 일정\",\"start\":\"2026-10-24\",\"end\":\"2026-10-26\",\"note\":\"백업\"}],\"todos\":[]}");
                        Check(store.Data.personal.Count==1&&store.Data.personal[0].note=="백업","legacy JSON import merges by id without duplicates");
                        Check(store.Entries(new DateTime(2026,10,24)).Any(x=>x.IsPersonal)&&store.Todos(new DateTime(2026,10,24)).Count==1,"personal range and todo due date render on weekends");
                        for(int grade=0;grade<=3;grade++){
                            store.Data.prefs.grade=grade;var snapshot=store.View(form.LayoutInfo.First);
                            Check(Enumerable.Range(0,42).All(day=>snapshot.Entries[day].Select(x=>x.Id).SequenceEqual(store.Entries(snapshot.First.AddDays(day)).Select(x=>x.Id))),"cached month matches all 42 date queries for grade "+grade);
                        }store.Data.prefs.grade=0;store.Save();form.RenderNow();
                        int fullBefore=view.ReadInt32(168);
                        foreach(int target in new[]{0,1,14,41,-1,5}){
                            Point point=target<0?new Point(100,40):Center(form.LayoutInfo.Cell(target));
                            form.HandleMouse(target<0?0x2A3:0x200,new Point(form.Left+point.X,form.Top+point.Y),0,0);form.RenderNow();
                            using(var expected=CalendarRenderer.Render(store,cfg,form.LayoutInfo,form.Selected,target,0,form.Feed.Name+" · 연결 설정"))
                                Check(Pixels(CurrentFrame()).SequenceEqual(Pixels(expected)),"incremental hover equals fresh rendering at "+target);
                        }
                        Check(view.ReadInt32(168)==fullBefore,"hover never publishes a full bitmap");
                        using(var pixelView=map.CreateViewAccessor(256,(long)form.Width*form.Height*4)){
                            var shared=new byte[form.Width*form.Height*4];pixelView.ReadArray(0,shared,0,shared.Length);
                            Check(shared.SequenceEqual(Pixels(CurrentFrame())),"partial IPC updates reproduce every frame pixel exactly");
                        }
                        Check(view.ReadInt64(160)<64L*1024*1024&&view.ReadInt64(160)>=256L+form.Width*form.Height*4L,"shared section commits only the frame pages");
                        long copied=view.ReadInt64(152);var location=form.Location;form.Left+=8;form.RenderNow();
                        Check(view.ReadInt64(152)==copied,"moving the calendar copies no pixels");form.Location=location;form.RenderNow();
                        int sameSequence=view.ReadInt32(8);form.RenderNow();Check(view.ReadInt32(8)==sameSequence,"unchanged explicit render does not publish a frame");
                        var bridge=typeof(WidgetForm).GetField("dock",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                        var sequenceField=typeof(DockBridge).GetField("sequence",BindingFlags.NonPublic|BindingFlags.Instance);sequenceField.SetValue(bridge,(int)sequenceField.GetValue(bridge)+3);
                        int nativeFullBefore=view.ReadInt32(184);var nextHover=Center(form.LayoutInfo.Cell(6));form.HandleMouse(0x200,new Point(form.Left+nextHover.X,form.Top+nextHover.Y),0,0);form.RenderNow();
                        Check(view.ReadInt32(184)==nativeFullBefore+1,"skipped delivery recovers with a complete native frame copy");
                        var toggle=typeof(WidgetForm).GetMethod("ToggleVisible",BindingFlags.NonPublic|BindingFlags.Instance);
                        toggle.Invoke(form,null);int hiddenSequence=view.ReadInt32(8);
                        store.SetMemo(new DateTime(2026,10,24),"숨김 중 수정");form.RenderNow();
                        Check(view.ReadInt32(8)==hiddenSequence,"hidden calendar does not render data changes");toggle.Invoke(form,null);
                        Check(view.ReadInt32(8)>hiddenSequence,"showing calendar publishes the latest hidden changes");store.SetMemo(new DateTime(2026,10,24),"가A");form.RenderNow();
                        Rectangle originalCell=form.LayoutInfo.Cell(index);cfg.FontSize=18;form.RenderNow();
                        Check(form.LayoutInfo.Cell(index)==originalCell&&Native.RectOf(form.Handle)==initial,"native font change preserves calendar geometry");cfg.FontSize=12;form.RenderNow();
                        using(var image=CalendarRenderer.Render(store,cfg,form.LayoutInfo,form.Selected,-1)){
                            image.Save(Path.Combine(Root,"native-preview.png"),ImageFormat.Png);
                            using(var narrow=CalendarRenderer.Render(store,cfg,new CalendarLayout(new Size(600,800),form.Month),form.Selected,-1))narrow.Save(Path.Combine(Root,"native-narrow-preview.png"),ImageFormat.Png);
                            var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                            byte[] pixels=new byte[image.Width*image.Height*4];Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);image.UnlockBits(bits);
                            bool premultiplied=true,transparent=false;
                            for(int i=0;i<pixels.Length;i+=4){byte a=pixels[i+3];if(pixels[i]>a||pixels[i+1]>a||pixels[i+2]>a)premultiplied=false;if(a<255)transparent=true;}
                            Check(premultiplied&&transparent,"native surface has valid premultiplied alpha and transparent pixels");
                        }
                        Point title=Center(form.LayoutInfo.Move);Mouse(0x201,title,1);await Task.Delay(40);Mouse(0x200,new Point(title.X+20,title.Y+12),1);await Task.Delay(40);Mouse(0x202,title,0);await Task.Delay(120);
                        Log("drag actual="+form.Bounds+" initial="+initial);
                        Check(form.Left==initial.Left+20&&form.Top==initial.Top+12,"native title drag moves calendar");form.Bounds=initial;form.RenderNow();
                        Point border=new Point(form.Width-2,form.Height-2);Mouse(0x201,border,1);await Task.Delay(40);Mouse(0x200,new Point(border.X+24,border.Y+18),1);await Task.Delay(40);Mouse(0x202,border,0);await Task.Delay(120);
                        Check(form.Width==initial.Width+24&&form.Height==initial.Height+18,"native border drag resizes calendar");form.Bounds=initial;form.RenderNow();
                        IntPtr dpiResult;Native.SendMessageTimeout(form.Handle,0x02E0,new IntPtr((144<<16)|144),IntPtr.Zero,2,1000,out dpiResult);await Task.Delay(80);
                        Check(Native.RectOf(form.Handle)==initial&&view.ReadInt32(20)==initial.Width,"DPI message preserves physical calendar size");
                        cfg.Mode=Mode.Normal;form.ApplyMode();Check(!form.DockAttached&&form.Opacity>0,"normal window mode still paints the native frame");
                        cfg.Mode=Mode.Dock;form.ApplyMode();Check(form.DockAttached&&Native.RectOf(form.Handle)==initial,"desktop mode reattaches without changing physical size");
                        int sequence=view.ReadInt32(8);await Task.Delay(500);
                        Check(view.ReadInt32(8)==sequence,"idle native calendar sends no capture or redraw frames");
                    }
                    Log("RESULT PASS");
                }catch(Exception ex){Log("RESULT FAIL "+ex);Environment.ExitCode=1;}
                finally{if(!initial.IsEmpty)form.Bounds=initial;cfg.FontSize=initialFontSize;form.SaveBounds();form.Quitting=true;form.DisposeTray();form.Dispose();Application.ExitThread();}
            };
            form.Show();Application.Run();
        }
    }
}
