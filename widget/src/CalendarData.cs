using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace SchoolWidget
{
    public sealed class SchoolEvent
    {
        public string id, s, e, t, c, g, n;
    }
    public sealed class PersonalEvent
    {
        public string id, start, end, title, category = "개인", grades = "전체", note = "", source = "개인 일정";
        public int schoolYear;
    }
    public sealed class TodoItem
    {
        public string id, title, due, doneAt, created, note = "";
        public bool important, done;
    }
    public sealed class CalendarPrefs
    {
        public int grade;
        public string tab = "events";
        public bool showDone;
        public string todoFilter = "all";
    }
    public sealed class CalendarDocument
    {
        public int formatVersion = 2;
        public List<PersonalEvent> personal = new List<PersonalEvent>();
        public List<TodoItem> todos = new List<TodoItem>();
        public Dictionary<string, string> memos = new Dictionary<string, string>();
        public CalendarPrefs prefs = new CalendarPrefs();
        public string savedAt;
    }
    public sealed class CalendarEntry
    {
        public string Id, Title, Category, Note, Grades;
        public DateTime Start, End;
        public PersonalEvent Personal;
        public bool IsPersonal { get { return Personal != null; } }
    }

    public sealed class CalendarStore
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        public readonly string FilePath;
        public CalendarDocument Data { get; private set; }
        public readonly List<SchoolEvent> School;
        public event Action Changed;

        public CalendarStore(string path)
        {
            FilePath = path;
            School = new List<SchoolEvent>();
            Data = new CalendarDocument();
            if (File.Exists(path)) Data = ReadDocument(File.ReadAllText(path, Encoding.UTF8));
        }
        public void ReplaceSchool(IEnumerable<SchoolEvent> events)
        {
            School.Clear(); School.AddRange(events);
            if (Changed != null) Changed();
        }
        public static string DateKey(DateTime day) { return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static bool TryDate(string text, out DateTime day)
        {
            return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }
        public static DateTime Date(string text)
        {
            DateTime day;
            if (!TryDate(text, out day)) throw new FormatException("올바르지 않은 날짜: " + text);
            return day;
        }
        public static CalendarDocument ReadDocument(string text)
        {
            var root = Json.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) throw new FormatException("백업 파일의 형식이 올바르지 않습니다.");
            object nested;
            if (root.TryGetValue("data", out nested) && nested is Dictionary<string, object>) root = (Dictionary<string, object>)nested;
            if (!root.ContainsKey("personal") && !root.ContainsKey("todos") && !root.ContainsKey("memos"))
                throw new FormatException("개인 일정·할 일·메모 데이터가 없는 파일입니다.");
            var document = Json.Deserialize<CalendarDocument>(Json.Serialize(root));
            document.personal = document.personal ?? new List<PersonalEvent>();
            document.todos = document.todos ?? new List<TodoItem>();
            document.memos = document.memos ?? new Dictionary<string, string>();
            document.prefs = document.prefs ?? new CalendarPrefs();
            if (document.prefs.grade < 0 || document.prefs.grade > 3) document.prefs.grade = 0;
            foreach (var p in document.personal)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.title) || Date(p.end) < Date(p.start))
                    throw new FormatException("개인 일정의 제목이나 날짜를 확인하세요.");
                if (string.IsNullOrEmpty(p.id)) p.id = Guid.NewGuid().ToString("N");
                p.note = p.note ?? "";
            }
            foreach (var t in document.todos)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.title)) throw new FormatException("할 일의 제목을 확인하세요.");
                if (string.IsNullOrEmpty(t.id)) t.id = Guid.NewGuid().ToString("N");
                if (!string.IsNullOrEmpty(t.due)) Date(t.due);
                t.note = t.note ?? "";
            }
            foreach (var day in document.memos.Keys) Date(day);
            document.formatVersion = 2;
            return document;
        }
        public void Save()
        {
            Data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, Json.Serialize(Data), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
            else File.Move(temp, FilePath);
            if (Changed != null) Changed();
        }
        public void Import(string text)
        {
            var incoming = ReadDocument(text);
            foreach (var p in incoming.personal)
            {
                Data.personal.RemoveAll(x => x.id == p.id);
                Data.personal.Add(p);
            }
            foreach (var t in incoming.todos)
            {
                Data.todos.RemoveAll(x => x.id == t.id);
                Data.todos.Add(t);
            }
            foreach (var pair in incoming.memos) Data.memos[pair.Key] = pair.Value;
            Save();
        }
        public string Export() { return Json.Serialize(Data); }
        public string Memo(DateTime day)
        {
            string text;
            return Data.memos.TryGetValue(DateKey(day), out text) ? text ?? "" : "";
        }
        public void SetMemo(DateTime day, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) Data.memos.Remove(DateKey(day));
            else Data.memos[DateKey(day)] = text;
            Save();
        }
        public List<CalendarEntry> Entries(DateTime day)
        {
            string key = DateKey(day);
            bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
            string grade = Data.prefs.grade.ToString(CultureInfo.InvariantCulture);
            var result = new List<CalendarEntry>();
            foreach (var e in School)
                if (string.CompareOrdinal(e.s, key) <= 0 && string.CompareOrdinal(e.e, key) >= 0 &&
                    (Data.prefs.grade == 0 || e.g == "전체" || (e.g ?? "").Split(',').Contains(grade)))
                    result.Add(new CalendarEntry { Id=e.id, Title=e.t, Category=e.c, Note=e.n, Grades=e.g, Start=Date(e.s), End=Date(e.e) });
            foreach (var p in Data.personal)
                if (string.CompareOrdinal(p.start,key)<=0 && string.CompareOrdinal(p.end,key)>=0)
                    result.Add(new CalendarEntry { Id=p.id, Title=p.title, Category="개인", Note=p.note, Grades="전체", Start=Date(p.start), End=Date(p.end), Personal=p });
            return result.OrderBy(e => e.Category == "수업 변경" ? 0 : e.Category == "휴업·공휴일" ? 1 : e.IsPersonal ? 3 : 2)
                .ThenBy(e => e.Title, StringComparer.CurrentCulture).ToList();
        }
        public List<TodoItem> Todos(DateTime day)
        {
            string key = DateKey(day);
            return Data.todos.Where(t => t.due == key && (!t.done || Data.prefs.showDone))
                .OrderBy(t => t.done).ThenByDescending(t => t.important).ToList();
        }
    }
}
