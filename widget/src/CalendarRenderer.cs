using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Linq;

namespace SchoolWidget
{
    public sealed class CalendarLayout
    {
        public readonly Size Size;
        public readonly DateTime Month, First;
        public readonly Rectangle Prev, Next, Today, Details, Menu, Move;
        public readonly Rectangle TodoPanel, TodoAdd, TodoAll, TodoToday, TodoImportant, TodoDone, TodoPrevious, TodoNext, Source;
        public readonly int CalendarWidth, CalendarBottom, TodoRowHeight = 45;
        public const int GridTop = 64, Margin = 6;
        public CalendarLayout(Size size, DateTime month)
        {
            Size = size; Month = new DateTime(month.Year, month.Month, 1);
            First = Month.AddDays(-(int)Month.DayOfWeek);
            bool side = size.Width >= 760;
            int panelWidth = Math.Max(220, Math.Min(290, size.Width / 3));
            int panelHeight = Math.Max(190, Math.Min(240, size.Height / 3));
            CalendarWidth = side ? size.Width - panelWidth - 6 : size.Width;
            CalendarBottom = side ? size.Height : size.Height - panelHeight - 6;
            TodoPanel = side ? new Rectangle(CalendarWidth + 2, 6, size.Width - CalendarWidth - 8, size.Height - 12) :
                new Rectangle(6, CalendarBottom + 2, size.Width - 12, size.Height - CalendarBottom - 8);
            TodoAdd = new Rectangle(TodoPanel.Right - 56, TodoPanel.Y + 8, 48, 25);
            int filterWidth = (TodoPanel.Width - 16) / 3;
            TodoAll = new Rectangle(TodoPanel.X+8,TodoPanel.Y+39,filterWidth,24);
            TodoToday = new Rectangle(TodoAll.Right,TodoAll.Y,filterWidth,24);
            TodoImportant = new Rectangle(TodoToday.Right,TodoAll.Y,filterWidth,24);
            Source = new Rectangle(TodoPanel.X+8,TodoPanel.Bottom-29,TodoPanel.Width-16,23);
            TodoDone = new Rectangle(TodoPanel.X+8,Source.Y-28,TodoPanel.Width-84,24);
            TodoPrevious = new Rectangle(TodoPanel.Right-70,TodoDone.Y,28,24);
            TodoNext = new Rectangle(TodoPanel.Right-38,TodoDone.Y,28,24);
            int center = CalendarWidth / 2;
            Move = new Rectangle(6, 6, Math.Max(58, center - 102), 28);
            Prev = new Rectangle(center - 96, 7, 26, 26);
            Today = new Rectangle(center - 66, 6, 132, 28);
            Next = new Rectangle(center + 70, 7, 26, 26);
            Details = new Rectangle(CalendarWidth - 74, 7, 31, 26);
            Menu = new Rectangle(CalendarWidth - 39, 7, 31, 26);
        }
        public int TodoCapacity { get { return Math.Max(1,(TodoDone.Y-TodoPanel.Y-70)/TodoRowHeight); } }
        public Rectangle TodoRow(int index) { return new Rectangle(TodoPanel.X+8,TodoPanel.Y+69+index*TodoRowHeight,TodoPanel.Width-16,TodoRowHeight-2); }
        public int TodoAt(Point p) { for(int i=0;i<TodoCapacity;i++)if(TodoRow(i).Contains(p))return i;return -1; }
        public Rectangle Cell(int index)
        {
            int width = CalendarWidth - Margin * 2, height = CalendarBottom - GridTop - Margin;
            int c = index % 7, r = index / 7;
            int x = Margin + c * width / 7, y = GridTop + r * height / 6;
            return Rectangle.FromLTRB(x, y, Margin + (c + 1) * width / 7 - 2,
                GridTop + (r + 1) * height / 6 - 2);
        }
        public int CellAt(Point point)
        {
            if (point.Y < GridTop) return -1;
            for (int i = 0; i < 42; i++) if (Cell(i).Contains(point)) return i;
            return -1;
        }
        public int Index(DateTime day) { return (day.Date - First).Days; }
    }

