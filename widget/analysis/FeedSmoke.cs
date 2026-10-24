using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SchoolWidget
{
    static class FeedSmoke
    {
        static string root=Path.Combine(Environment.CurrentDirectory,"analysis","feed-smoke",DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        static void Check(bool okay,string label){if(!okay)throw new Exception(label);File.AppendAllText(Path.Combine(root,"result.txt"),"PASS "+label+"\r\n");}
        static async Task Mode(string mode){using(var client=new HttpClient())await client.GetStringAsync("http://127.0.0.1:3101/control?mode="+mode);}
        [STAThread]static void Main()
        {
            Directory.CreateDirectory(root);
            var host=new Form{ShowInTaskbar=false,Opacity=0};
            host.Shown+=async(s,e)=>{
                try{await Run();File.AppendAllText(Path.Combine(root,"result.txt"),"RESULT PASS\r\n");}
                catch(Exception ex){File.AppendAllText(Path.Combine(root,"result.txt"),"RESULT FAIL "+ex);Environment.ExitCode=1;}
                finally{host.Close();}
            };Application.Run(host);
        }
        static async Task Run()
        {
            var store=new CalendarStore(Path.Combine(root,"calendar-data.json"));
            using(var sync=new FeedSync(store))
            {
                Check(store.School.Count==0&&sync.Url=="","fresh install has no school or central server");
                store.Data.todos.Add(new TodoItem{id="private-todo",title="내 할 일"});store.SetMemo(new DateTime(2026,10,24),"내 메모");
                using(var restart=new FeedSync(new CalendarStore(store.FilePath)))Check(restart.Name==null,"new install does not acquire a bundled school after restart");
                await Mode("first");await sync.ConnectAsync("http://127.0.0.1:3101/feed","");
                Check(store.School.Count==2&&store.Entries(new DateTime(2026,10,24)).Count==1,"valid HTTP snapshot includes weekend events");
                await Mode("changed");await sync.RefreshAsync();
                Check(store.School.Count==1&&store.School[0].t=="수정된 일정","refresh replaces edits and removes deleted events");
                Check(store.Data.todos.Count==1&&store.Memo(new DateTime(2026,10,24))=="내 메모","sync preserves personal todos and memos");
                await Mode("invalid");await sync.RefreshAsync();Check(store.School.Count==1&&sync.Revision=="changed","invalid version preserves last valid snapshot");
                await Mode("offline");await sync.RefreshAsync();
                var offlineStore=new CalendarStore(store.FilePath);
                using(var offline=new FeedSync(offlineStore))Check(offlineStore.School.Count==1&&offlineStore.School[0].t=="수정된 일정","offline restart restores cached shared calendar");
                await Mode("first");string oldUrl=sync.Url;
                bool rejected=false;try{await sync.ConnectAsync("http://127.0.0.1:3101/auth","bad");}catch{rejected=true;}
                Check(rejected&&sync.Url==oldUrl&&sync.Revision=="changed","failed new source does not replace current connection");
                await sync.ConnectAsync("http://127.0.0.1:3101/auth","fixture-token");
                Check(sync.GetToken()=="fixture-token"&&!File.ReadAllText(Path.Combine(root,"schedule-source.json")).Contains("fixture-token"),"bearer read token works and is encrypted with Windows DPAPI");
                rejected=false;try{await sync.ConnectAsync("http://127.0.0.1:3101/redirect","fixture-token");}catch{rejected=true;}
                Check(rejected,"redirects cannot forward a reading credential to another host");
                await Mode("huge");await sync.RefreshAsync();Check(store.School.Count==2,"oversized response keeps last good schedule");
                await Mode("empty");await sync.RefreshAsync();Check(store.School.Count==0,"published empty snapshot removes all shared entries");
                sync.ImportLocal("{\"schemaVersion\":1,\"name\":\"로컬 학교\",\"revision\":\"local\",\"updatedAt\":\"2026-10-24T00:00:00Z\",\"events\":[]}");sync.Disconnect();
                using(var disconnected=new FeedSync(new CalendarStore(store.FilePath)))Check(disconnected.Name==null&&disconnected.Url=="","disconnect persists and does not resurrect a local cache");
            }
            bool unsafeUrl=false;try{FeedSync.ValidateUrl("http://school.example/feed.json");}catch{unsafeUrl=true;}
            Check(unsafeUrl,"remote servers require HTTPS");
            Console.WriteLine(root);
        }
    }
}
