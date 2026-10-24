// Used only when updating an old installation. Loads its existing SDK DLLs;
// the native application and a clean native installation do not load WebView2.
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using SchoolWidget;

static class MigrateWebData
{
    static string legacyDir,profile,output;
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=3)return 2;
        legacyDir=Path.GetFullPath(args[0]);profile=Path.GetFullPath(args[1]);output=Path.GetFullPath(args[2]);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name;
            if(!name.StartsWith("Microsoft.Web.WebView2.",StringComparison.Ordinal))return null;
            return Assembly.LoadFrom(Path.Combine(legacyDir,"bin",name+".dll"));
        };
        try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
        return Run();
    }
    static int Run()
    {
        var form=new Form{Width=600,Height=700,Opacity=0,ShowInTaskbar=false};int result=0;
        form.Shown+=async(s,e)=>{
            CoreWebView2CompositionController browser=null;
            try{
                bool existing=File.Exists(output);
                var store=new CalendarStore(output);
                var environment=await CoreWebView2Environment.CreateAsync(null,profile,null);
                browser=await environment.CreateCoreWebView2CompositionControllerAsync(form.Handle);
                browser.Bounds=new System.Drawing.Rectangle(0,0,580,650);browser.IsVisible=true;
                string html=Path.Combine(legacyDir,"학사일정-스케줄러.html");
                if(!File.Exists(html))throw new FileNotFoundException("이전 HTML 파일이 없습니다.",html);
                var navigation=new TaskCompletionSource<bool>();
                browser.CoreWebView2.NavigationCompleted+=(sender,eventArgs)=>navigation.TrySetResult(eventArgs.IsSuccess);
                browser.CoreWebView2.Navigate(new Uri(html).AbsoluteUri+"?desktop=1#widget");
                if(await Task.WhenAny(navigation.Task,Task.Delay(15000))!=navigation.Task||!await navigation.Task)throw new Exception("이전 저장소를 열지 못했습니다.");
                string encoded=await browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('school-scheduler-web-v1')");
                string json=CalendarStore.Json.Deserialize<string>(encoded);
                if(!string.IsNullOrWhiteSpace(json)){
                    var incoming=CalendarStore.ReadDocument(json);
                    if(!existing){store.Data.prefs=incoming.prefs;store.Import(json);}
                    else{
                        incoming.personal.RemoveAll(p=>store.Data.personal.Any(x=>x.id==p.id));
                        incoming.todos.RemoveAll(t=>store.Data.todos.Any(x=>x.id==t.id));
                        incoming.memos.Clear();
                        if(incoming.personal.Count>0||incoming.todos.Count>0)store.Import(CalendarStore.Json.Serialize(incoming));
                    }
                }else if(!existing)store.Save();
                File.WriteAllText(output+".migration.log","Legacy data preserved. personal="+store.Data.personal.Count+" todos="+store.Data.todos.Count+"\r\n");
            }catch(Exception ex){result=1;File.WriteAllText(output+".migration-error.txt",ex.ToString());}
            finally{if(browser!=null)browser.Close();form.Dispose();Application.ExitThread();}
        };
        form.Show();Application.Run();return result;
    }
}
