// 컴시간알리미가 PC에 저장해 둔 시간표 캐시를 읽는다. 형식: docs/컴시간알리미-캐시-형식.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SchoolWidget
{
    public sealed class TimetableCell
    {
        public int Grade, Class, FromMonth, FromDay, FromPeriod;
        public string Subject = "";
        public bool Moved, Removed;
        public string ClassName { get { return Grade + "-" + Class; } }
    }
    public sealed class TimetableWeek
    {
        public const int Days = 5, MaxPeriods = 8;
        public DateTime Monday;
        public int Periods;
        // [요일 1~5, 교시 1~8]
        public readonly TimetableCell[,] Cells = new TimetableCell[Days + 1, MaxPeriods + 1];
    }
    public sealed class Timetable
    {
        public string Teacher;
        public DateTime Stamp;
        public readonly Dictionary<DateTime, TimetableWeek> Weeks = new Dictionary<DateTime, TimetableWeek>();
        // 달력이 일요일부터 시작하므로 일요일은 같은 줄에 있는 다음 날(월요일)의 주로 본다.
        public static DateTime MondayOf(DateTime day) { return day.Date.AddDays(1 - (int)day.DayOfWeek); }
        public TimetableWeek Week(DateTime day)
        {
            TimetableWeek week;
            return Weeks.TryGetValue(MondayOf(day), out week) ? week : null;
        }
    }

    public static class TimetableCache
    {
        const int Record = 252, WeekRecord = 1452;
        static readonly Encoding Korean = Encoding.GetEncoding(949);
        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "알림이", "dat", "tmp"); }
        }
        public static bool Exists(string folder) { return File.Exists(Path.Combine(folder, "교사명.kim")); }
        // 알리미가 약 20분마다 다시 쓰는 파일의 시각. 파일이 없으면 MinValue.
        public static DateTime Stamp(string folder)
        {
            try { var file = new FileInfo(Path.Combine(folder, "dayhour_t.kim")); return file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue; }
            catch (IOException) { return DateTime.MinValue; }
            catch (UnauthorizedAccessException) { return DateTime.MinValue; }
        }
        static byte[] Read(string folder, string name)
        {
            using (var stream = new FileStream(Path.Combine(folder, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var bytes = new byte[stream.Length];
                int done = 0;
                while (done < bytes.Length)
                {
                    int read = stream.Read(bytes, done, bytes.Length - done);
                    if (read <= 0) throw new EndOfStreamException(name);
                    done += read;
                }
                return bytes;
            }
        }
        // 칸 위치 = 요일 + 7 × 교시. 파일 끝을 넘으면 수업 없음.
        static int Cell(byte[] file, int record, int day, int period)
        {
            long offset = (long)record * Record + (day + 7 * period) * 4;
            return record < 0 || offset + 4 > file.Length ? 0 : BitConverter.ToInt32(file, (int)offset);
        }
        public static List<string> Teachers(string folder)
        {
            var names = new List<string>(Korean.GetString(Read(folder, "교사명.kim")).Split('^'));
            names.RemoveAt(0);
            while (names.Count > 0 && names[names.Count - 1].Trim().Length == 0) names.RemoveAt(names.Count - 1);
            return names;
        }
        static List<string> Subjects(string folder)
        {
            var names = new List<string> { "" };
            string[] items = Korean.GetString(Read(folder, "과목명.kim")).Split('$');
            for (int i = 1; i < items.Length; i++)
            {
                int cut = items[i].IndexOf('^');
                string name = cut < 0 ? items[i] : items[i].Substring(cut + 1);
                names.Add(name.Trim().Length == 0 && cut > 0 ? items[i].Substring(0, cut) : name.Trim());
            }
            return names;
        }
        // 읽는 도중 알리미가 파일을 바꾸면 null. 호출자는 이전 결과를 유지하고 다음에 다시 시도한다.
        public static Timetable Load(string folder, string teacher)
        {
            try
            {
                DateTime stamp = Stamp(folder);
                int number = Teachers(folder).IndexOf(teacher) + 1;
                if (number < 1 || stamp == DateTime.MinValue) return null;
                var subjects = Subjects(folder);
                byte[] dates = Read(folder, "주간일자.kim"), mine = Read(folder, "dayhour_t.kim"),
                       classes = Read(folder, "dayhour.kim"), basic = Read(folder, "기본교사시간표.kim");
                if (mine.Length % Record != 0 || classes.Length % Record != 0 || dates.Length % WeekRecord != 0 || Stamp(folder) != stamp) return null;
                var result = new Timetable { Teacher = teacher, Stamp = stamp };
                for (int at = 0; at + WeekRecord <= dates.Length; at += WeekRecord)
                {
                    int week = BitConverter.ToInt16(dates, at);
                    double serial = BitConverter.ToDouble(dates, at + 2);
                    if (week < 1 || serial < 30000 || serial > 80000) continue;
                    var item = new TimetableWeek { Monday = DateTime.FromOADate(serial).Date };
                    int record = (week - 1) * 400 + (number - 1) * 2;
                    bool any = false;
                    for (int day = 1; day <= TimetableWeek.Days && !any; day++)
                        for (int period = 1; period <= TimetableWeek.MaxPeriods; period++)
                            if (Cell(mine, record, day, period) != 0) { any = true; break; }
                    for (int day = 1; day <= TimetableWeek.Days; day++)
                    {
                        DateTime date = item.Monday.AddDays(day - 1);
                        for (int grade = 1; grade <= 3; grade++)
                            item.Periods = Math.Max(item.Periods, Math.Min(TimetableWeek.MaxPeriods, (int)BitConverter.ToInt16(dates, at + 10 + (grade + 4 * day) * 2)));
                        for (int period = 1; period <= TimetableWeek.MaxPeriods; period++)
                        {
                            int value = Cell(mine, record, day, period), usual = Cell(basic, number - 1, day, period);
                            if (value == 0)
                            {
                                // 휴업일이나 자료가 없는 주는 빠진 수업으로 표시하지 않는다.
                                int count = usual == 0 ? 0 : BitConverter.ToInt16(dates, at + 10 + (Math.Min(3, usual % 1000 / 100) + 4 * day) * 2);
                                if (any && usual != 0 && period <= count)
                                    item.Cells[day, period] = Lesson(usual, subjects, true);
                                continue;
                            }
                            var lesson = Lesson(value, subjects, false);
                            int from = Cell(classes, ((week - 1) * 3 + lesson.Grade - 1) * 60 + (lesson.Class - 1) * 2 + 1, day, period);
                            lesson.FromMonth = from / 1000; lesson.FromDay = from % 1000 / 10; lesson.FromPeriod = from % 10;
                            lesson.Moved = from != 0 && (lesson.FromMonth != date.Month || lesson.FromDay != date.Day || lesson.FromPeriod != period);
                            item.Cells[day, period] = lesson;
                            item.Periods = Math.Max(item.Periods, period);
                        }
                    }
                    result.Weeks[item.Monday] = item;
                }
                return result;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
        }
        static TimetableCell Lesson(int value, List<string> subjects, bool removed)
        {
            int subject = value / 1000;
            return new TimetableCell
            {
                Grade = value % 1000 / 100, Class = value % 100, Removed = removed,
                Subject = subject > 0 && subject < subjects.Count ? subjects[subject] : ""
            };
        }
    }
}
