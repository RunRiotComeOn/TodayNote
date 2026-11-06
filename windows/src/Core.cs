using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TodayNote
{
    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public int RolloverMinutes = 240;
        [DataMember] public bool Topmost = true;
        [DataMember] public bool PrioritySort = false;
        [DataMember] public double Width = 400;
        [DataMember] public double Height = 640;
        [DataMember] public double Left = -99999;
        [DataMember] public double Top = -99999;
        [DataMember] public int FontSize = 14;
        [DataMember] public string Theme = "light";
    }

    [DataContract]
    public sealed class NoteTask
    {
        [DataMember] public string Id;
        [DataMember] public string Title;
        [DataMember] public int Urgency = 2;
        [DataMember] public int Importance = 2;
        [DataMember] public string CreatedAt;
        [DataMember] public string CompletedDay;
        [DataMember] public string CompletedAt;
        public bool Done { get { return !String.IsNullOrEmpty(CompletedDay); } }
    }

    [DataContract]
    public sealed class NoteState
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Day;
        [DataMember] public Preferences Settings = new Preferences();
        [DataMember] public List<NoteTask> Tasks = new List<NoteTask>();

        public static string DayAt(DateTime now, int cutoff)
        {
            // Compare wall-clock dates; DST must not turn a 04:00 boundary into 03:00.
            DateTime day = now.Date;
            if (now.TimeOfDay < TimeSpan.FromMinutes(cutoff)) day = day.AddDays(-1);
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public bool Refresh(DateTime now)
        {
            string next = DayAt(now, Settings.RolloverMinutes);
            // Never reopen an archived day after a clock/time-zone/cutoff adjustment.
            if (String.IsNullOrEmpty(Day) || String.CompareOrdinal(next, Day) > 0)
            {
                Day = next;
                return true;
            }
            return false;
        }

        public IEnumerable<NoteTask> Today()
        {
            IEnumerable<NoteTask> visible = Tasks.Where(t => !t.Done || t.CompletedDay == Day);
            if (Settings.PrioritySort)
                return visible.OrderBy(t => t.Done).ThenByDescending(t => t.Urgency + t.Importance).ThenByDescending(t => t.Importance);
            return visible;
        }

        public NoteTask Add(string title, int urgency, int importance, DateTime now)
        {
            Refresh(now);
            title = (title ?? "").Trim();
            if (title.Length == 0 || title.Length > 500) throw new ArgumentException("待办内容需要在 1–500 字之间。");
            var task = new NoteTask { Id = Guid.NewGuid().ToString("N"), Title = title,
                Urgency = Clamp(urgency, 1, 3), Importance = Clamp(importance, 1, 3),
                CreatedAt = now.ToString("o", CultureInfo.InvariantCulture) };
            Tasks.Add(task);
            return task;
        }

        public bool Toggle(NoteTask task, DateTime now)
        {
            Refresh(now);
            if (!Tasks.Contains(task) || (task.Done && task.CompletedDay != Day)) return false;
            bool wasDone = task.Done;
            task.CompletedDay = wasDone ? null : Day;
            task.CompletedAt = wasDone ? null : now.ToString("o", CultureInfo.InvariantCulture);
            return true;
        }

        static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        public void Validate()
        {
            if (Version != 1 || Settings == null || Tasks == null) throw new InvalidDataException("备份格式不受支持。");
            DateTime parsed;
            if (!String.IsNullOrEmpty(Day) && !DateTime.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                throw new InvalidDataException("备份中的日期无效。");
            if (Tasks.Count > 100000) throw new InvalidDataException("备份内容过大。");
            var ids = new HashSet<string>();
            foreach (var t in Tasks)
            {
                if (t == null || String.IsNullOrWhiteSpace(t.Id) || !ids.Add(t.Id) || String.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 500)
                    throw new InvalidDataException("备份中的待办内容无效。");
                t.Urgency = Clamp(t.Urgency, 1, 3); t.Importance = Clamp(t.Importance, 1, 3);
                if (t.Done && !DateTime.TryParseExact(t.CompletedDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    throw new InvalidDataException("备份中的完成日期无效。");
            }
            Settings.RolloverMinutes = Clamp(Settings.RolloverMinutes, 0, 1439);
            Settings.FontSize = Clamp(Settings.FontSize, 12, 18);
            Settings.Theme = ThemePalette.Normalize(Settings.Theme);
            Settings.Width = Double.IsNaN(Settings.Width) || Double.IsInfinity(Settings.Width) ? 400 : Math.Max(340, Math.Min(900, Settings.Width));
            Settings.Height = Double.IsNaN(Settings.Height) || Double.IsInfinity(Settings.Height) ? 640 : Math.Max(440, Math.Min(1200, Settings.Height));
            if (Double.IsNaN(Settings.Left) || Double.IsInfinity(Settings.Left)) Settings.Left = -99999;
            if (Double.IsNaN(Settings.Top) || Double.IsInfinity(Settings.Top)) Settings.Top = -99999;
        }
    }

    public static class Storage
    {
        public static NoteState Read(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("备份文件超过 16 MB。");
            using (var stream = File.OpenRead(path))
            {
                var state = (NoteState)new DataContractJsonSerializer(typeof(NoteState)).ReadObject(stream);
                if (state == null) throw new InvalidDataException("备份内容为空。");
                state.Validate();
                return state;
            }
        }

        public static void Write(string path, NoteState state)
        {
            state.Validate();
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, ".todaynote-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(NoteState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
