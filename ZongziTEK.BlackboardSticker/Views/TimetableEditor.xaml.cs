using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZongziTEK.BlackboardSticker.Helpers;
using ZongziTEK.BlackboardSticker.Models;

namespace ZongziTEK.BlackboardSticker.Views
{
    public partial class TimetableEditor : Window
    {
        private ScheduleProfile profile;
        private bool dirty;
        public TimetableEditor()
        {
            InitializeComponent(); ScheduleStore.Initialize(); profile = ScheduleProfile.Clone(ScheduleStore.Current);
            Loaded += (_, __) => { if (!profile.SetupCompleted) Configure(true); else Render(); };
            Closing += (_, e) => { if (dirty && MessageBox.Show("尚有未保存的更改，关闭并放弃吗？", "课表设置", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true; };
        }
        private void Configure(bool first)
        {
            var wizard = new ScheduleSetupWindow(this, profile, first);
            if (wizard.ShowDialog() == true) { profile = wizard.Result; dirty = true; }
            Render();
        }
        private void Render()
        {
            Root.Children.Clear();
            var toolbar = new WrapPanel(); DockPanel.SetDock(toolbar, Dock.Top); Root.Children.Add(toolbar);
            toolbar.Children.Add(ScheduleUi.Button("默认时刻表 / 提醒 / 学科", () => Configure(false)));
            toolbar.Children.Add(ScheduleUi.Button("调课", () => {
                var dialog = new RescheduleWindow(this, profile);
                if (dialog.ShowDialog() == true) { dirty = true; Render(); }
            }));
            toolbar.Children.Add(ScheduleUi.Button("保存课表", () => {
                try { ScheduleStore.Save(profile); dirty = false; (Application.Current.MainWindow as MainWindow)?.LoadTimetableOrCurriculum(); MessageBox.Show("课表已保存。", "课表设置"); }
                catch (Exception e) { ScheduleUi.Error(e); }
            }));
            toolbar.Children.Add(new TextBlock { Text = dirty ? "有未保存的更改" : "已保存", Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center });
            var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Root.Children.Add(scroll);
            var grid = new Grid(); scroll.Content = grid;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            foreach (var day in ScheduleUi.Days) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140, GridUnitType.Star), MinWidth = 130 });
            var all = ScheduleUi.Days.SelectMany(profile.Slots).ToList();
            int minimum = all.Count == 0 ? 8 * 60 : (int)all.Min(x => x.Start.TotalMinutes) / 60 * 60;
            int maximum = all.Count == 0 ? 18 * 60 : (int)Math.Ceiling(all.Max(x => x.End.TotalMinutes) / 60) * 60;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(180, (maximum - minimum) * 2)) });
            var axis = new Canvas(); Grid.SetRow(axis, 1); grid.Children.Add(axis);
            for (int minute = minimum; minute <= maximum; minute += 60) {
                var label = new TextBlock { Text = TimeSpan.FromMinutes(minute).ToString(@"hh\:mm"), Foreground = Brushes.DimGray };
                Canvas.SetTop(label, (minute - minimum) * 2); axis.Children.Add(label);
            }
            for (int column = 0; column < 7; column++) {
                var day = ScheduleUi.Days[column];
                var header = ScheduleUi.Button(ScheduleUi.DayName(day) + (profile.Day(day).Slots == null ? " · 默认" : " · 独立"), () => EditDay(day));
                Grid.SetColumn(header, column + 1); grid.Children.Add(header);
                var canvas = new Canvas { Background = column % 2 == 0 ? Brushes.WhiteSmoke : Brushes.White, ClipToBounds = true };
                Grid.SetColumn(canvas, column + 1); Grid.SetRow(canvas, 1); grid.Children.Add(canvas);
                int number = 0;
                foreach (var slot in profile.Slots(day).OrderBy(x => x.Start)) {
                    if (!slot.IsExercise) number++;
                    var name = slot.IsExercise ? "眼保健操" : profile.SubjectName(profile.Day(day), slot);
                    var button = new Button { Content = new TextBlock { Text = (slot.IsExercise ? "活动" : "第 " + number + " 节") + "  " + slot.Start.ToString(@"hh\:mm") + "–" + slot.End.ToString(@"hh\:mm") + "\n" + (name == "" ? "未设置 · 点击选择" : name), TextWrapping = TextWrapping.Wrap }, Background = slot.IsExercise ? Brushes.LightGoldenrodYellow : Brushes.AliceBlue, Height = Math.Max(24, (slot.End - slot.Start).TotalMinutes * 2 - 3), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(6) };
                    canvas.SizeChanged += (_, __) => button.Width = Math.Max(1, canvas.ActualWidth - 5);
                    Canvas.SetTop(button, (slot.Start.TotalMinutes - minimum) * 2); canvas.Children.Add(button);
                    if (!slot.IsExercise) button.Click += (_, __) => {
                        profile.Day(day).Subjects.TryGetValue(slot.Id, out var old);
                        var selected = ScheduleUi.ChooseSubject(this, profile, old);
                        if (selected != old) { profile.Day(day).Subjects[slot.Id] = selected ?? ""; dirty = true; Render(); }
                    };
                }
            }
        }
        private void EditDay(DayOfWeek day)
        {
            var choice = MessageBox.Show("是：设置独立时刻表；否：恢复默认时刻表；取消：返回。", ScheduleUi.DayName(day), MessageBoxButton.YesNoCancel);
            if (choice == MessageBoxResult.Cancel) return;
            var target = profile.Day(day);
            var previous = profile.Slots(day);
            if (choice == MessageBoxResult.No) {
                if (!ConfirmRemoved(target, previous, profile.DefaultSlots)) return;
                target.Slots = null;
            } else {
                var editor = new SlotEditorWindow(this, previous, profile.DefaultLessonMinutes, profile.ExerciseMinutes);
                if (editor.ShowDialog() != true || !ConfirmRemoved(target, previous, editor.Result)) return;
                target.Slots = editor.Result;
            }
            dirty = true; Render();
        }
        private bool ConfirmRemoved(DayPlan plan, System.Collections.Generic.List<TimeSlot> previous, System.Collections.Generic.List<TimeSlot> next)
        {
            var lost = previous.Where(x => !next.Any(n => n.Id == x.Id) && !string.IsNullOrEmpty(profile.SubjectName(plan, x))).ToList();
            return lost.Count == 0 || MessageBox.Show("变更会移除 " + lost.Count + " 个已填课次，请先核对。继续吗？", "课次变化", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }
    }
    public class ScheduleSetupWindow : Window
    {
        public ScheduleProfile Result { get; private set; }
        private readonly ScheduleProfile draft;
        private readonly StackPanel root = new StackPanel { Margin = new Thickness(20) };
        public ScheduleSetupWindow(Window owner, ScheduleProfile source, bool first)
        {
            Owner = owner; Title = first ? "首次课表设置 · 1 / 2" : "默认安排与学科"; Width = 640; Height = 740; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            draft = ScheduleProfile.Clone(source); Content = new ScrollViewer { Content = root }; TimeStep();
        }
        private void TimeStep()
        {
            root.Children.Clear(); root.Children.Add(new TextBlock { Text = "1. 默认时刻表与提醒", FontSize = 24 });
            var lesson = ScheduleUi.Field(root, "默认每节课时长（分钟）", draft.DefaultLessonMinutes == 0 ? "" : draft.DefaultLessonMinutes.ToString());
            var exercise = ScheduleUi.Field(root, "眼保健操时长（分钟）", draft.ExerciseMinutes == 0 ? "" : draft.ExerciseMinutes.ToString());
            var preparation = ScheduleUi.Field(root, "预备铃至上课（分钟）", draft.PreparationMinutes.ToString());
            var lead = ScheduleUi.Field(root, "预备铃前提前提醒（秒）", draft.ReminderLeadSeconds.ToString());
            var dismissal = ScheduleUi.Field(root, "下课提醒显示（秒）", draft.DismissalSeconds.ToString());
            Action read = () => {
                draft.DefaultLessonMinutes = double.Parse(lesson.Text); draft.ExerciseMinutes = double.Parse(exercise.Text);
                if (draft.DefaultLessonMinutes <= 0 || draft.DefaultLessonMinutes > 240 || draft.ExerciseMinutes <= 0 || draft.ExerciseMinutes > 120) throw new Exception("请填写有效的课程与眼保健操时长。");
                draft.PreparationMinutes = double.Parse(preparation.Text); draft.ReminderLeadSeconds = double.Parse(lead.Text); draft.DismissalSeconds = double.Parse(dismissal.Text); draft.Validate();
            };
            root.Children.Add(ScheduleUi.Button("编辑默认每日时刻表（手动填写各节开始时间）", () => {
                try {
                    read(); var editor = new SlotEditorWindow(this, draft.DefaultSlots, draft.DefaultLessonMinutes, draft.ExerciseMinutes);
                    if (editor.ShowDialog() == true) {
                        var lost = draft.DefaultSlots.Where(x => !editor.Result.Any(n => n.Id == x.Id)).Count(x => draft.Week.Values.Any(d => d.Slots == null && !string.IsNullOrEmpty(draft.SubjectName(d, x))));
                        if (lost > 0 && MessageBox.Show("将删除有学科安排的默认课次，继续吗？", "课次变化", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        draft.DefaultSlots = editor.Result;
                    }
                } catch (Exception e) { ScheduleUi.Error(e); }
            }));
            root.Children.Add(ScheduleUi.Button("下一步：学科列表", () => { try { read(); if (draft.DefaultSlots.Count == 0) throw new Exception("请先添加默认时刻表。"); SubjectStep(); } catch (Exception e) { ScheduleUi.Error(e); } }));
        }
        private void SubjectStep()
        {
            Title = "课表设置 · 2 / 2"; root.Children.Clear(); root.Children.Add(new TextBlock { Text = "2. 学科列表", FontSize = 24 });
            var list = new StackPanel(); root.Children.Add(list);
            Action render = null;
            render = () => {
                list.Children.Clear();
                foreach (var subject in draft.Subjects.ToList()) {
                    var row = new DockPanel(); var remove = ScheduleUi.Button("删除", () => {
                        bool used = draft.Week.Values.Any(x => x.Subjects.Values.Contains(subject.Id)) || draft.Exceptions.Any(x => x.Plan.Subjects.Values.Contains(subject.Id));
                        if (used && MessageBox.Show("此学科已被引用，删除后相关格子将变为空白。继续吗？", "删除学科", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        foreach (var plan in draft.Week.Values.Concat(draft.Exceptions.Select(x => x.Plan))) foreach (var key in plan.Subjects.Where(x => x.Value == subject.Id).Select(x => x.Key).ToList()) plan.Subjects.Remove(key);
                        draft.Subjects.Remove(subject); render();
                    }); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                    var name = new TextBox { Text = subject.Name, Margin = new Thickness(4) }; name.TextChanged += (_, __) => subject.Name = name.Text; row.Children.Add(name); list.Children.Add(row);
                }
            }; render();
            root.Children.Add(ScheduleUi.Button("添加学科", () => { draft.Subjects.Add(new SubjectEntry { Name = "新学科" }); render(); }));
            root.Children.Add(ScheduleUi.Button("上一步", TimeStep));
            root.Children.Add(ScheduleUi.Button("完成，进入周视图", () => { try { draft.Validate(); draft.SetupCompleted = true; Result = draft; DialogResult = true; } catch (Exception e) { ScheduleUi.Error(e); } }));
        }
    }
    public class RescheduleWindow : Window
    {
        public RescheduleWindow(Window owner, ScheduleProfile profile)
        {
            Owner = owner; Title = "按日期调课"; Width = 600; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = root };
            root.Children.Add(new TextBlock { Text = "目标日期" }); var date = new DatePicker { SelectedDate = DateTime.Today }; root.Children.Add(date);
            root.Children.Add(new TextBlock { Text = "使用哪天的时刻表" }); var times = new ComboBox { ItemsSource = ScheduleUi.Days.Select(ScheduleUi.DayName), SelectedIndex = 0 }; root.Children.Add(times);
            root.Children.Add(new TextBlock { Text = "套用哪天的课程" }); var courses = new ComboBox { ItemsSource = ScheduleUi.Days.Select(ScheduleUi.DayName), SelectedIndex = 0 }; root.Children.Add(courses);
            DatePlan draft = null; var preview = new StackPanel(); var sources = new TextBlock { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
            Action render = null; render = () => {
                preview.Children.Clear(); if (draft == null) return;
                sources.Text = draft.Date.ToString("yyyy-MM-dd") + " · 时间：" + ScheduleUi.DayName(draft.TimeSource) + " · 课程：" + ScheduleUi.DayName(draft.CourseSource);
                foreach (var slot in draft.Plan.Slots.OrderBy(x => x.Start)) {
                    var b = ScheduleUi.Button(slot.Start.ToString(@"hh\:mm") + "–" + slot.End.ToString(@"hh\:mm") + "  " + (slot.IsExercise ? "眼保健操" : profile.SubjectName(draft.Plan, slot)), () => {
                        draft.Plan.Subjects.TryGetValue(slot.Id, out var old); var value = ScheduleUi.ChooseSubject(this, profile, old); draft.Plan.Subjects[slot.Id] = value ?? ""; render();
                    }); b.IsEnabled = !slot.IsExercise; preview.Children.Add(b);
                }
            };
            Action invalidate = () => { draft = null; sources.Text = "来源已改变，请重新生成预览或载入已有调课。"; render(); };
            date.SelectedDateChanged += (_, __) => invalidate(); times.SelectionChanged += (_, __) => invalidate(); courses.SelectionChanged += (_, __) => invalidate();
            root.Children.Add(ScheduleUi.Button("生成预览", () => {
                if (!date.SelectedDate.HasValue) return;
                draft = profile.Compose(date.SelectedDate.Value, ScheduleUi.Days[times.SelectedIndex], ScheduleUi.Days[courses.SelectedIndex], out int surplus);
                int targetCount = draft.Plan.Slots.Count(x => !x.IsExercise); int sourceCount = profile.Slots(draft.CourseSource).Count(x => !x.IsExercise);
                if (sourceCount != targetCount) {
                    MessageBox.Show("来源教学课次 " + sourceCount + " 节，目标 " + targetCount + " 节。" + (surplus > 0 ? "多余来源课程：" + string.Join("、", profile.Slots(draft.CourseSource).Where(x => !x.IsExercise).OrderBy(x => x.Start).Skip(targetCount).Select(x => profile.SubjectName(profile.Day(draft.CourseSource), x))) + "。请调整来源，课次不足时不能保存。" : "多余目标格子留空，可逐节填写。"), "课次数量不一致");
                    if (surplus > 0) { draft = null; render(); return; }
                } render();
            }));
            root.Children.Add(ScheduleUi.Button("载入该日期已有调课", () => { draft = ScheduleProfile.Clone(profile.Exceptions.FirstOrDefault(x => x.Date.Date == date.SelectedDate?.Date)); if (draft == null) MessageBox.Show("该日期没有调课。"); render(); }));
            root.Children.Add(sources); root.Children.Add(preview);
            root.Children.Add(ScheduleUi.Button("确认（主界面保存后生效）", () => {
                if (draft == null) return;
                if (profile.Exceptions.Any(x => x.Date.Date == draft.Date.Date) && MessageBox.Show("该日期已有调课，覆盖吗？", "覆盖调课", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                profile.Exceptions.RemoveAll(x => x.Date.Date == draft.Date.Date); profile.Exceptions.Add(draft); DialogResult = true;
            }));
            root.Children.Add(ScheduleUi.Button("取消该日期调课（主界面保存后生效）", () => {
                if (date.SelectedDate.HasValue && MessageBox.Show("取消该日期的调课？", "取消调课", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { profile.Exceptions.RemoveAll(x => x.Date.Date == date.SelectedDate.Value.Date); DialogResult = true; }
            }));
        }
    }
}