    static class CalendarRenderer
    {
        public static List<TodoItem> VisibleTodos(CalendarStore store)
        {
            string today=CalendarStore.DateKey(DateTime.Today),filter=store.Data.prefs.todoFilter;
            return store.Data.todos.Where(t=>(!t.done||store.Data.prefs.showDone)&&
                (filter!="today"||!string.IsNullOrEmpty(t.due)&&string.CompareOrdinal(t.due,today)<=0)&&
                (filter!="important"||t.important)).OrderBy(t=>t.done).ThenByDescending(t=>t.important)
                .ThenBy(t=>string.IsNullOrEmpty(t.due)?"9999":t.due,StringComparer.Ordinal).ThenBy(t=>t.created).ToList();
        }
        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "수업 변경": return Color.FromArgb(205, 182, 250);
                case "평가": return Color.FromArgb(255, 209, 125);
                case "교직원": return Color.FromArgb(149, 205, 255);
                case "휴업·공휴일": return Color.FromArgb(255, 167, 173);
                case "창체": return Color.FromArgb(177, 224, 158);
                case "개인": return Color.FromArgb(255, 197, 225);
                default: return Color.FromArgb(159, 229, 215);
            }
        }
        static void Text(Graphics g, string text, Font font, Color color, Rectangle rect, bool centered, bool wrap)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var format = new StringFormat())
            {
                format.Alignment = centered ? StringAlignment.Center : StringAlignment.Near;
                format.LineAlignment = centered ? StringAlignment.Center : StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisCharacter;
                if (!wrap) format.FormatFlags |= StringFormatFlags.NoWrap;
                var shadow = rect; shadow.Offset(1, 1);
                using (var brush = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.DrawString(text ?? "", font, brush, shadow, format);
                using (var brush = new SolidBrush(color)) g.DrawString(text ?? "", font, brush, rect, format);
            }
        }
        public static Bitmap Render(CalendarStore store, Config cfg, CalendarLayout layout, DateTime selected, int hover, int todoOffset=0, string source="일정 서버 연결")
        {
            var image = new Bitmap(layout.Size.Width, layout.Size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(image))
            using (var body = new Font("Malgun Gothic", cfg.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font("Malgun Gothic", cfg.FontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var month = new Font("Malgun Gothic", Math.Min(18, cfg.FontSize + 3), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var fill = new SolidBrush(Color.FromArgb(Math.Min(210, cfg.CellAlpha + 45), 20, 30, 39)))
                    g.FillRectangle(fill, new Rectangle(6, 5, layout.CalendarWidth - 12, 30));
                Text(g, cfg.Locked ? "학사일정 · 고정" : "학사일정", bold, Color.White, layout.Move, false, false);
                Text(g, "‹", month, Color.White, layout.Prev, true, false);
                Text(g, layout.Month.ToString("yyyy년 M월"), month, Color.White, layout.Today, true, false);
                Text(g, "›", month, Color.White, layout.Next, true, false);
                Text(g, "목록", body, Color.White, layout.Details, true, false);
                Text(g, "···", month, Color.White, layout.Menu, true, false);
                string[] weekdays = { "일", "월", "화", "수", "목", "금", "토" };
                for (int c = 0; c < 7; c++)
                {
                    Rectangle cell = layout.Cell(c);
                    Text(g, weekdays[c], bold, c == 0 ? CategoryColor("휴업·공휴일") : c == 6 ? CategoryColor("교직원") : Color.White,
                        new Rectangle(cell.X, 39, cell.Width, 22), true, false);
                }
                int line = Math.Max(15, cfg.FontSize + 5);
                for (int i = 0; i < 42; i++)
                {
                    DateTime day = layout.First.AddDays(i);
                    Rectangle cell = layout.Cell(i);
                    var cellState = g.Save();
                    g.SetClip(cell, CombineMode.Intersect);
                    bool outside = day.Month != layout.Month.Month;
                    int alpha = outside ? cfg.CellAlpha * 2 / 3 : cfg.CellAlpha;
                    Color fillColor = day == selected.Date ? Color.FromArgb(Math.Min(220, alpha + 45), 32, 86, 82) : Color.FromArgb(alpha, 20, 29, 37);
                    using (var fill = new SolidBrush(fillColor)) g.FillRectangle(fill, cell);
                    using (var pen = new Pen(Color.FromArgb(i == hover ? 170 : 45, 235, 245, 250)))
                        g.DrawRectangle(pen, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                    Color number = day.DayOfWeek == DayOfWeek.Sunday ? CategoryColor("휴업·공휴일") : day.DayOfWeek == DayOfWeek.Saturday ? CategoryColor("교직원") : Color.White;
                    if (outside) number = Color.FromArgb(155, number);
                    if (day.Date == DateTime.Today)
                    {
                        using (var mark = new SolidBrush(Color.FromArgb(230, 23, 135, 120))) g.FillEllipse(mark, cell.X + 5, cell.Y + 4, 25, 23);
                    }
                    Text(g, day.Day.ToString(), bold, number, new Rectangle(cell.X + 6, cell.Y + 5, cell.Width - 12, line + 3), false, false);
                    var entries = store.Entries(day);
                    var todos = store.Todos(day);
                    int top = cell.Y + line + 12;
                    int shown = Math.Min(entries.Count, Math.Max(0, Math.Min(3, (cell.Height - line - 20) / (line + 4))));
                    for (int n = 0; n < shown; n++)
                    {
                        Color color = CategoryColor(entries[n].Category);
                        using (var stripe = new SolidBrush(color)) g.FillRectangle(stripe, cell.X + 6, top + 2, 2, line - 2);
                        Text(g, entries[n].Title, body, color, new Rectangle(cell.X + 12, top, cell.Width - 17, line), false, false);
                        top += line + 3;
                    }
                    if (entries.Count > shown)
                        Text(g, "+" + (entries.Count - shown), body, Color.Silver, new Rectangle(cell.Right - 32, cell.Y + 5, 28, line), true, false);
                    foreach (var todo in todos)
                    {
                        if (top + line > cell.Bottom - 4) break;
                        Text(g, (todo.done ? "☑ " : "☐ ") + todo.title, body,
                            todo.done ? Color.Silver : todo.important ? CategoryColor("평가") : Color.White,
                            new Rectangle(cell.X + 6, top, cell.Width - 12, line), false, false);
                        top += line + 3;
                    }
                    string memo = store.Memo(day);
                    if (!string.IsNullOrEmpty(memo))
                        Text(g, memo, body, outside ? Color.Silver : Color.White,
                            new Rectangle(cell.X + 6, top + 2, cell.Width - 12, cell.Bottom - top - 6), false, true);
                    g.Restore(cellState);
                }
                using(var fill=new SolidBrush(Color.FromArgb(Math.Min(225,cfg.CellAlpha+65),20,30,39)))g.FillRectangle(fill,layout.TodoPanel);
                int open=store.Data.todos.Count(t=>!t.done);
                Text(g,"할 일 · "+open,bold,Color.White,new Rectangle(layout.TodoPanel.X+10,layout.TodoPanel.Y+10,layout.TodoPanel.Width-73,24),false,false);
                Button(g,"+ 추가",body,layout.TodoAdd,false);
                Button(g,"전체",body,layout.TodoAll,store.Data.prefs.todoFilter!="today"&&store.Data.prefs.todoFilter!="important");
                Button(g,"오늘까지",body,layout.TodoToday,store.Data.prefs.todoFilter=="today");
                Button(g,"중요",body,layout.TodoImportant,store.Data.prefs.todoFilter=="important");
                var visible=VisibleTodos(store);
                for(int i=0;i<layout.TodoCapacity&&todoOffset+i<visible.Count;i++)
                {
                    var todo=visible[todoOffset+i];var row=layout.TodoRow(i);
                    using(var pen=new Pen(Color.FromArgb(65,225,235,240)))g.DrawLine(pen,row.X,row.Bottom,row.Right,row.Bottom);
                    Text(g,todo.done?"☑":"☐",bold,todo.done?Color.Silver:Color.White,new Rectangle(row.X,row.Y+3,24,24),true,false);
                    Color color=todo.done?Color.Silver:todo.important?CategoryColor("평가"):Color.White;
                    Text(g,(todo.important?"★ ":"")+todo.title,body,color,new Rectangle(row.X+27,row.Y+2,row.Width-30,22),false,false);
                    bool overdue=!todo.done&&!string.IsNullOrEmpty(todo.due)&&string.CompareOrdinal(todo.due,CalendarStore.DateKey(DateTime.Today))<0;
                    Text(g,string.IsNullOrEmpty(todo.due)?"기한 없음":todo.due+(overdue?" · 기한 지남":""),body,overdue?CategoryColor("휴업·공휴일"):Color.Silver,new Rectangle(row.X+27,row.Y+24,row.Width-30,19),false,false);
                }
                if(visible.Count==0)Text(g,"+ 추가로 할 일을 적으세요",body,Color.Silver,layout.TodoRow(0),true,false);
                Button(g,store.Data.prefs.showDone?"☑ 완료 포함":"☐ 완료 포함",body,layout.TodoDone,false);
                Button(g,"‹",body,layout.TodoPrevious,false);Button(g,"›",body,layout.TodoNext,false);
                Button(g,source,body,layout.Source,false);
            }
            return image;
        }
        static void Button(Graphics g,string text,Font font,Rectangle rect,bool active)
        {
            using(var fill=new SolidBrush(active?Color.FromArgb(160,26,129,112):Color.FromArgb(45,200,220,230)))g.FillRectangle(fill,rect);
            Text(g,text,font,Color.White,rect,true,false);
        }
    }
}
