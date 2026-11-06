// Windows-only regression check: uses the real WPF resource system and visual tree.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using TodayNote;

static class ThemeSmokeTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Color ColorOf(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }
    static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
    [STAThread]
    static int Main()
    {
        Window window = null;
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))
                window = (Window)XamlReader.Load(stream);
            window.ShowInTaskbar = false;
            var sources = new Dictionary<string, ThemeColorSource>();
            foreach (string hex in ThemePalette.Colors)
            {
                var source = new ThemeColorSource(ColorOf(hex));
                Check(!source.Brush.CanFreeze, "A bound theme brush must not be freezable.");
                window.Resources["Tone_" + hex.Substring(1)] = source.Brush;
                sources.Add(hex, source);
                Check(!source.Brush.IsFrozen, "Resource insertion froze a theme brush.");
            }
            var sample = new Border { Background = sources["#F7F8F9"].Brush };
            ((StackPanel)window.FindName("TaskList")).Children.Add(sample);
            // Exercise control templates, including resource/style lookups.
            ((StackPanel)window.FindName("TaskList")).Children.Add(new ComboBox { ItemsSource = new[] { "Light", "Dark" }, SelectedIndex = 0 });
            ((StackPanel)window.FindName("TaskList")).Children.Add(new CheckBox { Content = "Theme smoke check", IsChecked = true });
            window.Show(); window.UpdateLayout(); Pump();
            for (int n = 0; n < 6; n++)
            {
                bool dark = n % 2 == 0;
                foreach (var pair in sources) pair.Value.Value = ColorOf(ThemePalette.Resolve(pair.Key, dark));
                Pump();
                foreach (var pair in sources)
                {
                    Check(!pair.Value.Brush.IsFrozen, "A displayed brush became frozen.");
                    Check(pair.Value.Brush.Color == ColorOf(ThemePalette.Resolve(pair.Key, dark)), "Theme binding did not update.");
                }
                Check(((SolidColorBrush)sample.Background).Color == ColorOf(ThemePalette.Resolve("#F7F8F9", dark)), "Programmatic task background did not update.");
                Check(((SolidColorBrush)window.Foreground).Color == ColorOf(ThemePalette.Resolve("#303236", dark)), "XAML dynamic resource did not update.");
            }
            Console.WriteLine("WPF startup and repeated theme switching passed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (window != null) window.Close(); }
    }
}
