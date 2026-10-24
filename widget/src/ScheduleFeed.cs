using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SchoolWidget
{
    public sealed class FeedEvent
    {
        public string id, start, end, title, category, grades, note;
    }
    public sealed class ScheduleFeed
    {
        public int schemaVersion;
        public string name, revision, updatedAt;
        public List<FeedEvent> events;
        public static ScheduleFeed Parse(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > FeedSync.MaxBytes) throw new FormatException("일정 파일은 2MB 이하여야 합니다.");
            var feed = new JavaScriptSerializer().Deserialize<ScheduleFeed>(text);
            DateTimeOffset updated;
            if (feed == null || feed.schemaVersion != 1 || string.IsNullOrWhiteSpace(feed.name) || feed.name.Length > 100 ||
                string.IsNullOrWhiteSpace(feed.revision) || feed.revision.Length > 200 ||
                !DateTimeOffset.TryParse(feed.updatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out updated) ||
                feed.events == null || feed.events.Count > 10000)
                throw new FormatException("지원하는 학사일정 JSON 형식(schemaVersion 1)이 아닙니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in feed.events)
            {
                DateTime start, end;
                if (item == null || string.IsNullOrWhiteSpace(item.id) || item.id.Length > 200 || !ids.Add(item.id) ||
                    string.IsNullOrWhiteSpace(item.title) || item.title.Length > 300 ||
                    !CalendarStore.TryDate(item.start, out start) || !CalendarStore.TryDate(item.end, out end) || end < start ||
                    (item.note ?? "").Length > 20000 || (item.category ?? "").Length > 100 || (item.grades ?? "").Length > 100)
                    throw new FormatException("일정 ID·제목·날짜 또는 중복 항목을 확인하세요.");
            }
            return feed;
        }
        public List<SchoolEvent> ToSchool()
        {
            var list = new List<SchoolEvent>();
            foreach (var e in events) list.Add(new SchoolEvent { id=e.id,s=e.start,e=e.end,t=e.title,c=e.category??"학교",g=string.IsNullOrEmpty(e.grades)?"전체":e.grades,n=e.note??"" });
            return list;
        }
    }
    public sealed class FeedSettings
    {
        public string url = "", protectedToken = "";
        public bool disconnected;
    }
    public sealed class FeedCache
    {
        public string sourceKey;
        public ScheduleFeed feed;
    }
    public sealed class FeedSync : IDisposable
    {
        public const int MaxBytes = 2 * 1024 * 1024;
        readonly CalendarStore store;
        readonly string settingsPath, cachePath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength=MaxBytes+4096 };
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        FeedSettings settings = new FeedSettings();
        int generation;
        bool disposed;
        public string Url { get { return settings.url; } }
        public string Name { get; private set; }
        public string Revision { get; private set; }
        public string Status { get; private set; }
        public bool Busy { get; private set; }
        public event Action Changed;
        public FeedSync(CalendarStore data)
        {
            store=data; string dir=Path.GetDirectoryName(store.FilePath);
            settingsPath=Path.Combine(dir,"schedule-source.json");cachePath=Path.Combine(dir,"schedule-cache.json");
            Status="일정 서버를 연결하세요";
            try
            {
                if(File.Exists(settingsPath))settings=json.Deserialize<FeedSettings>(File.ReadAllText(settingsPath,Encoding.UTF8))??new FeedSettings();
                settings.url=settings.url??"";
                if(!settings.disconnected&&File.Exists(cachePath))
                {
                    var cache=json.Deserialize<FeedCache>(File.ReadAllText(cachePath,Encoding.UTF8));
                    if(cache!=null&&cache.sourceKey==SourceKey(settings))
                    {
                        var feed=ScheduleFeed.Parse(json.Serialize(cache.feed));Apply(feed);Status=Url.Length==0?"기존 로컬 일정":"저장된 일정 · 연결 확인 전";
                    }
                }
                // Upgrade only: preserve the previously bundled calendar as a local snapshot.
                // A fresh installation starts without a school or a remote address.
                else if(!File.Exists(settingsPath)&&File.Exists(store.FilePath))
                {
                    using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("school-events.json"))
                    if(stream!=null)using(var reader=new StreamReader(stream,Encoding.UTF8))
                    {
                        var old=json.Deserialize<List<SchoolEvent>>(reader.ReadToEnd());
                        var feed=new ScheduleFeed{schemaVersion=1,name="기존 로컬 일정",revision="legacy-2026",updatedAt=DateTimeOffset.UtcNow.ToString("o"),events=new List<FeedEvent>()};
                        foreach(var e in old)feed.events.Add(new FeedEvent{id=e.id,start=e.s,end=e.e,title=e.t,category=e.c,grades=e.g,note=e.n});
                        WriteCache(feed,settings);Apply(feed);Status="기존 로컬 일정 · 서버 미연결";
                    }
                }
                if(!File.Exists(settingsPath))WriteAtomic(settingsPath,json.Serialize(settings));
            }
            catch(Exception){Status="연결 설정 또는 저장된 일정을 읽지 못했습니다. 연결 설정을 확인하세요.";}
        }
        static void WriteAtomic(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            File.WriteAllText(temp,text,new UTF8Encoding(false));
            if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);
        }
        static string SourceKey(FeedSettings value)
        {
            using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((value.url??"")+"\n"+(value.protectedToken??""))));
        }
        static string Protect(string token)
        {
            return string.IsNullOrWhiteSpace(token)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()),null,DataProtectionScope.CurrentUser));
        }
        public string GetToken()
        {
            return string.IsNullOrEmpty(settings.protectedToken)?"":Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(settings.protectedToken),null,DataProtectionScope.CurrentUser));
        }
        public static Uri ValidateUrl(string text)
        {
            Uri uri;
            if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||
                !(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback))
                throw new FormatException("HTTPS 일정 주소를 입력하세요. 로컬 테스트는 http://localhost 주소도 사용할 수 있습니다.");
            return uri;
        }
        public async Task ConnectAsync(string url,string token)
        {
            var next=new FeedSettings{url=ValidateUrl(url.Trim()).AbsoluteUri,protectedToken=Protect(token)};
            int request=++generation;Busy=true;Status="새 서버 확인 중…";Notify();
            try
            {
                var feed=await DownloadAsync(next,lifetime.Token);
                if(disposed||request!=generation)return;
                // Cache first; mismatched cache is ignored after an interrupted settings write.
                WriteCache(feed,next);WriteAtomic(settingsPath,json.Serialize(next));settings=next;
                Apply(feed);Status="동기화 완료 · "+DateTime.Now.ToString("HH:mm");
            }
            catch{if(!disposed&&request==generation)Status="연결 실패 · 기존 일정 유지";throw;}
            finally{if(request==generation){Busy=false;Notify();}}
        }
        public async Task RefreshAsync()
        {
            if(disposed||Busy||string.IsNullOrEmpty(Url))return;
            int request=++generation;Busy=true;
            try
            {
                var feed=await DownloadAsync(settings,lifetime.Token);
                if(disposed||request!=generation)return;
                WriteCache(feed,settings);
                if(feed.revision!=Revision||feed.name!=Name)Apply(feed);
                Status="동기화 완료 · "+DateTime.Now.ToString("HH:mm");
            }
            catch(OperationCanceledException){if(!disposed)Status="응답 시간 초과 · 저장된 일정 사용";}
            catch(Exception){if(!disposed)Status="연결 실패 · 저장된 일정 사용";}
            finally{if(request==generation){Busy=false;Notify();}}
        }
        public void Disconnect()
        {
            ++generation;Busy=false;var empty=new FeedSettings{disconnected=true};
            WriteAtomic(settingsPath,json.Serialize(empty));settings=empty;
            Name=null;Revision=null;store.ReplaceSchool(new SchoolEvent[0]);Status="일정 서버를 연결하세요";Notify();
        }
        public void ImportLocal(string text)
        {
            var feed=ScheduleFeed.Parse(text);++generation;Busy=false;var next=new FeedSettings();
            WriteCache(feed,next);WriteAtomic(settingsPath,json.Serialize(next));settings=next;Apply(feed);Status="로컬 JSON 일정 · 자동 갱신 없음";Notify();
        }
        void Apply(ScheduleFeed feed){Name=feed.name;Revision=feed.revision;store.ReplaceSchool(feed.ToSchool());}
        void WriteCache(ScheduleFeed feed,FeedSettings source){WriteAtomic(cachePath,json.Serialize(new FeedCache{sourceKey=SourceKey(source),feed=feed}));}
        void Notify(){if(!disposed&&Changed!=null)Changed();}
        static async Task<ScheduleFeed> DownloadAsync(FeedSettings source,CancellationToken cancellation)
        {
            ValidateUrl(source.url);
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            using(var handler=new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate})
            using(var client=new HttpClient(handler))
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(15000);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                if(!string.IsNullOrEmpty(source.protectedToken))
                {
                    string token=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(source.protectedToken),null,DataProtectionScope.CurrentUser));
                    client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
                }
                using(var response=await client.GetAsync(source.url,HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                {
                    if(!response.IsSuccessStatusCode)throw new IOException("일정 서버 응답: "+(int)response.StatusCode+". 주소와 읽기 토큰을 확인하세요. 리디렉션 주소 대신 최종 JSON 주소를 입력해야 합니다.");
                    if(response.Content.Headers.ContentLength>MaxBytes)throw new IOException("일정 파일이 2MB를 넘습니다.");
                    using(var input=await response.Content.ReadAsStreamAsync())
                    using(var output=new MemoryStream())
                    {
                        byte[] buffer=new byte[8192];int count;
                        while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0)
                        {
                            if(output.Length+count>MaxBytes)throw new IOException("일정 파일이 2MB를 넘습니다.");
                            output.Write(buffer,0,count);
                        }
                        return ScheduleFeed.Parse(new UTF8Encoding(false,true).GetString(output.ToArray()).TrimStart('\uFEFF'));
                    }
                }
            }
        }
        public void Dispose(){disposed=true;++generation;lifetime.Cancel();lifetime.Dispose();}
    }
}
