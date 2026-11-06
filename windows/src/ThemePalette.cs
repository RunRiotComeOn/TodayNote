using System;
using System.Collections.Generic;

namespace TodayNote
{
    public static class ThemePalette
    {
        // Each light tone is also its resource identity. All surfaces and controls
        // share these mutable brushes so a theme switch preserves focus and scroll.
        static readonly Dictionary<string, string> dark = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#303236", "#E7E9ED" }, { "#3D4045", "#DDE1E7" },
            { "#55585E", "#D2D7DE" }, { "#646971", "#BDC4CE" },
            { "#707278", "#B1B7C1" }, { "#858D87", "#98A69C" },
            { "#92959B", "#949BA6" }, { "#929A94", "#6D8174" },
            { "#959CA3", "#929BA6" }, { "#989DA4", "#9EA6B1" },
            { "#9B9EA4", "#9299A4" }, { "#A1A6AD", "#969EA9" },
            { "#A2A6AC", "#8D96A2" }, { "#A5A8AD", "#929AA5" },
            { "#A65757", "#E4A0A0" }, { "#A8ABB0", "#969EA9" },
            { "#ADB1B7", "#949CA7" }, { "#C6C9CE", "#555D69" },
            { "#C7CBD0", "#65707D" }, { "#CCD3DC", "#455264" },
            { "#DDDFE2", "#4F5763" }, { "#DFE1E4", "#434A56" },
            { "#E1E3E6", "#3B414B" }, { "#E2E3E5", "#3B414B" },
            { "#E2E4E7", "#626B78" }, { "#E3E5E8", "#3A414C" },
            { "#E8EAED", "#3C444F" }, { "#E9EBEE", "#353D48" },
            { "#EAEBED", "#373E49" }, { "#ECEDEF", "#343C47" },
            { "#ECEEF0", "#343A44" }, { "#EDEEF0", "#303640" },
            { "#EFF0F2", "#343B45" }, { "#F0F2F4", "#303741" },
            { "#F3F4F6", "#292E36" }, { "#F4F5F6", "#292E36" },
            { "#F5F6F8", "#272C34" }, { "#F7F8F9", "#272C34" },
            { "#FDFDFD", "#1E2228" }
        };

        public static IEnumerable<string> Colors { get { return dark.Keys; } }
        public static string Normalize(string mode)
        {
            mode = (mode ?? "").Trim().ToLowerInvariant();
            return mode == "dark" || mode == "system" ? mode : "light";
        }
        public static bool UseDark(string mode, bool systemUsesLight)
        { mode = Normalize(mode); return mode == "dark" || (mode == "system" && !systemUsesLight); }
        public static string Resolve(string light, bool useDark)
        { string color; return useDark && dark.TryGetValue(light, out color) ? color : light; }
    }
}
