using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TodayNote
{
    public static class Program
    {
        public const string Version = "1.1.1";
        [STAThread]
        public static void Main()
        {
            string suffix = WindowsIdentity.GetCurrent().User.Value;
            bool first;
            using (var mutex = new Mutex(true, "Local\\TodayNote.Mutex." + suffix, out first))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TodayNote.Wake." + suffix))
            {
                if (!first) { wake.Set(); return; }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    MessageBox.Show("这一步没有完成。已有数据会保留。\n\n" + e.Exception.Message, "今日", MessageBoxButton.OK, MessageBoxImage.Information);
                    e.Handled = true;
                };
                MainView main = null;
                try { main = new MainView(); }
                catch (Exception e)
                {
                    MessageBox.Show("暂时无法打开今日。\n\n" + e.Message, "今日", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(wake,
                    delegate { app.Dispatcher.BeginInvoke(new Action(main.Reveal)); }, null, Timeout.Infinite, false);
                try { app.Run(main.Window); }
                finally { registration.Unregister(null); main.Dispose(); }
            }
        }
    }

    public sealed class MainView : IDisposable
    {
        public Window Window;
        NoteState state;
        readonly string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodayNote");
        string dataPath;
        Forms.NotifyIcon tray;
        System.Drawing.Icon appIcon;
        DispatcherTimer clock;
        StackPanel list;
        TextBox input;
        TextBlock date, count, status;
        Border fill;
        Grid track;
        Button pin, urgencyButton, importanceButton, undoButton;
        int urgency = 2, importance = 2;
        bool dirty, saveFailed, exiting;
        NoteTask deleted;
        int deletedIndex;
        DateTime statusUntil;
        string statusText;
        string restoreMessage;
        double completionRatio;
        string[] levels = { "", "低", "中", "高" };
        readonly Dictionary<string, ThemeColorSource> paint = new Dictionary<string, ThemeColorSource>(StringComparer.OrdinalIgnoreCase);
        bool? appliedDark;

        public MainView()
        {
            dataPath = System.IO.Path.Combine(folder, "data.json");
            Directory.CreateDirectory(folder);
            Load();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))
                Window = (Window)XamlReader.Load(stream);
            ApplyTheme();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TodayNote.ico"))
            {
                appIcon = new System.Drawing.Icon(stream);
                Window.Icon = ImagingIcon(appIcon);
            }
            Window.Width = state.Settings.Width;
            Window.Height = Math.Min(state.Settings.Height, SystemParameters.WorkArea.Height - 20);
            Window.MinHeight = Math.Min(440, SystemParameters.WorkArea.Height - 20);
            Window.Topmost = state.Settings.Topmost;
            PlaceWindow();
            list = Find<StackPanel>("TaskList"); input = Find<TextBox>("NewTitle");
            date = Find<TextBlock>("DateLabel"); count = Find<TextBlock>("CountLabel"); status = Find<TextBlock>("StatusLabel");
            fill = Find<Border>("ProgressFill"); track = Find<Grid>("ProgressTrack");
            pin = Find<Button>("PinButton"); undoButton = Find<Button>("UndoButton");
            urgencyButton = Find<Button>("UrgencyButton"); importanceButton = Find<Button>("ImportanceButton");
            Find<Grid>("DragHeader").MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { Window.DragMove(); RememberWindow(); }
            };
            Find<Button>("AddButton").Click += delegate { Add(); };
            Find<Button>("SettingsButton").Click += delegate { Settings(); };
            Find<Button>("HideButton").Click += delegate { Window.Hide(); };
            Find<Button>("HistoryButton").Click += delegate { History(); };
            pin.Click += delegate { TogglePin(); };
            input.TextChanged += delegate { Find<TextBlock>("Placeholder").Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            input.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Return) { Add(); e.Handled = true; } };
            input.GotKeyboardFocus += delegate { RefreshNow(); };
            Window.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { input.Focus(); e.Handled = true; }
            };
            Window.Activated += delegate { RefreshNow(); };
            Window.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; Window.Hide(); } };
            var grip = Find<Thumb>("ResizeGrip");
            grip.DragDelta += delegate(object s, DragDeltaEventArgs e)
            {
                Window.Width = Math.Max(Window.MinWidth, Math.Min(900, Window.Width + e.HorizontalChange));
                Window.Height = Math.Max(Window.MinHeight, Math.Min(1200, Window.Height + e.VerticalChange));
            };
            grip.DragCompleted += delegate { RememberWindow(); };
            track.SizeChanged += delegate { UpdateProgress(false); };
            status.MouseLeftButtonUp += delegate { if (saveFailed) Save(); };
            undoButton.Click += delegate
            {
                if (deleted == null) return;
                state.Tasks.Insert(Math.Min(deletedIndex, state.Tasks.Count), deleted);
                deleted = null; undoButton.Visibility = Visibility.Collapsed;
                Save(); Render(null); SetStatus("已恢复", 3);
            };
            BindLevelMenu(urgencyButton, true, null);
            BindLevelMenu(importanceButton, false, null);
            SetupTray();
            state.Refresh(DateTime.Now); Save(); Render(null);
            clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            clock.Tick += delegate { ApplyTheme(); RefreshNow(); UpdateStatus(); if (dirty) Save(false); };
            clock.Start();
            if (!String.IsNullOrEmpty(restoreMessage))
                Window.Loaded += delegate { MessageBox.Show(Window, restoreMessage, "今日", MessageBoxButton.OK, MessageBoxImage.Information); };
        }

        T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        SolidColorBrush Brush(string hex)
        {
            ThemeColorSource source;
            if (!paint.TryGetValue(hex, out source))
            {
                source = new ThemeColorSource((Color)ColorConverter.ConvertFromString(ThemePalette.Resolve(hex, appliedDark == true)));
                paint[hex] = source;
            }
            return source.Brush;
        }

        void ApplyTheme()
        {
            bool systemLight = true;
            if (state.Settings.Theme == "system")
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                        systemLight = key == null || Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1), CultureInfo.InvariantCulture) != 0;
                }
                catch { systemLight = true; }
            }
            bool useDark = ThemePalette.UseDark(state.Settings.Theme, systemLight);
            if (appliedDark == useDark) return;
            foreach (string hex in ThemePalette.Colors)
            {
                Color color = (Color)ColorConverter.ConvertFromString(ThemePalette.Resolve(hex, useDark));
                ThemeColorSource source;
                if (!paint.TryGetValue(hex, out source))
                {
                    source = new ThemeColorSource(color); paint[hex] = source;
                    Window.Resources["Tone_" + hex.Substring(1)] = source.Brush;
                }
                else source.Value = color;
            }
            appliedDark = useDark;
        }
        static BitmapSource ImagingIcon(System.Drawing.Icon icon)
        { return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }

        void Load()
        {
            if (!File.Exists(dataPath))
            {
                if (File.Exists(dataPath + ".bak"))
                {
                    state = Storage.Read(dataPath + ".bak");
                    restoreMessage = "已从本地备份恢复待办。";
                }
                else state = new NoteState();
                return;
            }
            try { state = Storage.Read(dataPath); }
            catch (Exception original)
            {
                if (!File.Exists(dataPath + ".bak"))
                    throw new InvalidDataException("本地数据无法读取，原文件已保留。\n可以将 data.json 复制给技术人员检查。\n\n" + dataPath + "\n" + original.Message);
                state = Storage.Read(dataPath + ".bak");
                File.Copy(dataPath, System.IO.Path.Combine(folder, "unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), false);
                // Keep the good backup intact during the first recovery write.
                File.Delete(dataPath);
                restoreMessage = "最近一次保存无法读取，已恢复上一次备份。原文件也已保留。";
            }
        }

        bool Save(bool showError = true)
        {
            dirty = true;
            try { Storage.Write(dataPath, state); dirty = false; saveFailed = false; UpdateStatus(); return true; }
            catch (Exception e)
            {
                bool alreadyFailed = saveFailed; saveFailed = true; UpdateStatus();
                if (showError && !alreadyFailed)
                    MessageBox.Show(Window, "暂时无法保存，请先不要退出软件。你可以在设置中导出备份。\n\n" + e.Message, "今日", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        void RefreshNow()
        {
            if (state.Refresh(DateTime.Now)) { deleted = null; undoButton.Visibility = Visibility.Collapsed; Save(); Render(null); }
        }

        void RememberWindow()
        {
            state.Settings.Width = Window.Width; state.Settings.Height = Window.Height;
            state.Settings.Left = Window.Left; state.Settings.Top = Window.Top; Save(false);
        }

        void PlaceWindow()
        {
            double x = state.Settings.Left, y = state.Settings.Top;
            double scale;
            using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96.0;
            bool intersects = Forms.Screen.AllScreens.Any(screen =>
            {
                // The manifest uses system DPI awareness; convert physical screen pixels to WPF DIPs.
                var area = screen.WorkingArea;
                return x >= area.Left / scale - Window.Width + 100 && x < area.Right / scale - 100 && y >= area.Top / scale - 20 && y < area.Bottom / scale - 60;
            });
            if (!intersects)
            {
                var area = SystemParameters.WorkArea;
                x = area.Right - Window.Width - 32; y = area.Top + 50;
            }
            Window.Left = x; Window.Top = y;
        }

        public void Reveal()
        {
            if (Window == null) return;
            Window.Dispatcher.Invoke(new Action(delegate
            {
                RefreshNow(); Window.Show(); Window.WindowState = WindowState.Normal;
                Window.Activate();
            }));
        }

        void SetupTray()
        {
            tray = new Forms.NotifyIcon { Icon = appIcon, Text = "今日 · TodayNote", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开今日", null, delegate { Reveal(); });
            menu.Items.Add("收起", null, delegate { Window.Hide(); });
            menu.Items.Add("切换置顶", null, delegate { TogglePin(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Exit(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Reveal(); };
        }

        void TogglePin()
        {
            state.Settings.Topmost = !state.Settings.Topmost;
            Window.Topmost = state.Settings.Topmost; Save(); UpdatePin();
        }

        void UpdatePin()
        {
            pin.Opacity = state.Settings.Topmost ? 1 : 0.45;
            pin.Background = state.Settings.Topmost ? Brush("#ECEDEF") : Brushes.Transparent;
            pin.ToolTip = state.Settings.Topmost ? "已置顶 · 点击取消" : "置顶";
            AutomationProperties.SetName(pin, state.Settings.Topmost ? "取消置顶" : "置顶");
        }

        void Render(string animatedId)
        {
            var tasks = state.Today().ToList();
            var day = DateTime.ParseExact(state.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            date.Text = day.ToString("M 月 d 日  ·  dddd", CultureInfo.GetCultureInfo("zh-CN"));
            int done = tasks.Count(t => t.Done);
            count.Text = done.ToString() + " / " + tasks.Count.ToString();
            count.ToolTip = "已完成 " + done + " 项，共 " + tasks.Count + " 项";
            completionRatio = tasks.Count == 0 ? 0 : (double)done / tasks.Count;
            UpdateProgress(animatedId != null);
            list.Children.Clear();
            foreach (NoteTask t in tasks) list.Children.Add(TaskRow(t, animatedId == t.Id));
            Find<StackPanel>("EmptyState").Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdatePin(); UpdateLevels(); UpdateStatus();
            if (animatedId != null && done == tasks.Count && done > 0) SetStatus("今天做得很好。", 8);
        }

        void UpdateProgress(bool animate)
        {
            double width = Math.Max(0, track.ActualWidth * completionRatio);
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            if (animate)
                fill.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(fill.ActualWidth, width, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
            fill.Width = width;
        }

        Border TaskRow(NoteTask task, bool animate)
        {
            var row = new Border { Background = Brush("#F7F8F9"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 12, 10, 12), Margin = new Thickness(0, 0, 0, 8) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); row.Child = grid;
            var circle = new Border { Width = 19, Height = 19, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(task.Done ? 0 : 1.2),
                BorderBrush = Brush("#C7CBD0"), Background = task.Done ? Brush("#929A94") : Brushes.Transparent };
            if (task.Done)
                circle.Child = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 4.8,9.5 L 8,12.5 L 14,6.5"), Stroke = Brushes.White, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var check = new Button { Content = circle, Padding = new Thickness(0), Width = 23, Height = 24, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-2, -1, 6, 0), ToolTip = task.Done ? "标记为未完成" : "完成" };
            AutomationProperties.SetName(check, (task.Done ? "取消完成：" : "完成：") + task.Title);
            check.Click += delegate
            {
                if (state.Toggle(task, DateTime.Now)) { Save(); Render(task.Done ? task.Id : null); }
                else { Save(); Render(null); }
            };
            grid.Children.Add(check);
            var body = new StackPanel(); Grid.SetColumn(body, 1); grid.Children.Add(body);
            var title = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, FontSize = state.Settings.FontSize, LineHeight = state.Settings.FontSize * 1.55,
                Foreground = Brush(task.Done ? "#A2A6AC" : "#3D4045"), TextDecorations = task.Done ? TextDecorations.Strikethrough : null };
            title.ToolTip = "双击编辑 · 右键查看更多";
            title.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) { Edit(task); e.Handled = true; } };
            body.Children.Add(title);
            var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-3, 6, 0, -2), Opacity = task.Done ? 0.50 : 1 };
            var u = Badge("紧急 · " + levels[task.Urgency], task.Urgency);
            var i = Badge("重要 · " + levels[task.Importance], task.Importance);
            BindLevelMenu(u, true, task); BindLevelMenu(i, false, task);
            tags.Children.Add(u); tags.Children.Add(i); body.Children.Add(tags);
            var menu = new ContextMenu { Resources = Window.Resources };
            var edit = new MenuItem { Header = "编辑" }; edit.Click += delegate { Edit(task); }; menu.Items.Add(edit);
            var remove = new MenuItem { Header = "删除" }; remove.Click += delegate
            {
                RefreshNow(); deleted = task; deletedIndex = state.Tasks.IndexOf(task); state.Tasks.Remove(task);
                SetStatus("已删除", 12); undoButton.Visibility = Visibility.Visible; Save(); Render(null);
            }; menu.Items.Add(remove); row.ContextMenu = menu;
            row.MouseEnter += delegate { row.Background = Brush("#F0F2F4"); };
            row.MouseLeave += delegate { row.Background = Brush("#F7F8F9"); };
            if (animate)
            {
                var scale = new ScaleTransform(0.7, 0.7); circle.RenderTransform = scale; circle.RenderTransformOrigin = new Point(0.5, 0.5);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(180)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(180)));
            }
            return row;
        }

        Button Badge(string text, int level)
        {
            return new Button { Content = text, FontSize = 10, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 5, 0),
                Background = Brush(level == 3 ? "#E3E5E8" : "#EDEEF0"), Foreground = Brush(level == 3 ? "#646971" : "#989DA4") };
        }

        void BindLevelMenu(Button button, bool isUrgency, NoteTask task)
        {
            button.Click += delegate
            {
                RefreshNow();
                var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Resources = Window.Resources };
                for (int n = 1; n <= 3; n++)
                {
                    int level = n;
                    var item = new MenuItem { Header = (isUrgency ? "紧急 · " : "重要 · ") + levels[n], IsCheckable = true,
                        IsChecked = n == (task == null ? (isUrgency ? urgency : importance) : (isUrgency ? task.Urgency : task.Importance)) };
                    item.Click += delegate
                    {
                        if (task == null) { if (isUrgency) urgency = level; else importance = level; UpdateLevels(); }
                        else { if (isUrgency) task.Urgency = level; else task.Importance = level; Save(); Render(null); }
                    };
                    menu.Items.Add(item);
                }
                button.ContextMenu = menu; menu.IsOpen = true;
            };
        }

        void UpdateLevels()
        { urgencyButton.Content = "紧急 · " + levels[urgency] + "  ⌄"; importanceButton.Content = "重要 · " + levels[importance] + "  ⌄"; }

        void Add()
        {
            if (String.IsNullOrWhiteSpace(input.Text)) { input.Focus(); return; }
            state.Add(input.Text, urgency, importance, DateTime.Now);
            if (Save()) SetStatus("已保存", 2);
            input.Clear(); Render(null); input.Focus();
            Find<ScrollViewer>("TaskScroll").ScrollToEnd();
        }

        void SetStatus(string text, int seconds)
        { statusText = text; statusUntil = DateTime.Now.AddSeconds(seconds); UpdateStatus(); }
        void UpdateStatus()
        {
            if (status == null) return;
            if (saveFailed) { status.Text = "未保存 · 点击重试"; status.Foreground = Brush("#A65757"); status.Cursor = Cursors.Hand; return; }
            status.Foreground = Brush("#A5A8AD"); status.Cursor = Cursors.Arrow;
            status.Text = DateTime.Now < statusUntil ? statusText : TimeSpan.FromMinutes(state.Settings.RolloverMinutes).ToString(@"hh\:mm") + " 换日";
            if (DateTime.Now >= statusUntil && undoButton != null) { undoButton.Visibility = Visibility.Collapsed; deleted = null; }
        }

        Window Dialog(string title, double width, double height, out StackPanel body)
        {
            var d = new Window { Title = title, Owner = Window, Width = width, Height = Math.Min(height, SystemParameters.WorkArea.Height - 30),
                WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = Window.Topmost,
                FontFamily = Window.FontFamily, FontSize = 13, Foreground = Brush("#3D4045"), Resources = Window.Resources };
            var shell = new Border { Background = Brush("#FDFDFD"), BorderBrush = Brush("#E1E3E6"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Margin = new Thickness(10), Padding = new Thickness(22, 15, 22, 17) };
            shell.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 15, ShadowDepth = 3, Opacity = 0.12 };
            d.Content = shell;
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); shell.Child = layout;
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 15), Background = Brushes.Transparent };
            var close = new Button { Content = "\uE8BB", Style = (Style)Window.Resources["IconButton"], ToolTip = "关闭" }; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close); close.Click += delegate { d.Close(); };
            header.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) d.DragMove(); };
            layout.Children.Add(header);
            body = new StackPanel();
            var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 3, 0) };
            Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
            d.PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) { d.Close(); e.Handled = true; } };
            return d;
        }

        void Edit(NoteTask task)
        {
            RefreshNow();
            if (!state.Today().Contains(task)) return;
            StackPanel body; var d = Dialog("编辑待办", 380, 360, out body);
            var box = new TextBox { Text = task.Title, MaxLength = 500, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 85, Padding = new Thickness(10),
                Background = Brush("#F3F4F6"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            body.Children.Add(box);
            var u = new ComboBox { ItemsSource = new[] { "低", "中", "高" }, SelectedIndex = task.Urgency - 1, Width = 110 };
            var i = new ComboBox { ItemsSource = new[] { "低", "中", "高" }, SelectedIndex = task.Importance - 1, Width = 110 };
            body.Children.Add(Labeled("紧急程度", u)); body.Children.Add(Labeled("重要程度", i));
            var save = new Button { Content = "保存", Background = Brush("#E9EBEE"), Margin = new Thickness(0, 15, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Width = 80 };
            save.Click += delegate
            {
                if (String.IsNullOrWhiteSpace(box.Text)) { box.Focus(); return; }
                task.Title = box.Text.Trim(); task.Urgency = u.SelectedIndex + 1; task.Importance = i.SelectedIndex + 1;
                RefreshNow(); Save(); Render(null); d.Close();
            }; body.Children.Add(save);
            d.Loaded += delegate { box.Focus(); box.SelectAll(); }; d.ShowDialog();
        }

        DockPanel Labeled(string label, FrameworkElement value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
            value.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); return row;
        }

        CheckBox Switch(string label, bool value)
        { return new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 11, 0, 4), VerticalContentAlignment = VerticalAlignment.Center }; }

        void Settings()
        {
            RefreshNow();
            StackPanel body; var d = Dialog("设置", 380, 660, out body);
            var appearance = new ComboBox { ItemsSource = new[] { "浅色", "深色", "跟随系统" },
                SelectedIndex = state.Settings.Theme == "dark" ? 1 : state.Settings.Theme == "system" ? 2 : 0, Width = 110 };
            body.Children.Add(Labeled("外观", appearance));
            appearance.SelectionChanged += delegate
            {
                state.Settings.Theme = appearance.SelectedIndex == 1 ? "dark" : appearance.SelectedIndex == 2 ? "system" : "light";
                ApplyTheme(); Save();
            };
            var top = Switch("窗口置顶", state.Settings.Topmost); body.Children.Add(top);
            top.Click += delegate { state.Settings.Topmost = top.IsChecked == true; Window.Topmost = state.Settings.Topmost; d.Topmost = Window.Topmost; Save(); UpdatePin(); };
            var start = Switch("开机启动", IsStartup()); body.Children.Add(start);
            start.Click += delegate
            {
                try { SetStartup(start.IsChecked == true); }
                catch (Exception e) { start.IsChecked = IsStartup(); MessageBox.Show(d, "无法更改开机启动。\n" + e.Message, "今日"); }
            };
            var sort = Switch("按优先级排列", state.Settings.PrioritySort); body.Children.Add(sort);
            sort.Click += delegate { state.Settings.PrioritySort = sort.IsChecked == true; Save(); Render(null); };
            body.Children.Add(new Border { Height = 1, Background = Brush("#ECEEF0"), Margin = new Thickness(0, 15, 0, 6) });
            var times = Enumerable.Range(0, 48).Select(n => TimeSpan.FromMinutes(n * 30).ToString(@"hh\:mm")).ToList();
            string configured = TimeSpan.FromMinutes(state.Settings.RolloverMinutes).ToString(@"hh\:mm");
            if (!times.Contains(configured)) { times.Add(configured); times.Sort(StringComparer.Ordinal); }
            var cutoff = new ComboBox { ItemsSource = times, SelectedItem = configured, Width = 105 };
            body.Children.Add(Labeled("每天换日时间", cutoff));
            cutoff.SelectionChanged += delegate
            {
                if (cutoff.SelectedItem == null) return;
                state.Settings.RolloverMinutes = (int)TimeSpan.ParseExact((string)cutoff.SelectedItem, @"hh\:mm", CultureInfo.InvariantCulture).TotalMinutes;
                RefreshNow(); Save(); Render(null);
            };
            body.Children.Add(new TextBlock { Text = "到点后，未完成的继续保留。\n完成的收进记录，关机后也会补做换日。", Foreground = Brush("#989DA4"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 2), LineHeight = 18 });
            var fonts = new ComboBox { ItemsSource = new[] { "小", "标准", "大" }, SelectedIndex = state.Settings.FontSize <= 12 ? 0 : state.Settings.FontSize >= 16 ? 2 : 1, Width = 105 };
            body.Children.Add(Labeled("字号", fonts));
            fonts.SelectionChanged += delegate { state.Settings.FontSize = fonts.SelectedIndex == 0 ? 12 : fonts.SelectedIndex == 2 ? 16 : 14; Save(); Render(null); };
            body.Children.Add(new Border { Height = 1, Background = Brush("#ECEEF0"), Margin = new Thickness(0, 18, 0, 10) });
            var backup = new StackPanel { Orientation = Orientation.Horizontal };
            var export = new Button { Content = "导出备份", Background = Brush("#F0F2F4"), Margin = new Thickness(0, 0, 8, 0) }; backup.Children.Add(export);
            var import = new Button { Content = "恢复备份", Background = Brush("#F0F2F4") }; backup.Children.Add(import); body.Children.Add(backup);
            export.Click += delegate { Export(d); };
            import.Click += delegate { if (Import(d)) d.Close(); };
            var shortcut = new Button { Content = "创建桌面快捷方式", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 8, 0, 8), Margin = new Thickness(0, 6, 0, 0), FontSize = 12 };
            shortcut.Click += delegate
            {
                try { CreateShortcut(); MessageBox.Show(d, "桌面快捷方式已创建。", "今日"); }
                catch (Exception e) { MessageBox.Show(d, "无法创建快捷方式。\n" + e.Message, "今日"); }
            }; body.Children.Add(shortcut);
            var bottom = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
            var exit = new Button { Content = "退出软件", Padding = new Thickness(8, 5, 8, 5), FontSize = 12 }; DockPanel.SetDock(exit, Dock.Right); bottom.Children.Add(exit);
            bottom.Children.Add(new TextBlock { Text = "今日  " + Program.Version + "\n本地保存 · 无需联网", Foreground = Brush("#ADB1B7"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, LineHeight = 16 });
            body.Children.Add(bottom); exit.Click += delegate { d.Close(); Exit(); };
            d.ShowDialog();
        }

        bool IsStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("TodayNote") != null;
        }
        void SetStartup(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (on) key.SetValue("TodayNote", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else key.DeleteValue("TodayNote", false);
            }
        }
        void CreateShortcut()
        {
            object shell = null, shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell"); shell = Activator.CreateInstance(type);
                string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "今日.lnk");
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Assembly.GetExecutingAssembly().Location });
                shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "今日 · 待办便利贴" });
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        void Export(Window owner)
        {
            RefreshNow();
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "今日备份 (*.json)|*.json", FileName = "TodayNote-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json" };
            if (dialog.ShowDialog(owner) != true) return;
            try { Storage.Write(dialog.FileName, state); MessageBox.Show(owner, "备份已导出。", "今日"); }
            catch (Exception e) { MessageBox.Show(owner, "导出失败。\n" + e.Message, "今日"); }
        }

        bool Import(Window owner)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "今日备份 (*.json)|*.json" };
            if (dialog.ShowDialog(owner) != true) return false;
            try
            {
                var imported = Storage.Read(dialog.FileName);
                if (MessageBox.Show(owner, "用这份备份替换当前待办和设置？\n当前内容会先另存一份，以便恢复。", "恢复备份", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return false;
                Storage.Write(System.IO.Path.Combine(folder, "before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), state);
                // Commit the replacement before updating the visible state.
                imported.Refresh(DateTime.Now); Storage.Write(dataPath, imported); state = imported;
                dirty = false; saveFailed = false; deleted = null; undoButton.Visibility = Visibility.Collapsed;
                Window.Topmost = state.Settings.Topmost; ApplyTheme(); Render(null); SetStatus("备份已恢复", 6); return true;
            }
            catch (Exception e) { MessageBox.Show(owner, "恢复失败，当前待办没有被替换。\n\n" + e.Message, "今日"); return false; }
        }

        void History()
        {
            RefreshNow();
            StackPanel body; var d = Dialog("完成记录", 390, 540, out body);
            var days = state.Tasks.Where(t => t.Done).Select(t => t.CompletedDay).Distinct().OrderByDescending(x => x).ToList();
            if (days.Count == 0)
                body.Children.Add(new TextBlock { Text = "每完成一件小事，都会记在这里。", Foreground = Brush("#A1A6AD"), Margin = new Thickness(0, 50, 0, 0), TextWrapping = TextWrapping.Wrap });
            else
            {
                var select = new ComboBox { ItemsSource = days, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 16) }; body.Children.Add(select);
                var records = new StackPanel(); body.Children.Add(records);
                Action render = delegate
                {
                    records.Children.Clear();
                    foreach (var t in state.Tasks.Where(t => t.CompletedDay == (string)select.SelectedItem))
                    {
                        var b = new Border { Background = Brush("#F5F6F8"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8) };
                        b.Child = new TextBlock { Text = "✓   " + t.Title, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#959CA3"), TextDecorations = TextDecorations.Strikethrough, LineHeight = 22 };
                        records.Children.Add(b);
                    }
                };
                select.SelectionChanged += delegate { render(); }; render();
            }
            d.ShowDialog();
        }

        void Exit()
        {
            RememberWindow();
            if (dirty && !Save())
            {
                if (MessageBox.Show(Window, "还有内容没有成功保存，退出会丢失未保存的修改。仍要退出吗？", "今日", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }
            exiting = true; Application.Current.Shutdown();
        }

        public void Dispose()
        {
            if (clock != null) clock.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (appIcon != null) appIcon.Dispose();
        }
    }
}
