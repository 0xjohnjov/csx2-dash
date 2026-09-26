// Settings page shown in SimHub's left menu ("CSX2 Dash"). Built in code (no XAML) so it compiles with csc.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CSX2Dash
{
    public class SettingsView : UserControl
    {
        readonly Csx2DashPlugin p;
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
        readonly TextBlock info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 12, 0, 0) };
        readonly ComboBox page = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };

        public SettingsView(Csx2DashPlugin plugin)
        {
            p = plugin;
            var s = p.Settings;
            var root = new StackPanel { Margin = new Thickness(16) };

            root.Children.Add(new TextBlock { Text = "CSX2 Dash", FontSize = 20, Margin = new Thickness(0, 0, 0, 8) });
            root.Children.Add(status);

            var enabled = new CheckBox { Content = "Drive the CSX2 screen and LEDs from SimHub", IsChecked = s.Enabled, Margin = new Thickness(0, 4, 0, 4) };
            enabled.Click += (a, b) => { s.Enabled = enabled.IsChecked == true; p.SettingsChanged(); };
            root.Children.Add(enabled);

            root.Children.Add(Label("Dash page (your UGT layouts). Map wheel buttons to the actions 'CSX2Dash.NextPage' / 'CSX2Dash.PreviousPage' in Controls and events."));
            FillPages();
            page.SelectionChanged += (a, b) => { var l = page.SelectedItem as Layout; if (l != null && l.Id != s.PageLayoutId) { s.PageLayoutId = l.Id; p.SettingsChanged(); } };
            root.Children.Add(page);

            root.Children.Add(Slider("Screen brightness", 1, 16, s.LcdBrightness, v => { s.LcdBrightness = v; p.SettingsChanged(); }));
            root.Children.Add(Slider("LED brightness", 1, 128, s.LedBrightness, v => { s.LedBrightness = v; p.SettingsChanged(); }));

            var flash = new CheckBox { Content = "Flash the last 5 rev LEDs at the shift point", IsChecked = s.ShiftFlash, Margin = new Thickness(0, 8, 0, 4) };
            flash.Click += (a, b) => { s.ShiftFlash = flash.IsChecked == true; p.SettingsChanged(); };
            root.Children.Add(flash);

            var flags = new CheckBox { Content = "Blink side LEDs for yellow / blue flags (red LEDs for red flag)", IsChecked = s.FlagLeds, Margin = new Thickness(0, 4, 0, 4) };
            flags.Click += (a, b) => { s.FlagLeds = flags.IsChecked == true; p.SettingsChanged(); };
            root.Children.Add(flags);

            var screenOff = new CheckBox { Content = "Turn the wheel screen and LEDs off when no game is running, and when SimHub closes", IsChecked = s.ScreenOffWhenIdle, Margin = new Thickness(0, 4, 0, 4) };
            screenOff.Click += (a, b) => { s.ScreenOffWhenIdle = screenOff.IsChecked == true; p.SettingsChanged(); };
            root.Children.Add(screenOff);

            var reload = new Button { Content = "Reload UGT layouts / settings", Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            reload.Click += (a, b) => { p.RequestReload(); Dispatcher.BeginInvoke(new Action(FillPages), DispatcherPriority.Background); };
            root.Children.Add(reload);

            root.Children.Add(info);
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (a, b) => Refresh();
            Loaded += (a, b) => { Refresh(); t.Start(); };
            Unloaded += (a, b) => t.Stop();
        }

        void FillPages()
        {
            page.ItemsSource = p.Ugt.Layouts.ToList();
            page.SelectedItem = p.Ugt.Layouts.FirstOrDefault(l => l.Id == p.Settings.PageLayoutId);
        }

        void Refresh()
        {
            status.Text = "Status: " + p.Status;
            if (page.SelectedItem is Layout && ((Layout)page.SelectedItem).Id != p.Settings.PageLayoutId)
                page.SelectedItem = p.Ugt.Layouts.FirstOrDefault(l => l.Id == p.Settings.PageLayoutId);
            info.Text = "UGT data: " + UgtFiles.Root + "\nSD index: " + (p.Ugt.TocFile ?? "none") +
                        "\nShift lights: " + p.Ugt.ShiftTableName + " table, limiter " + p.Ugt.PitLimiterMode +
                        (p.Ugt.Warnings.Count > 0 ? "\nWarnings: " + string.Join("; ", p.Ugt.Warnings) : "");
        }

        static TextBlock Label(string text) { return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) }; }

        static StackPanel Slider(string name, int min, int max, int value, Action<int> changed)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var lbl = new TextBlock { Text = name + ": " + value };
            var sl = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
            sl.ValueChanged += (a, b) => { int v = (int)sl.Value; lbl.Text = name + ": " + v; changed(v); };
            sp.Children.Add(lbl); sp.Children.Add(sl);
            return sp;
        }
    }
}
