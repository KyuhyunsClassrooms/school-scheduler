// 할 일을 Google Tasks 목록 하나와 양방향으로 맞춘다.
// 연결 정보와 대응표는 개인 데이터와 별도 파일(google-tasks.json)에 두고, 토큰은 DPAPI로 보호한다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SchoolWidget
{
    // hash: 마지막 동기화 때의 내용, seenHash: 현재 내용, changedAt: 현재 내용으로 바뀐 시각(UTC).
    public sealed class TaskLink
    {
        public string googleId, hash, remoteUpdated, seenHash, changedAt;
    }
    public sealed class GoogleTaskSettings
    {
        public string clientId = "", protectedSecret = "", protectedRefresh = "", listId = "", listTitle = "";
        public Dictionary<string, TaskLink> links = new Dictionary<string, TaskLink>();
    }
    public sealed class GoogleApiException : IOException
    {
        public readonly int Code;
        public GoogleApiException(int code, string message) : base(message) { Code = code; }
    }

    public sealed class GoogleTaskSync : IDisposable
    {
        const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth", TokenUrl = "https://oauth2.googleapis.com/token",
                     ApiUrl = "https://tasks.googleapis.com/tasks/v1", Scope = "https://www.googleapis.com/auth/tasks";
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        readonly CalendarStore store;
        readonly string settingsPath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly System.Windows.Forms.Timer soon = new System.Windows.Forms.Timer { Interval = 3000 };
        GoogleTaskSettings settings = new GoogleTaskSettings();
        string access;
        DateTime accessExpires;
        bool disposed;
        // 메서드, 경로, 본문 → 응답. 검사에서는 가짜 서버로 바꾼다.
        internal Func<string, string, object, Task<Dictionary<string, object>>> Transport;
        public bool Busy { get; private set; }
        public string Status { get; private set; }
        public bool Connected { get { return settings.listId.Length > 0; } }
        // 마지막 동기화가 실패했는지. 로그인 만료처럼 사용자가 알아야 하는 상태다.
        public bool Failed { get; private set; }
        bool linksDirty;
        public string ListTitle { get { return settings.listTitle; } }
        public event Action Changed;

        public GoogleTaskSync(CalendarStore data)
        {
            store = data; Transport = Send;
            settingsPath = Path.Combine(Path.GetDirectoryName(store.FilePath), "google-tasks.json");
            Status = "연결되지 않음";
            try
            {
                if (File.Exists(settingsPath))
                {
                    settings = json.Deserialize<GoogleTaskSettings>(File.ReadAllText(settingsPath, Encoding.UTF8)) ?? new GoogleTaskSettings();
                    settings.links = settings.links ?? new Dictionary<string, TaskLink>();
                    settings.listId = settings.listId ?? ""; settings.listTitle = settings.listTitle ?? "";
                    if (Connected) Status = "연결됨 · 동기화 대기";
                }
            }
            catch (Exception) { settings = new GoogleTaskSettings(); Status = "Google Tasks 연결 설정을 읽지 못했습니다. 다시 연결하세요."; }
            soon.Tick += async (s, e) => { soon.Stop(); await SyncAsync(); };
            store.Changed += Stamp;
        }

        static string Hash(TodoItem todo)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(todo.title + "\n" + (todo.due ?? "") + "\n" + todo.done + "\n" + (todo.note ?? ""))));
        }
        static string Now() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }
        static DateTime Time(string text)
        {
            DateTime value;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value) ? value : DateTime.MinValue;
        }
        // 할 일이 바뀐 시각을 기록하고, 보낼 것이 있으면 잠시 뒤 동기화한다.
        void Stamp()
        {
            if (disposed || !Connected) return;
            bool pending = false, dirty = false;
            var ids = new HashSet<string>();
            foreach (var todo in store.Data.todos)
            {
                ids.Add(todo.id);
                TaskLink link;
                if (!settings.links.TryGetValue(todo.id, out link)) { pending = true; continue; }
                string hash = Hash(todo);
                if (link.seenHash != hash) { link.seenHash = hash; link.changedAt = Now(); dirty = true; }
                if (link.hash != hash) pending = true;
            }
            if (settings.links.Keys.Any(id => !ids.Contains(id))) pending = true;
            if (dirty) try { SaveSettings(); } catch (Exception) { }
            if (pending && !Busy) { soon.Stop(); soon.Start(); }
        }
        void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); string temp = settingsPath + ".tmp";
            File.WriteAllText(temp, json.Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null, true); else File.Move(temp, settingsPath);
        }
        void Notify() { if (!disposed && Changed != null) Changed(); }

        // ── 로그인 ──────────────────────────────────────────────
        static string Protect(string text) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser)); }
        static string Unprotect(string text) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser)); }
        static string Url64(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static string Random64() { var bytes = new byte[48]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes); return Url64(bytes); }
        static string Text(Dictionary<string, object> node, string key)
        {
            object value;
            return node != null && node.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }
        async Task<Dictionary<string, object>> PostForm(Dictionary<string, string> form)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), lifetime.Token))
            {
                var result = json.DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                if (!response.IsSuccessStatusCode || result == null)
                    throw new GoogleApiException((int)response.StatusCode, "Google 로그인 오류: " + (Text(result, "error_description").Length > 0 ? Text(result, "error_description") : Text(result, "error")));
                return result;
            }
        }
        void KeepAccess(Dictionary<string, object> token)
        {
            access = Text(token, "access_token");
            double seconds; double.TryParse(Text(token, "expires_in"), NumberStyles.Any, CultureInfo.InvariantCulture, out seconds);
            accessExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds) - 60);
        }
        // 빌드할 때 src\google-client.json을 넣었으면 파일을 고르지 않고 바로 로그인한다.
        static string BuiltInClient()
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("google-client.json"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
        public static bool HasBuiltInClient { get { return BuiltInClient() != null; } }
        // '데스크톱 앱' OAuth 클라이언트 JSON으로 브라우저 로그인을 한다. 경로가 null이면 내장 클라이언트를 쓴다. 작업 목록들을 돌려준다.
        public async Task<List<KeyValuePair<string, string>>> LoginAsync(string clientJsonPath)
        {
            var root = json.DeserializeObject(clientJsonPath == null ? BuiltInClient() : File.ReadAllText(clientJsonPath, Encoding.UTF8)) as Dictionary<string, object>;
            object node;
            var installed = root != null && root.TryGetValue("installed", out node) ? node as Dictionary<string, object> : null;
            string id = Text(installed, "client_id"), secret = Text(installed, "client_secret");
            if (id.Length == 0) throw new FormatException("'데스크톱 앱' 유형으로 만든 OAuth 클라이언트의 JSON 파일을 선택하세요.");
            string verifier = Random64(), state = Random64(), challenge;
            using (var sha = SHA256.Create()) challenge = Url64(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string redirect = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/", code;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                timeout.CancelAfter(180000);
                Process.Start(AuthUrl + "?response_type=code&access_type=offline&prompt=consent&code_challenge_method=S256&client_id=" + Uri.EscapeDataString(id) +
                    "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&scope=" + Uri.EscapeDataString(Scope) + "&code_challenge=" + challenge + "&state=" + state);
                code = await WaitForCode(listener, state, timeout.Token);
            }
            var form = new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", code }, { "client_id", id },
                { "redirect_uri", redirect }, { "code_verifier", verifier } };
            if (secret.Length > 0) form["client_secret"] = secret;
            var token = await PostForm(form);
            if (Text(token, "refresh_token").Length == 0) throw new IOException("Google이 갱신 토큰을 주지 않았습니다. 다시 로그인하세요.");
            settings = new GoogleTaskSettings { clientId = id, protectedSecret = secret.Length == 0 ? "" : Protect(secret), protectedRefresh = Protect(Text(token, "refresh_token")) };
            KeepAccess(token); SaveSettings();
            var lists = new List<KeyValuePair<string, string>>();
            var result = await Transport("GET", "/users/@me/lists?maxResults=100", null);
            foreach (var item in Items(result)) lists.Add(new KeyValuePair<string, string>(Text(item, "id"), Text(item, "title")));
            if (lists.Count == 0) throw new IOException("Google Tasks 작업 목록이 없습니다.");
            return lists;
        }
        static async Task<string> WaitForCode(TcpListener listener, string state, CancellationToken cancel)
        {
            using (cancel.Register(listener.Stop))
            try
            {
                while (true)
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var buffer = new byte[8192];
                        int count = await stream.ReadAsync(buffer, 0, buffer.Length);
                        string[] request = Encoding.ASCII.GetString(buffer, 0, count).Split('\r')[0].Split(' ');
                        var query = new Dictionary<string, string>();
                        if (request.Length > 1 && request[1].Contains("?"))
                            foreach (string pair in request[1].Substring(request[1].IndexOf('?') + 1).Split('&'))
                            {
                                int eq = pair.IndexOf('=');
                                if (eq > 0) query[pair.Substring(0, eq)] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
                            }
                        string sent, value;
                        bool mine = query.TryGetValue("state", out sent) && sent == state, ok = mine && query.TryGetValue("code", out value) && value.Length > 0;
                        byte[] page = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>학사일정 달력</title><body style='font-family:sans-serif;padding:40px'>" +
                            (ok ? "Google Tasks 연결이 끝났습니다. 이 창을 닫아도 됩니다." : mine ? "연결이 취소되었습니다." : "") + "</body>");
                        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (mine ? "200 OK" : "404 Not Found") + "\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\nContent-Length: " + page.Length + "\r\n\r\n");
                        await stream.WriteAsync(head, 0, head.Length); await stream.WriteAsync(page, 0, page.Length);
                        if (ok) return query["code"];
                        if (mine) throw new IOException("Google 로그인이 취소되었습니다.");
                    }
            }
            catch (ObjectDisposedException) { throw new TimeoutException("3분 안에 로그인이 끝나지 않았습니다."); }
            catch (SocketException) { throw new TimeoutException("3분 안에 로그인이 끝나지 않았습니다."); }
            finally { listener.Stop(); }
        }
        // 목록을 바꾸면 대응표를 새로 만든다.
        public void SelectList(string id, string title)
        {
            if (settings.listId != id) settings.links = new Dictionary<string, TaskLink>();
            settings.listId = id; settings.listTitle = title; SaveSettings();
            Status = "연결됨 · 동기화 대기"; Notify();
        }
        public void Disconnect()
        {
            soon.Stop(); access = null; Failed = false; settings = new GoogleTaskSettings();
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            Status = "연결되지 않음"; Notify();
        }
        async Task<string> AccessAsync()
        {
            if (access != null && DateTime.UtcNow < accessExpires) return access;
            var form = new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "client_id", settings.clientId },
                { "refresh_token", Unprotect(settings.protectedRefresh) } };
            if (settings.protectedSecret.Length > 0) form["client_secret"] = Unprotect(settings.protectedSecret);
            KeepAccess(await PostForm(form));
            return access;
        }
        async Task<Dictionary<string, object>> Send(string method, string path, object body)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var request = new HttpRequestMessage(new HttpMethod(method), ApiUrl + path))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await AccessAsync());
                if (body != null) request.Content = new StringContent(json.Serialize(body), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, lifetime.Token))
                {
                    string text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) throw new GoogleApiException((int)response.StatusCode, "Google Tasks 응답 " + (int)response.StatusCode);
                    return text.Length == 0 ? new Dictionary<string, object>() : json.DeserializeObject(text) as Dictionary<string, object>;
                }
            }
        }

        // ── 동기화 ──────────────────────────────────────────────
        static IEnumerable<Dictionary<string, object>> Items(Dictionary<string, object> result)
        {
            object items;
            if (result != null && result.TryGetValue("items", out items) && items is IEnumerable)
                foreach (object item in (IEnumerable)items) if (item is Dictionary<string, object>) yield return (Dictionary<string, object>)item;
        }
        static bool Deleted(Dictionary<string, object> task) { return Text(task, "deleted").Equals("true", StringComparison.OrdinalIgnoreCase); }
        static bool Done(Dictionary<string, object> task) { return Text(task, "status") == "completed"; }
        static string Due(Dictionary<string, object> task) { string due = Text(task, "due"); return due.Length >= 10 ? due.Substring(0, 10) : ""; }
        static string Limit(string text, int max) { text = text ?? ""; return text.Length > max ? text.Substring(0, max) : text; }
        static Dictionary<string, object> Body(TodoItem todo)
        {
            // Google의 길이 제한을 넘으면 항목 하나 때문에 전체 동기화가 계속 실패한다.
            var body = new Dictionary<string, object> { { "title", Limit(todo.title, 1024) }, { "notes", Limit(todo.note, 8192) },
                { "due", string.IsNullOrEmpty(todo.due) ? null : todo.due + "T00:00:00.000Z" }, { "status", todo.done ? "completed" : "needsAction" } };
            if (!todo.done) body["completed"] = null;
            return body;
        }
        // 중요 표시·만든 날짜처럼 Google에 없는 값은 그대로 둔다.
        static void Apply(TodoItem todo, Dictionary<string, object> task)
        {
            if (Text(task, "title").Trim().Length > 0) todo.title = Text(task, "title").Trim();
            todo.note = Text(task, "notes");
            todo.due = Due(task).Length == 0 ? null : Due(task);
            if (Done(task) == todo.done) return;
            todo.done = Done(task);
            DateTime completed = Time(Text(task, "completed"));
            todo.doneAt = !todo.done ? null : (completed == DateTime.MinValue ? DateTime.Now : completed.ToLocalTime()).ToString("yyyy-MM-dd HH:mm");
        }
        string TasksPath { get { return "/lists/" + Uri.EscapeDataString(settings.listId) + "/tasks"; } }
        void Linked(TodoItem todo, Dictionary<string, object> task)
        {
            string hash = Hash(todo); linksDirty = true;
            settings.links[todo.id] = new TaskLink { googleId = Text(task, "id"), hash = hash, seenHash = hash, remoteUpdated = Text(task, "updated"), changedAt = Now() };
        }
        public async Task SyncAsync()
        {
            if (disposed || Busy || !Connected) return;
            Busy = true; soon.Stop();
            bool localChanged = false; string error = null;
            var todos = store.Data.todos;
            try
            {
                var remote = new Dictionary<string, Dictionary<string, object>>();
                string page = null;
                do
                {
                    var result = await Transport("GET", TasksPath + "?maxResults=100&showCompleted=true&showHidden=true&showDeleted=true" +
                        (page == null ? "" : "&pageToken=" + Uri.EscapeDataString(page)), null);
                    foreach (var item in Items(result)) remote[Text(item, "id")] = item;
                    page = Text(result, "nextPageToken");
                } while (page.Length > 0);
                // 할 일 파일이 초기화됐거나 목록이 통째로 바뀐 경우 반대쪽을 대량으로 지우지 않는다.
                var present = new HashSet<string>(todos.Select(t => t.id));
                int limit = Math.Max(5, settings.links.Count / 2);
                if (settings.links.Count(p => !present.Contains(p.Key)) > limit || settings.links.Count(p => present.Contains(p.Key) && !remote.ContainsKey(p.Value.googleId)) > limit)
                    throw new IOException("한꺼번에 너무 많은 할 일이 사라져 동기화를 멈췄습니다. 연결을 해제한 뒤 다시 연결하면 양쪽을 합칩니다.");
                var handled = new HashSet<string>(settings.links.Values.Select(link => link.googleId));
                foreach (var todo in todos.ToList())
                {
                    TaskLink link; Dictionary<string, object> task;
                    string hash = Hash(todo);
                    if (!settings.links.TryGetValue(todo.id, out link))
                    {
                        // 처음 연결할 때 제목·기한·완료가 같은 항목은 새로 만들지 않고 잇는다.
                        task = remote.Values.FirstOrDefault(r => !Deleted(r) && !handled.Contains(Text(r, "id")) &&
                            Text(r, "title").Trim() == todo.title && Due(r) == (todo.due ?? "") && Done(r) == todo.done);
                        task = task == null ? await Transport("POST", TasksPath, Body(todo))
                            : await Transport("PATCH", TasksPath + "/" + Uri.EscapeDataString(Text(task, "id")), Body(todo));
                        Linked(todo, task); handled.Add(Text(task, "id"));
                        continue;
                    }
                    remote.TryGetValue(link.googleId, out task);
                    bool mine = link.hash != hash;
                    if (task == null || Deleted(task))
                    {
                        // Google에서 지운 항목. 그 뒤 PC에서 고쳤다면 다시 만든다.
                        if (mine) Linked(todo, await Transport("POST", TasksPath, Body(todo)));
                        else { todos.Remove(todo); settings.links.Remove(todo.id); localChanged = linksDirty = true; }
                        continue;
                    }
                    bool theirs = Text(task, "updated") != link.remoteUpdated;
                    if (mine && (!theirs || Time(link.changedAt) >= Time(Text(task, "updated"))))
                        Linked(todo, await Transport("PATCH", TasksPath + "/" + Uri.EscapeDataString(link.googleId), Body(todo)));
                    else if (theirs) { Apply(todo, task); if (Hash(todo) != hash) localChanged = true; Linked(todo, task); }
                }
                foreach (var pair in settings.links.Where(p => !todos.Any(t => t.id == p.Key)).ToList())
                {
                    Dictionary<string, object> task;
                    if (remote.TryGetValue(pair.Value.googleId, out task) && !Deleted(task))
                        try { await Transport("DELETE", TasksPath + "/" + Uri.EscapeDataString(pair.Value.googleId), null); }
                        catch (GoogleApiException ex) { if (ex.Code != 404 && ex.Code != 410) throw; }
                    settings.links.Remove(pair.Key); linksDirty = true;
                }
                foreach (var task in remote.Values)
                {
                    if (handled.Contains(Text(task, "id")) || Deleted(task) || Text(task, "title").Trim().Length == 0) continue;
                    var todo = new TodoItem { id = Guid.NewGuid().ToString("N"), title = "", created = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
                    Apply(todo, task); todos.Add(todo); Linked(todo, task); localChanged = true;
                }
            }
            catch (OperationCanceledException) { error = "응답 시간 초과"; }
            catch (Exception ex) { error = ex.Message; }
            // 일부만 반영된 뒤 실패해도 대응표와 할 일을 저장해 다음 동기화에서 중복을 만들지 않는다.
            if (!disposed)
            {
                try { if (linksDirty) { SaveSettings(); linksDirty = false; } if (localChanged) store.Save(); }
                catch (Exception ex) { error = error ?? ex.Message; }
                Failed = error != null;
                Status = error == null ? "동기화 완료 · " + DateTime.Now.ToString("HH:mm") : "동기화 실패 · " + error;
            }
            Busy = false;
            // 동기화하는 동안 고친 할 일이 있으면 이어서 보낸다. 실패했을 때는 다음 주기까지 기다린다.
            if (error == null) Stamp();
            Notify();
        }
        public void Dispose()
        {
            disposed = true; store.Changed -= Stamp; soon.Stop(); soon.Dispose(); lifetime.Cancel(); lifetime.Dispose();
        }
    }

    sealed class GoogleTasksDialog : Form
    {
        bool working;
        public GoogleTasksDialog(GoogleTaskSync tasks)
        {
            Text="Google Tasks 연결";Font=new Font("Malgun Gothic",9f);ClientSize=new Size(500,250);
            FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterScreen;
            MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            bool builtIn=GoogleTaskSync.HasBuiltInClient;
            var intro=new Label{Left=20,Top=18,Width=460,Height=60,Text="할 일을 Google Tasks 목록 하나와 양쪽으로 맞춥니다.\n"+(builtIn?"로그인을 누르면 브라우저에서 Google 로그인 화면이 열립니다.":
                "Google Cloud에서 만든 '데스크톱 앱' OAuth 클라이언트의 JSON 파일을 고르면\n브라우저에서 로그인 화면이 열립니다.")};
            var state=new Label{Left=20,Top=88,Width=460,Height=60};
            var login=new Button{Left=20,Top=160,Width=200,Height=30,Text=builtIn?"Google 로그인":"클라이언트 JSON 선택 후 로그인"};
            var sync=new Button{Left=228,Top=160,Width=110,Height=30,Text="지금 동기화"};
            var disconnect=new Button{Left=346,Top=160,Width=110,Height=30,Text="연결 해제"};
            var close=new Button{Left=380,Top=208,Width=100,Text="닫기",DialogResult=DialogResult.Cancel};
            Controls.AddRange(new Control[]{intro,state,login,sync,disconnect,close});
            Action show=()=>{state.Text=(tasks.Connected?"연결된 목록: "+tasks.ListTitle:"연결된 목록 없음")+"\n"+tasks.Status+"\n중요 표시는 Google에 없어 이 PC에만 남습니다.";sync.Enabled=disconnect.Enabled=tasks.Connected&&!working;};
            Action<bool> busy=value=>{working=value;login.Enabled=close.Enabled=!value;show();};
            login.Click+=async(s,e)=>{
                string path=null;
                if(!builtIn)using(var file=new OpenFileDialog{Filter="OAuth 클라이언트 JSON|*.json"}){if(file.ShowDialog(this)!=DialogResult.OK)return;path=file.FileName;}
                busy(true);state.Text="브라우저에서 Google 로그인을 마치세요… (3분 안에)";
                try
                {
                    var lists=await tasks.LoginAsync(path);
                    var chosen=lists.Count==1?lists[0]:ChooseList(lists);
                    if(chosen.Key==null)tasks.Disconnect();
                    else{tasks.SelectList(chosen.Key,chosen.Value);state.Text="처음 동기화하는 중…";await tasks.SyncAsync();}
                }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"연결하지 못했습니다",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
                finally{busy(false);}
            };
            sync.Click+=async(s,e)=>{busy(true);try{await tasks.SyncAsync();}finally{busy(false);}};
            disconnect.Click+=(s,e)=>{if(MessageBox.Show(this,"연결을 해제할까요? 이 PC와 Google의 할 일은 지우지 않습니다.","Google Tasks",MessageBoxButtons.YesNo)==DialogResult.Yes)NativeDialogs.Safe(()=>{tasks.Disconnect();show();});};
            FormClosing+=(s,e)=>{if(working)e.Cancel=true;};CancelButton=close;show();
        }
        KeyValuePair<string,string> ChooseList(List<KeyValuePair<string,string>> lists)
        {
            using(var dialog=new Form{Text="작업 목록 선택",ClientSize=new Size(320,150),Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,ShowInTaskbar=false,MaximizeBox=false,MinimizeBox=false})
            {
                var list=new ComboBox{Left=24,Top=48,Width=272,DropDownStyle=ComboBoxStyle.DropDownList};
                foreach(var item in lists)list.Items.Add(item.Value);list.SelectedIndex=0;
                var save=new Button{Left=156,Top=100,Width=65,Text="연결",DialogResult=DialogResult.OK};
                var cancel=new Button{Left=231,Top=100,Width=65,Text="취소",DialogResult=DialogResult.Cancel};
                dialog.Controls.AddRange(new Control[]{new Label{Text="할 일을 맞출 Google Tasks 목록",Left=24,Top=20,Width=280},list,save,cancel});
                dialog.AcceptButton=save;dialog.CancelButton=cancel;
                return dialog.ShowDialog(this)==DialogResult.OK?lists[list.SelectedIndex]:new KeyValuePair<string,string>(null,null);
            }
        }
    }
}
