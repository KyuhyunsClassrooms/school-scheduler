using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Windows.Forms;

namespace SchoolWidget
{
    static class PerformanceProbe
    {
        static WidgetForm form;
        static object Measure(string name,int count,Action<int> action)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var process=Process.GetCurrentProcess();var explorer=Process.GetProcessesByName("explorer").First();
            long allocated=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            int gen2=GC.CollectionCount(2);TimeSpan cpu=process.TotalProcessorTime,shellCpu=explorer.TotalProcessorTime;
            var watch=Stopwatch.StartNew();
            for(int i=0;i<count;i++)action(i);
            watch.Stop();process.Refresh();explorer.Refresh();
            return new {name,count,elapsedMs=watch.Elapsed.TotalMilliseconds,
                appCpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,explorerCpuMs=(explorer.TotalProcessorTime-shellCpu).TotalMilliseconds,
                allocatedBytes=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize-allocated,gen2Collections=GC.CollectionCount(2)-gen2,
                workingSetMiB=process.WorkingSet64/(double)(1024*1024),privateMiB=process.PrivateMemorySize64/(double)(1024*1024)};
        }
        static void Hover(int index)
        {
            var cell=form.LayoutInfo.Cell(index);var p=new Point(form.Left+cell.X+cell.Width/2,form.Top+cell.Y+cell.Height/2);
            form.HandleMouse(0x200,p,0,0);form.RenderNow();
        }
        [STAThread]static void Main(string[] args)
        {
            string label=args.Length==0?"probe":args[0];
            string root=Path.Combine(Environment.CurrentDirectory,"analysis","performance");Directory.CreateDirectory(root);
            Native.EnablePixelCoordinates();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            AppDomain.MonitoringIsEnabled=true;
            var cfg=Config.Load();var store=new CalendarStore(Path.Combine(root,"fixture-data.json"));store.Save();
            form=new WidgetForm(cfg,store);
            form.Shown+=(s,e)=>{
                try
                {
                    form.GoTo(new DateTime(2026,10,1));form.RenderNow();
                    for(int i=0;i<4;i++)Hover(i);
                    var hover=Measure("hover",84,i=>Hover(i%42));
                    var original=form.Location;
                    var move=Measure("move",24,i=>{form.Location=new Point(original.X+(i%2)*8,original.Y);form.RenderNow();});form.Location=original;form.RenderNow();
                    var content=Measure("month",6,i=>{form.GoTo(new DateTime(2026,10,1).AddMonths(i%2));form.RenderNow();});
                    var dense=new System.Collections.Generic.List<SchoolEvent>();
                    for(int i=0;i<5000;i++){var day=new DateTime(2026,1,1).AddDays(i%365);dense.Add(new SchoolEvent{id="dense-"+i,s=CalendarStore.DateKey(day),e=CalendarStore.DateKey(day.AddDays(2)),t="일정 "+i,c="학교",g="전체",n=""});}
                    store.ReplaceSchool(dense);form.RenderNow();
                    var manyEvents=Measure("5000-events",6,i=>{form.GoTo(new DateTime(2026,10,1).AddMonths(i%2));form.RenderNow();});
                    form.GoTo(new DateTime(2026,10,1));form.RenderNow();
                    var result=new {label,width=form.Width,height=form.Height,hover,move,content,manyEvents};
                    string text=CalendarStore.Json.Serialize(result);File.WriteAllText(Path.Combine(root,label+".json"),text);Console.WriteLine(text);
                }
                catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
                finally{form.Quitting=true;form.DisposeTray();form.Dispose();Application.ExitThread();}
            };
            form.Show();Application.Run();
        }
    }
}
