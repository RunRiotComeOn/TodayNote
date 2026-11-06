using System;
using System.IO;
using System.Linq;
using TodayNote;

static class Tests
{
    static int passed;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    static DateTime At(string value) { return DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture); }
    static NoteState Fresh(string now = "2026-11-06 12:00") { var s = new NoteState(); s.Refresh(At(now)); return s; }
    static void Main()
    {
        Check(NoteState.DayAt(At("2026-11-06 00:30"), 240) == "2026-11-05", "after midnight belongs to previous workday");
        Check(NoteState.DayAt(At("2026-11-06 03:59:59"), 240) == "2026-11-05", "one second before boundary");
        Check(NoteState.DayAt(At("2026-11-06 04:00"), 240) == "2026-11-06", "exact boundary");
        Check(NoteState.DayAt(At("2026-01-01 01:00"), 240) == "2025-12-31", "year boundary");
        Check(NoteState.DayAt(At("2028-03-01 01:00"), 240) == "2028-02-29", "leap day");
        Check(NoteState.DayAt(At("2026-11-06 00:00"), 0) == "2026-11-06", "midnight cutoff");
        Check(NoteState.DayAt(At("2026-11-06 17:14"), 1035) == "2026-11-05", "custom minute cutoff before");
        Check(NoteState.DayAt(At("2026-11-06 17:15"), 1035) == "2026-11-06", "custom minute cutoff exact");
        var state = Fresh("2026-11-05 20:00");
        var done = state.Add("完成研究笔记 📝", 3, 1, At("2026-11-05 20:00"));
        var pending = state.Add("继续修改文书", 1, 3, At("2026-11-05 21:00"));
        Check(done.Urgency == 3 && done.Importance == 1 && pending.Urgency == 1 && pending.Importance == 3, "two independent three-level dimensions");
        Check(state.Toggle(done, At("2026-11-05 22:00")), "complete an item");
        Check(state.Today().Contains(done) && state.Today().Count() == 2, "completed item remains visible");
        state.Refresh(At("2026-11-06 00:45"));
        Check(state.Today().Contains(done) && state.Day == "2026-11-05", "completion stays visible after midnight");
        state.Refresh(At("2026-11-06 04:00"));
        Check(!state.Today().Contains(done) && state.Tasks.Contains(done), "archive completion without deleting it");
        Check(state.Today().Single() == pending, "carry unfinished item forward");
        Check(!state.Toggle(done, At("2026-11-06 04:00")) && done.Done, "stale checkbox cannot undo yesterday completion");
        state.Refresh(At("2026-11-15 12:00"));
        Check(state.Today().Single() == pending && state.Tasks.Count == 2, "closed for multiple days: no duplicates or loss");
        Check(!state.Refresh(At("2026-11-14 12:00")) && state.Day == "2026-11-15", "clock moves backwards safely");
        var midnight = Fresh("2026-11-05 23:50");
        var late = midnight.Add("睡前收尾", 2, 2, At("2026-11-06 01:00"));
        midnight.Toggle(late, At("2026-11-06 01:10"));
        Check(late.CompletedDay == "2026-11-05" && midnight.Today().Count() == 1, "late night completion uses workday date");
        midnight.Toggle(late, At("2026-11-06 01:11"));
        Check(!late.Done && late.CompletedAt == null, "undo completion clears both fields");
        midnight.Settings.RolloverMinutes = 60; midnight.Refresh(At("2026-11-06 01:12"));
        Check(midnight.Day == "2026-11-06" && midnight.Today().Single() == late, "earlier cutoff moves forward without losing pending work");
        midnight.Settings.RolloverMinutes = 240; midnight.Refresh(At("2026-11-06 01:13"));
        Check(midnight.Day == "2026-11-06", "later cutoff does not reopen an archived day");
        var order = Fresh();
        var first = order.Add("first", 1, 1, At("2026-11-06 12:00"));
        var high = order.Add("high", 3, 3, At("2026-11-06 12:00"));
        Check(order.Today().First() == first, "default order remains stable");
        order.Toggle(first, At("2026-11-06 12:01"));
        Check(order.Today().First() == first && order.Today().Count() == 2, "checking does not move or hide rows by default");
        order.Settings.PrioritySort = true;
        Check(order.Today().First() == high && order.Today().Last() == first, "optional priority sort places done items last");
        Check(first.Id != high.Id, "unique IDs");
        bool blank = false; try { order.Add("  ", 1, 1, DateTime.Now); } catch (ArgumentException) { blank = true; }
        Check(blank, "reject blank item");
        bool longTitle = false; try { order.Add(new string('a', 501), 1, 1, DateTime.Now); } catch (ArgumentException) { longTitle = true; }
        Check(longTitle, "reject overlong item");
        var clamped = order.Add("valid", -9, 77, At("2026-11-06 12:00"));
        Check(clamped.Urgency == 1 && clamped.Importance == 3, "normalize imported levels");
        Check(!ThemePalette.UseDark("light", true) && !ThemePalette.UseDark("light", false), "manual light overrides system mode");
        Check(ThemePalette.UseDark("dark", true) && ThemePalette.UseDark("dark", false), "manual dark overrides system mode");
        Check(ThemePalette.UseDark("system", false) && !ThemePalette.UseDark("system", true), "follow system responds to both system modes");
        Check(ThemePalette.Normalize(null) == "light" && ThemePalette.Normalize("unknown") == "light", "missing or invalid theme keeps original light appearance");
        Check(ThemePalette.Normalize(" DARK ") == "dark", "normalize imported theme choice");
        Check(ThemePalette.Colors.All(c => ThemePalette.Resolve(c, false) == c), "light palette preserves existing colors");
        Check(ThemePalette.Colors.All(c => ThemePalette.Resolve(c, true) != c), "every theme tone has a dark counterpart");
        string dir = Path.Combine(Path.GetTempPath(), "todaynote-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "data.json"); Storage.Write(path, state);
            var reopened = Storage.Read(path);
            Check(reopened.Tasks[0].Title == "完成研究笔记 📝" && reopened.Tasks[0].Done, "JSON roundtrip supports Chinese and emoji");
            Check(reopened.Day == state.Day && reopened.Settings.RolloverMinutes == 240, "reopen retains workday and settings");
            Check(reopened.Today().Count() == 1 && reopened.Today().First().Title == pending.Title, "reopen preserves archived and pending separation");
            reopened.Settings.Theme = "dark"; Storage.Write(path, reopened);
            Check(Storage.Read(path).Settings.Theme == "dark", "theme choice persists across reopen and backup");
            string legacy = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), "\"Theme\":\"dark\",?", "");
            legacy = legacy.Replace(",}", "}"); File.WriteAllText(Path.Combine(dir, "legacy.json"), legacy);
            var migrated = Storage.Read(Path.Combine(dir, "legacy.json"));
            Check(migrated.Settings.Theme == "light" && migrated.Tasks.Count == state.Tasks.Count && migrated.Tasks[0].Done,
                "old 1.0 data loads in light mode with tasks and completions intact");
            reopened.Settings.Theme = "system"; Storage.Write(path, reopened);
            Check(Storage.Read(path).Settings.Theme == "system", "follow-system choice persists");
            reopened.Add("new", 2, 2, At("2026-11-15 13:00")); Storage.Write(path, reopened);
            Check(Storage.Read(path + ".bak").Tasks.Count == 2 && Storage.Read(path).Tasks.Count == 3, "atomic replacement preserves last good backup");
            string previous = File.ReadAllText(path);
            reopened.Tasks[0].Title = "";
            bool invalid = false; try { Storage.Write(path, reopened); } catch (InvalidDataException) { invalid = true; }
            Check(invalid && File.ReadAllText(path) == previous, "failed validation cannot overwrite valid data");
            Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temporary files left behind");
            File.WriteAllText(Path.Combine(dir, "bad.json"), "{broken");
            bool bad = false; try { Storage.Read(Path.Combine(dir, "bad.json")); } catch { bad = true; }
            Check(bad, "corrupted import is rejected");
            var future = Fresh(); future.Version = 9;
            bool version = false; try { future.Validate(); } catch (InvalidDataException) { version = true; }
            Check(version, "unknown schema version is rejected");
            var dup = Fresh(); var t = dup.Add("one", 1, 1, DateTime.Now); dup.Tasks.Add(t);
            bool duplicate = false; try { dup.Validate(); } catch (InvalidDataException) { duplicate = true; }
            Check(duplicate, "duplicate imported IDs are rejected");
        }
        finally { Directory.Delete(dir, true); }
        Console.WriteLine("\n" + passed + " checks passed.");
    }
}
