using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZongziTEK.BlackboardSticker.Views
{
    public sealed class ReminderOverlayWindow : Window
    {
        private readonly TextBlock title = new TextBlock { FontSize = 30, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock remaining = new TextBlock { FontSize = 18, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
        public ReminderOverlayWindow()
        {
            Title = "课程提醒"; Width = 520; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
            var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(title); panel.Children.Add(remaining);
            Content = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 55, 85)), Child = panel };
            int index = MainWindow.Settings.Look.TargetMonitor;
            var screens = System.Windows.Forms.Screen.AllScreens;
            var area = screens[Math.Max(0, Math.Min(index, screens.Length - 1))].WorkingArea;
            Loaded += (_, __) => {
                var source = PresentationSource.FromVisual(this);
                var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var point = transform.Transform(new Point(area.Right, area.Top)); Left = point.X - Width - 24; Top = point.Y + 24;
            };
        }
        public void Update(string text, double seconds)
        {
            title.Text = text; remaining.Text = "剩余 " + Math.Ceiling(seconds) + " 秒";
        }
    }
}
