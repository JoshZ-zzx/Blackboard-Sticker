using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZongziTEK.BlackboardSticker.Models;

namespace ZongziTEK.BlackboardSticker.Views
{
    internal static class ScheduleUi
    {
        public static readonly DayOfWeek[] Days = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        public static string DayName(DayOfWeek day) => "星期" + "日一二三四五六"[(int)day];
        public static Button Button(string label, Action action)
        {
            var b = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(10, 6, 10, 6) };
            b.Click += (_, __) => action(); return b;
        }
        public static TextBox Field(Panel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(4, 8, 4, 2) });
            var input = new TextBox { Text = value, Margin = new Thickness(4), MinWidth = 80 }; panel.Children.Add(input); return input;
        }
        public static void Error(Exception error) => MessageBox.Show(error.Message, "无法完成操作", MessageBoxButton.OK, MessageBoxImage.Warning);
        public static string ChooseSubject(Window owner, ScheduleProfile profile, string selected)
        {
            var dialog = new Window { Owner = owner, Title = "选择学科", Width = 320, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new StackPanel { Margin = new Thickness(16) };
            var choices = new List<SubjectEntry> { new SubjectEntry { Id = "", Name = "未设置" } }; choices.AddRange(profile.Subjects);
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = selected ?? "", Margin = new Thickness(4) };
            panel.Children.Add(combo); panel.Children.Add(Button("确认", () => dialog.DialogResult = true)); dialog.Content = panel;
            return dialog.ShowDialog() == true ? (string)combo.SelectedValue ?? "" : selected;
        }
    }
    public class SlotEditorWindow : Window
    {
        private class Row
        {
            public string Id { get; set; }
            public string Start { get; set; }
            public string End { get; set; }
            public bool IsExercise { get; set; }
            public string FollowingSlotId { get; set; }
        }
        public List<TimeSlot> Result { get; private set; }
        private readonly System.Collections.ObjectModel.ObservableCollection<Row> rows;
        public SlotEditorWindow(Window owner, List<TimeSlot> slots, double lessonMinutes, double exerciseMinutes)
        {
            Owner = owner; Title = "时刻表（眼保健操结束即上课时自动关联）"; Width = 720; Height = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            rows = new System.Collections.ObjectModel.ObservableCollection<Row>(slots.Select(x => new Row { Id = x.Id, Start = x.Start.ToString(@"hh\:mm"), End = x.End.ToString(@"hh\:mm"), IsExercise = x.IsExercise, FollowingSlotId = x.FollowingSlotId }));
            var root = new DockPanel { Margin = new Thickness(16) }; var toolbar = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(toolbar, Dock.Bottom); root.Children.Add(toolbar);
            var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false };
            grid.Columns.Add(new DataGridTextColumn { Header = "开始（HH:mm）", Binding = new System.Windows.Data.Binding("Start") });
            grid.Columns.Add(new DataGridTextColumn { Header = "结束（HH:mm）", Binding = new System.Windows.Data.Binding("End") });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "眼保健操", Binding = new System.Windows.Data.Binding("IsExercise") });
            Action<bool> add = exercise => {
                double minutes = exercise ? exerciseMinutes : lessonMinutes;
                if (minutes <= 0) { ScheduleUi.Error(new Exception("请先设置默认时长。")); return; }
                var from = rows.Count == 0 ? TimeSpan.FromHours(8) : TimeSpan.TryParse(rows.Last().End, out var last) ? last : TimeSpan.FromHours(8);
                rows.Add(new Row { Id = Guid.NewGuid().ToString("N"), Start = from.ToString(@"hh\:mm"), End = (from + TimeSpan.FromMinutes(minutes)).ToString(@"hh\:mm"), IsExercise = exercise });
            };
            toolbar.Children.Add(ScheduleUi.Button("添加课次", () => add(false)));
            toolbar.Children.Add(ScheduleUi.Button("添加眼保健操", () => add(true)));
            toolbar.Children.Add(ScheduleUi.Button("删除选中", () => { if (grid.SelectedItem is Row r) rows.Remove(r); }));
            toolbar.Children.Add(ScheduleUi.Button("确认", () => {
                try {
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                    Result = rows.Select(x => new TimeSlot { Id = x.Id, Start = TimeSpan.Parse(x.Start), End = TimeSpan.Parse(x.End), IsExercise = x.IsExercise, FollowingSlotId = x.FollowingSlotId }).ToList();
                    ScheduleProfile.ValidateSlots(Result); DialogResult = true;
                } catch (Exception e) { ScheduleUi.Error(e); }
            })); root.Children.Add(grid); Content = root;
        }
    }
}
