using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DragShift.Core
{
    /// <summary>Plain "key = value" settings file (%APPDATA%\DragShift\settings.ini), easy to read and fix by hand.</summary>
    public sealed class Settings
    {
        public static class Keys
        {
            public const string Shift = "trigger.shift", Finger = "trigger.finger", Mouse = "trigger.mouse";
            public const string Hotkey = "hotkey.clipboard", Shelf = "shelf.side", Paused = "paused";
            public const string Favorites = "favorites", Hidden = "ring.hidden", LastActions = "last.actions";
            public const string OutputMode = "output.mode", OutputFolder = "output.folder", Quality = "quality";
            public const string Photo = "photo.spec", FitLast = "fit.last", Watermark = "watermark.text";
            public const string Sound = "feedback.sound", Reveal = "after.reveal", CopyText = "after.copyText";
            public const string WelcomeDone = "welcome.done";
            public const string Habits = "habits", UsualGlow = "ring.usual";
            /// <summary>How many files DragShift has made on this PC, for the About tab. It never leaves the PC.</summary>
            public const string Fixed = "files.fixed";
            public const string PrintPresets = "print.presets", PrintLast = "print.last", PrintQuick = "print.quick";
        }

        readonly string path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        public Settings(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        var t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#", StringComparison.Ordinal)) continue;
                        int eq = t.IndexOf('=');
                        if (eq > 0) values[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public event Action Changed;

        public string Get(string key, string fallback = "") => values.TryGetValue(key, out var v) ? v : fallback;

        public bool GetBool(string key, bool fallback) =>
            values.TryGetValue(key, out var v) ? v == "true" || v == "1" || v == "yes" : fallback;

        public int GetInt(string key, int fallback) =>
            values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        public double GetDouble(string key, double fallback) =>
            values.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

        /// <summary>Lists are stored comma-separated (ids never contain commas).</summary>
        public List<string> GetList(string key, IEnumerable<string> fallback) =>
            values.TryGetValue(key, out var v)
                ? v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList()
                : fallback.ToList();

        public void Set(string key, string value)
        {
            values[key] = (value ?? "").Replace("\r", " ").Replace("\n", " ");
            Save();
            Changed?.Invoke();
        }

        public void Set(string key, bool value) => Set(key, value ? "true" : "false");
        public void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
        public void Set(string key, double value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
        public void Set(string key, IEnumerable<string> list) => Set(key, string.Join(",", list));

        // Typed shortcuts the app uses everywhere.
        public long Fixed => long.TryParse(Get(Keys.Fixed, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;
        public void AddFixed(int made)
        {
            if (made <= 0) return;
            long now = Fixed;
            Set(Keys.Fixed, (now > long.MaxValue - made ? long.MaxValue : now + made).ToString(CultureInfo.InvariantCulture));
        }

        public bool Shift => GetBool(Keys.Shift, true);
        public bool Finger => GetBool(Keys.Finger, true);
        public bool Mouse => GetBool(Keys.Mouse, true);
        public bool Paused => GetBool(Keys.Paused, false);
        public List<string> Favorites => GetList(Keys.Favorites, Catalog.DefaultFavorites);
        public HashSet<string> Hidden => new HashSet<string>(GetList(Keys.Hidden, new string[0]));
        public PhotoSpec Photo => PhotoSpec.Parse(Get(Keys.Photo, new PhotoSpec().ToString()));
        public double Quality => Num.Clamp(GetDouble(Keys.Quality, 0.85), 0.4, 1);

        /// <summary>Adds or removes a favourite; true when it's now a favourite.</summary>
        public bool ToggleFavorite(string id)
        {
            var f = Favorites;
            bool added;
            if (f.Remove(id)) added = false; else { f.Add(id); added = true; }
            Set(Keys.Favorites, f);
            return added;
        }

        /// <summary>Last thing done to each kind of file, for "double-tap Shift = do it again".</summary>
        public string LastAction(FileKind kind) =>
            GetList(Keys.LastActions, new string[0]).Select(s => s.Split(':')).Where(p => p.Length == 2 && p[0] == kind.ToString())
                .Select(p => p[1]).FirstOrDefault();

        public void SetLastAction(FileKind kind, string id)
        {
            var list = GetList(Keys.LastActions, new string[0]).Where(s => !s.StartsWith(kind + ":", StringComparison.Ordinal)).ToList();
            list.Add(kind + ":" + id);
            Set(Keys.LastActions, list);
        }

        /// <summary>Saved print presets in the person's order (the top one is the quick one).</summary>
        public List<PrintPreset> PrintPresets
        {
            get
            {
                var list = values.ContainsKey(Keys.PrintPresets) ? PrintPreset.ParseList(Get(Keys.PrintPresets)) : null;
                return list != null && list.Count > 0 ? list : PrintPreset.BuiltIns.Select(p => p.Copy()).ToList();
            }
            set => Set(Keys.PrintPresets, PrintPreset.Join(value));
        }

        /// <summary>The presets in the Print list and on the ring's Print page: the ones not switched off (they share the
        /// ring's hidden set).</summary>
        public List<PrintPreset> ShownPrintPresets
        {
            get
            {
                var off = Hidden;
                return PrintPresets.Where(p => !off.Contains(p.StepId)).ToList();
            }
        }

        /// <summary>Shows or hides a preset in the Print list and on the ring.</summary>
        public void ShowPreset(PrintPreset p, bool on)
        {
            var off = Hidden;
            if (on) off.Remove(p.StepId); else off.Add(p.StepId);
            Set(Keys.Hidden, off.OrderBy(s => s, StringComparer.Ordinal));
        }

        /// <summary>What the print window sent last time ("Print again").</summary>
        public PrintPreset PrintLast
        {
            get => PrintPreset.Parse(Get(Keys.PrintLast));
            set => Set(Keys.PrintLast, value?.ToString() ?? "");
        }

        /// <summary>A ring step's preset: a saved one, or "last".</summary>
        public PrintPreset PrintPresetFor(string step)
        {
            var id = PrintPreset.IdFromStep(step);
            if (id == null) return null;
            return id == PrintPreset.LastId ? PrintLast : PrintPresets.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>A pick from the ring or the shelf, for "your usual".</summary>
        public void RecordPick(FileKind kind, string id)
        {
            var h = Habits.Parse(Get(Keys.Habits));
            h.Record(kind, id);
            Set(Keys.Habits, h.ToString());
        }

        /// <summary>The slice to glow for this kind of file, if there's a habit and the glow is on.</summary>
        public string Usual(FileKind kind) => GetBool(Keys.UsualGlow, true) ? Habits.Parse(Get(Keys.Habits)).Usual(kind) : null;

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder("# DragShift settings. Safe to edit while DragShift is closed.\r\n");
                foreach (var kv in values.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append(" = ").Append(kv.Value).Append("\r\n");
                Naming.Atomically(path, tmp => File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
