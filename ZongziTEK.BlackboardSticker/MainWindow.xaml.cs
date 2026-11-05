using AutoUpdaterDotNET;
using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZongziTEK.BlackboardSticker.Helpers;
using ZongziTEK.BlackboardSticker.Models;
using ZongziTEK.BlackboardSticker.Pages;

using ZongziTEK.BlackboardSticker.ViewModels;
using ZongziTEK.BlackboardSticker.Views;
using Drawing = System.Drawing;
using File = System.IO.File;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace ZongziTEK.BlackboardSticker
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 窗体
            SetWindowVerticalSize();

            // 加载文件
            LoadSettings();
            ScheduleStore.Initialize();
            LoadBlackboardText();
            Task.Run(() =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    LoadLauncher();
                });
            });

            SetTheme();

            // 设置透明窗口
            if (Settings.Look.IsWindowChromeDisabled) // AllowTransparency
            {
                WindowsHelper.SetAllowTransparency(this);
            }
            else // WindowChrome
            {
                WindowsHelper.SetWindowChrome(this);
            }

            // 检查更新
            // Fork updates are installed explicitly; never install an upstream binary over this edition.

            LoadFrameInfoPagesList();
            _frameInfoNavigationTimer.Tick += FrameInfoNavigationTimer_Tick;
            _frameInfoNavigationTimer.Interval = TimeSpan.FromSeconds(4);
            _frameInfoNavigationTimer.Start();

            // 课程表
            _timetableTimer = new DispatcherTimer();
            _timetableTimer.Tick += CheckTimetable;
            _timetableTimer.Interval = TimeSpan.FromMilliseconds(1000);
            LoadTimetableOrCurriculum();

            // 颜色主题
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            SystemEvents_UserPreferenceChanged(null, null);

            // 小黑板 2
            CheckIsBlackboardLocked();

            // 隐藏管家助手
            timerHideSeewoServiceAssistant.Tick += TimerHideSeewoServiceAssistant_Tick;
            timerHideSeewoServiceAssistant.Interval = TimeSpan.FromSeconds(1);

        }

        #region Window
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var hwnd = new WindowInteropHelper(this).Handle;
            WindowsHelper.SetWindowStyleToolWindow(hwnd);
        }

        private void window_StateChanged(object sender, EventArgs e)
        {
            WindowState = WindowState.Normal;

            if (Settings.Automation.IsBottomMost) WindowsHelper.SetBottom(window);
        }

        private void window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!ScheduleStore.Current.SetupCompleted) Dispatcher.BeginInvoke(new Action(() => new TimetableEditor().ShowDialog()));
            SwitchLookMode(0);
            SwitchLookMode(Settings.Look.LookMode);

            if (Settings.Look.IsAnimationEnhanced)
            {
                ThicknessAnimation marginAnimation = new()
                {
                    From = new Thickness(BorderMain.ActualWidth + BorderMain.Margin.Right, 8, -BorderMain.ActualWidth + BorderMain.Margin.Right, 8),
                    To = new Thickness(8),
                    Duration = TimeSpan.FromMilliseconds(1000),
                    EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
                };

                BorderMain.BeginAnimation(MarginProperty, marginAnimation);
            }

            _timetableScrollTimer.Tick += TimetableScrollTimer_Tick;
            _timetableScrollTimer.Start();
            ScrollToCurrentLesson();
        }

        private void window_Activated(object sender, EventArgs e)
        {
            if (Settings.Automation.IsBottomMost) WindowsHelper.SetBottom(window);
        }

        public static bool IsClosingFromButton = false;
        private void window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_textLoaded && !SaveBlackboardText()) { e.Cancel = true; return; }
            if (!IsClosingFromButton)
            {
                e.Cancel = true;
                if (MessageBox.Show("是否继续关闭 ZongziTEK 黑板贴", "ZongziTEK 黑板贴", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                {
                    if (MessageBox.Show("真的狠心关闭 ZongziTEK 黑板贴 吗？", "ZongziTEK 黑板贴", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK)
                    {
                        if (MessageBox.Show("是否取消关闭 ZongziTEK 黑板贴 ？", "ZongziTEK 黑板贴", MessageBoxButton.YesNo, MessageBoxImage.Error) != MessageBoxResult.Yes)
                        {
                            e.Cancel = false;
                            Application.Current.Shutdown();
                        }
                    }
                }
            }
        }

        // 没用
        /*
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);

            Dispatcher.BeginInvoke(() =>
            {
                if (MessageBox.Show("检测到系统 DPI 变化。为确保黑板贴显示正常，需要重启黑板贴。\r\n是否立即重启黑板贴？", "ZongziTEK 黑板贴", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Restart();
                }
            });
        }
        */

        private void SetWindowVerticalSize()
        {
            Height = SystemParameters.WorkArea.Height;
            Top = 0;
        }

        private async void iconSwitchLeft_MouseDown(object sender, MouseButtonEventArgs e)
        {
            window.BeginAnimation(LeftProperty, null);

            iconSwitchLeft.Visibility = Visibility.Collapsed;
            iconSwitchRight.Visibility = Visibility.Visible;

            Rect targetWorkArea = GetTargetWorkArea();

            DoubleAnimation leftAnimation = new()
            {
                From = Left,
                To = targetWorkArea.Left,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(LeftProperty, leftAnimation);

            await Task.Delay(500);

            window.BeginAnimation(LeftProperty, null);
        }

        private async void iconSwitchRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            window.BeginAnimation(LeftProperty, null);

            iconSwitchRight.Visibility = Visibility.Collapsed;
            iconSwitchLeft.Visibility = Visibility.Visible;

            Rect targetWorkArea = GetTargetWorkArea();

            DoubleAnimation leftAnimation = new()
            {
                From = Left,
                To = targetWorkArea.Left + (targetWorkArea.Width - ActualWidth),
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CircleEase() { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(LeftProperty, leftAnimation);

            await Task.Delay(500);

            window.BeginAnimation(LeftProperty, null);
        }
        #endregion

        #region Blackboard
        private bool _textLoaded;
        private DispatcherTimer _textSaveTimer;
        private void LoadBlackboardText()
        {
            try {
                string path = System.IO.Path.Combine(GetDataPath(), "Blackboard.txt");
                BlackboardText.Text = File.Exists(path) ? File.ReadAllText(path) : "";
            } catch (Exception e) { System.Windows.MessageBox.Show(e.Message, "小黑板读取失败"); }
            _textLoaded = true;
            _textSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _textSaveTimer.Tick += (_, __) => { _textSaveTimer.Stop(); SaveBlackboardText(); };
        }
        private bool SaveBlackboardText()
        {
            try { ScheduleStore.AtomicWrite(System.IO.Path.Combine(GetDataPath(), "Blackboard.txt"), BlackboardText.Text); return true; }
            catch (Exception e) { System.Windows.MessageBox.Show(e.Message, "小黑板保存失败，内容仍在编辑区"); return false; }
        }
        private void BlackboardText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_textLoaded) return;
            _textSaveTimer.Stop(); _textSaveTimer.Start();
        }
        private void ImportBlackboard_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "文本文件 (*.txt)|*.txt" };
            if (dialog.ShowDialog() != true) return;
            if (BlackboardText.Text.Length > 0 && MessageBox.Show("导入会替换当前文本，继续吗？", "导入文本", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { var text = File.ReadAllText(dialog.FileName); BlackboardText.Text = text; SaveBlackboardText(); }
            catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "导入失败"); }
        }
        private void ExportBlackboard_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "小黑板.txt" };
            if (dialog.ShowDialog() != true) return;
            try { ScheduleStore.AtomicWrite(dialog.FileName, BlackboardText.Text); }
            catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "导出失败"); }
        }
        private void ToggleButtonLock_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded) return;
            Settings.Blackboard.IsLocked = ToggleButtonLock.IsChecked == true; CheckIsBlackboardLocked(); SaveSettings();
        }
        private void CheckIsBlackboardLocked() { BlackboardText.IsReadOnly = Settings.Blackboard.IsLocked; }
        #endregion

        #region Timetable & Curriculum

        public static int TimetableToShowIndex = (int)DateTime.Today.DayOfWeek;

        public void LoadTimetableOrCurriculum()
        {

            if (Settings.TimetableSettings.IsTimetableEnabled)
            {
                LoadTimetable();

                textBlockCurriculum.Visibility = Visibility.Collapsed;
                StackPanelShowTimetable.Visibility = Visibility.Visible;
                MenuItemTimetableAutoScroll.IsChecked = true;

                _timetableTimer.Start();
            }
            else
            {
                LoadCurriculum();

                textBlockCurriculum.Visibility = Visibility.Visible;
                StackPanelShowTimetable.Visibility = Visibility.Collapsed;
                MenuItemTimetableAutoScroll.IsChecked = false;

                _timetableTimer.Stop();
            }

            CheckTimetableMenuItems();
        }

        private void MenuItemShowMondayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 1;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowTuesdayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 2;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowWednesdayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 3;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowThursdayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 4;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowFridayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 5;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowSaturdayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 6;
            LoadTimetableOrCurriculum();
        }

        private void MenuItemShowSundayTimetable_Click(object sender, RoutedEventArgs e)
        {
            TimetableToShowIndex = 0;
            LoadTimetableOrCurriculum();
        }

        private void CheckTimetableMenuItems() // 通过菜单项的 IsChecked 状态来告知用户正在展示哪个课表
        {
            foreach (MenuItem menuItem in MenuChooseTimetableToShow.Items)
            {
                menuItem.IsChecked = false;
            }

            switch (TimetableToShowIndex)
            {
                case 1: // 周一
                    MenuItemShowMondayTimetable.IsChecked = true;
                    break;
                case 2: // 周二
                    MenuItemShowTuesdayTimetable.IsChecked = true;
                    break;
                case 3: // 周三
                    MenuItemShowWednesdayTimetable.IsChecked = true;
                    break;
                case 4: // 周四
                    MenuItemShowThursdayTimetable.IsChecked = true;
                    break;
                case 5: // 周五
                    MenuItemShowFridayTimetable.IsChecked = true;
                    break;
                case 6: // 周六
                    MenuItemShowSaturdayTimetable.IsChecked = true;
                    break;
                case 0: // 周日
                    MenuItemShowSundayTimetable.IsChecked = true;
                    break;

            }
        }

        #region Curriculum
        public static Curriculums Curriculums = new Curriculums();
        public const string CurriculumsFileName = "Curriculums.json";

        private void SaveCurriculum()
        {
            Curriculums.Monday.Curriculums = textBoxMonday.Text;
            Curriculums.Tuesday.Curriculums = textBoxTuesday.Text;
            Curriculums.Wednesday.Curriculums = textBoxWednesday.Text;
            Curriculums.Thursday.Curriculums = textBoxThursday.Text;
            Curriculums.Friday.Curriculums = textBoxFriday.Text;
            Curriculums.Saturday.Curriculums = textBoxSaturday.Text;
            Curriculums.Sunday.Curriculums = textBoxSunday.Text;
            Curriculums.Temp.Curriculums = textBoxTempCurriculums.Text;

            string text = JsonConvert.SerializeObject(Curriculums, Formatting.Indented);

            try
            {
                File.WriteAllText(GetDataPath() + CurriculumsFileName, text);
            }
            catch { }
        }

        private void LoadCurriculum()
        {
            if (File.Exists(GetDataPath() + CurriculumsFileName))
            {
                try
                {
                    string text = File.ReadAllText(GetDataPath() + CurriculumsFileName);
                    Curriculums = JsonConvert.DeserializeObject<Curriculums>(text);
                }
                catch { }
            }

            textBoxMonday.Text = Curriculums.Monday.Curriculums;
            textBoxTuesday.Text = Curriculums.Tuesday.Curriculums;
            textBoxWednesday.Text = Curriculums.Wednesday.Curriculums;
            textBoxThursday.Text = Curriculums.Thursday.Curriculums;
            textBoxFriday.Text = Curriculums.Friday.Curriculums;
            textBoxSaturday.Text = Curriculums.Saturday.Curriculums;
            textBoxSunday.Text = Curriculums.Sunday.Curriculums;
            textBoxTempCurriculums.Text = Curriculums.Temp.Curriculums;

            textBlockCurriculum.FontSize = Settings.TimetableSettings.FontSize;

            switch (TimetableToShowIndex)
            {
                case 1: // 周一
                    textBlockCurriculum.Text = Curriculums.Monday.Curriculums;
                    break;
                case 2: // 周二
                    textBlockCurriculum.Text = Curriculums.Tuesday.Curriculums;
                    break;
                case 3: // 周三
                    textBlockCurriculum.Text = Curriculums.Wednesday.Curriculums;
                    break;
                case 4: // 周四
                    textBlockCurriculum.Text = Curriculums.Thursday.Curriculums;
                    break;
                case 5: // 周五
                    textBlockCurriculum.Text = Curriculums.Friday.Curriculums;
                    break;
                case 6: // 周六
                    textBlockCurriculum.Text = Curriculums.Saturday.Curriculums;
                    break;
                case 0: // 周日
                    textBlockCurriculum.Text = Curriculums.Sunday.Curriculums;
                    break;
                case 7: // 临时
                    textBlockCurriculum.Text = Curriculums.Temp.Curriculums;
                    break;
            }

            if (textBlockCurriculum.Text.Trim() == string.Empty)
            {
                LabelNoClass.Visibility = Visibility.Visible;
            }
            else
            {
                LabelNoClass.Visibility = Visibility.Collapsed;
            }
        }

        private bool _isTimetableEditorOpen = false;

        private void editCurriculumButton_Click(object sender, RoutedEventArgs e)
        {
            if (Settings.TimetableSettings.IsTimetableEnabled)
            {
                if (!_isTimetableEditorOpen)
                {
                    TimetableEditor timetableEditor = new();
                    timetableEditor.Closed += TimetableEditor_Closed;
                    _isTimetableEditorOpen = true;
                    timetableEditor.Show();
                }
            }
            else
            {
                ScrollViewerShowCurriculum.Visibility = Visibility.Collapsed;
                editCurriculumButton.Visibility = Visibility.Collapsed;

                saveCurriculumButton.Visibility = Visibility.Visible;
                ScrollViewerCurriculum.Visibility = Visibility.Visible;
            }
        }

        private void TimetableEditor_Closed(object sender, EventArgs e)
        {
            LoadTimetableOrCurriculum();
            _isTimetableEditorOpen = false;
        }

        private void saveCurriculumButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurriculum();

            ScrollViewerShowCurriculum.Visibility = Visibility.Visible;
            editCurriculumButton.Visibility = Visibility.Visible;

            saveCurriculumButton.Visibility = Visibility.Collapsed;
            ScrollViewerCurriculum.Visibility = Visibility.Collapsed;

            LoadTimetableOrCurriculum();
        }
        #endregion

        #region Timetable
        public static Timetable Timetable = new Timetable();
        public const string TimetableFileName = "Timetable.json";
        private DispatcherTimer _timetableTimer;
        private List<Lesson> _timetableCurrent = new();

        private void LoadTimetable()
        {

            ScheduleStore.Initialize();
            var today = DateTime.Now.AddSeconds(Settings.TimetableSettings.TimeOffset).Date;
            var date = today.AddDays(((TimetableToShowIndex % 7) - (int)today.DayOfWeek + 7) % 7);
            _timetableCurrent = ScheduleStore.Current.Lessons(date);
            MenuChooseTimetableToShow.IsEnabled = true;
            PresentTimetable(_timetableCurrent);

            _lessonIndex = -1;

            double scale = (0.5 / 16) * Settings.TimetableSettings.FontSize + (1 - (0.5 / 16) * 24);
            if (scale < 0.5) scale = 0.5;
            else if (scale > 1.6) scale = 1.6;
            ScaleTimetable.ScaleX = scale;
            ScaleTimetable.ScaleY = scale;
        }

        private void PresentTimetable(List<Lesson> timetable)
        {
            StackPanelShowTimetable.Children.Clear();
            if (timetable == null || timetable.Count == 0)
            {
                LabelNoClass.Visibility = Visibility.Visible;
            }
            else
            {
                LabelNoClass.Visibility = Visibility.Collapsed;

                foreach (Lesson lesson in timetable)
                {
                    TimetableLesson timetableLesson = new()
                    {
                        Subject = lesson.Subject,
                        Time = lesson.StartTime.ToString(@"hh\:mm")
                    };
                    if (lesson.IsSplitBelow)
                    {
                        timetableLesson.Margin = new Thickness(0, 0, 0, 8);
                    }

                    StackPanelShowTimetable.Children.Add(timetableLesson);
                }
            }
        }

        private int _lessonIndex = -1; // 第几节课
        private DayOfWeek _lastDay = DateTime.Today.DayOfWeek;

        private ReminderOverlayWindow _reminderWindow;
        private readonly System.Collections.Generic.HashSet<string> _spokenReminders = new();
        private void CheckTimetable(object sender, EventArgs e)
        {
            try {
                var now = DateTime.Now.AddSeconds(Settings.TimetableSettings.TimeOffset);
                if (_lastDay != now.DayOfWeek) { _lastDay = now.DayOfWeek; TimetableToShowIndex = (int)now.DayOfWeek; LoadTimetable(); _spokenReminders.Clear(); }
                var state = ReminderEngine.Evaluate(ScheduleStore.Current, now);
                if (state == null || !Settings.TimetableSettings.IsTimetableNotificationEnabled) {
                    _reminderWindow?.Close(); _reminderWindow = null;
                } else {
                    if (_reminderWindow == null) { _reminderWindow = new ReminderOverlayWindow(); _reminderWindow.Show(); }
                    _reminderWindow.Update(state.Title, Math.Max(0, (state.Until - now).TotalSeconds));
                }
                if (state != null && _spokenReminders.Add(state.Key) && (state.BeforeClass ? Settings.TimetableSettings.IsBeginSpeechEnabled : Settings.TimetableSettings.IsOverSpeechEnabled)) TTSHelper.PlayText(state.Title);
                int highlight = _timetableCurrent.FindIndex(x => now.TimeOfDay >= x.StartTime && now.TimeOfDay < x.EndTime);
                if (highlight < 0) highlight = _timetableCurrent.FindIndex(x => x.StartTime > now.TimeOfDay);
                for (int i = 0; i < StackPanelShowTimetable.Children.Count; i++) {
                    if (!(StackPanelShowTimetable.Children[i] is TimetableLesson item)) continue;
                    if (i == highlight) item.Activate(); else item.Deactivate();
                    var lesson = _timetableCurrent[i];
                    item.Time = i == highlight && now.TimeOfDay >= lesson.StartTime ? (lesson.EndTime - now.TimeOfDay).ToString(@"hh\:mm\:ss") : lesson.StartTime.ToString(@"hh\:mm");
                }
                _lessonIndex = highlight;
            } catch (Exception error) { LogHelper.WriteLogToFile(error.ToString(), LogHelper.LogType.Error); }
        }

        private void ScrollToCurrentLesson()
        {
            if (Settings.TimetableSettings.IsTimetableEnabled && StackPanelShowTimetable.ActualHeight > (ScrollViewerShowCurriculum.ActualHeight - 32))
            {
                int extraMarginCount = 0;
                double scale = ScaleTimetable.ScaleX;

                foreach (Lesson lesson in _timetableCurrent)
                {
                    if (lesson.IsSplitBelow) extraMarginCount++;
                    if (_timetableCurrent.IndexOf(lesson) >= _lessonIndex) break;
                }

                double offset = (((_lessonIndex + 1) * 48) + 8 * extraMarginCount - 48) * scale;
                if (offset > (StackPanelShowTimetable.ActualHeight * scale + 32 - ScrollViewerShowCurriculum.ActualHeight))
                    offset = (StackPanelShowTimetable.ActualHeight * scale + 32 - ScrollViewerShowCurriculum.ActualHeight);

                DoubleAnimation offsetAnimation = new()
                {
                    From = ScrollViewerShowCurriculum.VerticalOffset,
                    To = offset,
                    Duration = TimeSpan.FromMilliseconds(750),
                    EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseInOut }
                };
                ScrollViewerShowCurriculum.BeginAnimation(Helpers.ScrollViewerBehavior.VerticalOffsetProperty, offsetAnimation);
            }
        }

        private int _scrollFreeTime = 0;

        private DispatcherTimer _timetableScrollTimer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        private void TimetableScrollTimer_Tick(object sender, EventArgs e)
        {
            _scrollFreeTime++;

            if (_scrollFreeTime > 5)
            {
                ScrollToCurrentLesson();
            }
        }

        private void ScrollViewerShowCurriculum_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _scrollFreeTime = 0;
        }

        private void MenuItemTimetableAutoScroll_Click(object sender, RoutedEventArgs e)
        {
            if (MenuItemTimetableAutoScroll.IsChecked)
            {
                ScrollToCurrentLesson();
                _timetableScrollTimer.Start();
            }
            else
            {
                _timetableScrollTimer.Stop();
            }
        }
        #endregion
        #endregion

        #region Clock
        private void iconShowBigClock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            new FullScreenClock(GetTargetWorkArea()).Show();
        }
        #endregion

        #region Launcher
        private async void LoadLauncher()
        {
            ScrollViewerLauncher.Visibility = Visibility.Collapsed;
            ProgressBarLauncher.Visibility = Visibility.Visible;

            Dictionary<string, Drawing.Bitmap> fileInfo = new();
            string LinkPath = AppDomain.CurrentDomain.BaseDirectory + @"LauncherLinks\";
            if (!new DirectoryInfo(LinkPath).Exists)
            {
                try { new DirectoryInfo(LinkPath).Create(); }
                catch { }
            }
            try
            {
                string[] files = Directory.GetFiles(LinkPath);
                foreach (string filePath in files)
                {
                    if (!filePath.EndsWith(".lnk"))
                    {
                        continue;
                    }
                    WshShell shell = new();
                    IWshShortcut wshShortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                    /*if (!File.Exists(wshShortcut.TargetPath))
                    {
                        continue;
                    }*/
                    try
                    {
#pragma warning disable CS8602 // 解引用可能出现空引用。
                        Drawing.Bitmap icon = Drawing.Icon.ExtractAssociatedIcon(wshShortcut.TargetPath).ToBitmap();
#pragma warning restore CS8602 // 解引用可能出现空引用。
                        //fileInfo.Add(wshShortcut.TargetPath, icon);
                        fileInfo.Add(filePath, icon);
                    }
                    catch { }
                }

                foreach (KeyValuePair<string, Drawing.Bitmap> file in fileInfo)
                {
                    //启动台里面的按钮
                    Button LinkButton = new()
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Height = 36,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                        BorderThickness = new Thickness(0)
                    };

                    //按钮里面的布局
                    SimpleStackPanel ContentStackPanel = new()
                    {
                        Spacing = 8,
                        Orientation = Orientation.Horizontal
                    };

                    //图标
                    Image image = new()
                    {
                        Height = 19,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    BitmapSource bitmapSource = System.Windows.Interop.Imaging.
                        CreateBitmapSourceFromHBitmap(file.Value.GetHbitmap(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Source = bitmapSource;

                    //名字
                    TextBlock textBlockFileName = new()
                    {
                        Text = Path.GetFileName(file.Key),
                        Visibility = Visibility.Collapsed
                    };

                    TextBlock textBlockLinkName = new()
                    {
                        Text = Path.GetFileName(file.Key).Remove(Path.GetFileName(file.Key).LastIndexOf("."), 4),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    //开始组装按钮
                    ContentStackPanel.Children.Add(image);
                    ContentStackPanel.Children.Add(textBlockFileName);
                    ContentStackPanel.Children.Add(textBlockLinkName);
                    LinkButton.Content = ContentStackPanel;
                    LinkButton.Click += LinkButton_Click;

                    //往启动台里面添加按钮
                    await Task.Run(() =>
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            StackPanelLauncher.Children.Add(LinkButton);
                        }));
                    });
                }

                ScrollViewerLauncher.Visibility = Visibility.Visible;
                ProgressBarLauncher.Visibility = Visibility.Collapsed;
            }
            catch (Exception e)
            {
                MessageBox.Show("加载启动台时出现错误：\r\n" + e.Message);
            }
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            string LinkPath = AppDomain.CurrentDomain.BaseDirectory + @"LauncherLinks\";
            string filePath = LinkPath + ((TextBlock)((SimpleStackPanel)((Button)sender).Content).Children[2]).Text + ".lnk";
            WshShell shell = new();
            IWshShortcut wshShortcut = (IWshShortcut)shell.CreateShortcut(filePath);
            Process.Start("explorer.exe", wshShortcut.TargetPath);

            if (Settings.Automation.IsBottomMost) WindowsHelper.SetBottom(window);
        }
        private void buttonExplorer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(@"C:\Windows\System32\explorer.exe", "/s,");
            }
            catch (Exception) { }

            if (Settings.Automation.IsBottomMost) WindowsHelper.SetBottom(window);
        }
        private void ButtonReloadLauncher_Click(object sender, RoutedEventArgs e)
        {
            ScrollViewerLauncher.Visibility = Visibility.Collapsed;
            ProgressBarLauncher.Visibility = Visibility.Visible;

            Button buttonExplorerBackup = buttonExplorer;

            StackPanelLauncher.Children.Clear();

            Task.Run(() =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StackPanelLauncher.Children.Add(buttonExplorerBackup);
                }));
            });

            Task.Run(() =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    LoadLauncher();
                });
            });
        }

        private bool _isLauncherEditorOpen = false;

        private void ButtonEditLauncher_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLauncherEditorOpen)
            {
                LauncherEditor launcherEditor = new();
                launcherEditor.Closed += LauncherEditor_Closed;
                _isLauncherEditorOpen = true;
                launcherEditor.Show();
            }
        }

        private void LauncherEditor_Closed(object sender, EventArgs e)
        {
            ButtonReloadLauncher_Click(null, null);
            _isLauncherEditorOpen = false;
        }

        #endregion

        #region Settings

        #region Panel Show & Hide

        private bool _isSettingsWindowOpen = false;
        private SettingsWindow _settingsWindow;

        private void iconShowSettingsPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            /*if (borderSettingsPanel.Visibility == Visibility.Collapsed) borderSettingsPanel.Visibility = Visibility.Visible;
            else btnHideSettingsPanel_Click(null, null);

            ButtonRefreshBNSStatus_Click(null, null);*/
            if (!_isSettingsWindowOpen)
            {
                SettingsWindow settingsWindow = new();
                _settingsWindow = settingsWindow;
                settingsWindow.Closed += SettingsWindow_Closed;
                settingsWindow.Show();
                _isSettingsWindowOpen = true;
            } else { _settingsWindow.WindowState = WindowState.Normal; _settingsWindow.Activate(); }
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            _isSettingsWindowOpen = false;
        }

        #endregion

        private bool _isSettingsLoaded = false;

        private void LoadSettings()
        {
            if (File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName)))
            {
                try
                {
                    string text = File.ReadAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName));
                    Settings = JsonConvert.DeserializeObject<Settings>(text);
                }
                catch { }
            }

            ToggleButtonLock.IsChecked = Settings.Blackboard.IsLocked;

            if (Settings.Automation.IsAutoHideHugoAssistantEnabled) timerHideSeewoServiceAssistant.Start();

            _isSettingsLoaded = true;
        }
        public static Settings Settings = new();
        public const string SettingsFileName = "Settings.json";
        public static void SaveSettings()
        {
            string text = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            try
            {
                ScheduleStore.AtomicWrite(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName), text);
            }
            catch (Exception error) { System.Windows.MessageBox.Show(error.Message, "设置保存失败"); }
        }
        #endregion

        #region Utility
        #region FileDrag

        /*private bool isCopying = false;
        private void window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            TextBlockDragHint.Text = "松手以将文件添加到桌面";
            ProgressBarDragEnter.Visibility = Visibility.Collapsed;
            BorderDragEnter.Visibility = Visibility.Visible;
        }

        private void window_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            if (!isCopying) BorderDragEnter.Visibility = Visibility.Collapsed;
        }

        private async void window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            isCopying = true;

            ProgressBarDragEnter.Visibility = Visibility.Visible;
            TextBlockDragHint.Text = "正在添加文件到桌面，请稍等";

            BorderDragEnter.Visibility = Visibility.Collapsed;

            string folderFileName;
            try { folderFileName = ((System.Array)e.Data.GetData(DataFormats.FileDrop)).GetValue(0).ToString(); }
            catch
            {
#pragma warning disable CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法
                Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("请将此文件拖到桌面空白处", "", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                });
#pragma warning restore CS4014 // 由于此调用不会等待，因此在调用完成前将继续执行当前方法

                BorderDragEnter.Visibility = Visibility.Collapsed;
                ProgressBarDragEnter.Visibility = Visibility.Collapsed;

                return;
            }

            string dest = Path.GetFileName(folderFileName);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\";

            await Task.Run(() =>
            {
                if (folderFileName != desktop + dest)
                {
                    if (new DirectoryInfo(folderFileName).Exists)
                    {
                        if (new DirectoryInfo(desktop + dest).Exists) new DirectoryInfo(desktop + dest).Delete(true);
                        try
                        {
                            FileUtility.CopyFolder(folderFileName, desktop + dest);
                        }
                        catch (Exception ex) { MessageBox.Show(Convert.ToString(ex)); }
                    }
                    else
                    {
                        if (File.Exists(desktop + dest)) File.Delete(desktop + dest);
                        try
                        {
                            File.Copy(folderFileName, desktop + dest);
                        }
                        catch (Exception ex) { MessageBox.Show(Convert.ToString(ex)); }
                    }
                }
            });

            BorderDragEnter.Visibility = Visibility.Collapsed;
            ProgressBarDragEnter.Visibility = Visibility.Collapsed;

            isCopying = false;
        }*/
        private void window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show("请将此文件拖到桌面空白处", null, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                });
            });
        }
        #endregion
        #endregion

        #region Check
        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Settings.Look.Theme == 0) SetTheme();
        }

        public static bool IsSystemThemeLight()
        {
            bool light = true;
            try
            {
                RegistryKey registryKey = Registry.CurrentUser;
                RegistryKey themeKey = registryKey.OpenSubKey("software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                int keyValue = 0;
                if (themeKey != null)
                {
                    keyValue = (int)themeKey.GetValue("SystemUsesLightTheme");
                }
                if (keyValue == 1) light = true;
                else light = false;
            }
            catch { }
            return light;
        }

        #region AutoHideSeewoServiceAssistant
        public DispatcherTimer timerHideSeewoServiceAssistant = new DispatcherTimer();
        public static bool IsSeewoServiceAssistantHidden = false;
        private void TimerHideSeewoServiceAssistant_Tick(object sender, EventArgs e)
        {
            if (WindowsHelper.MinimizeSeewoServiceAssistant())
            {
                timerHideSeewoServiceAssistant.Stop();
                IsSeewoServiceAssistantHidden = true;
            }
        }
        #endregion

        #endregion

        #region InfoBoard
        private List<Page> _frameInfoPages = new();
        private int _frameInfoPageIndex = 0;
        private DispatcherTimer _frameInfoNavigationTimer = new DispatcherTimer();

        private void BorderSwitchFrameInfoPage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            SwitchFrameInfoPage();
        }

        private void FrameInfoNavigationTimer_Tick(object sender, EventArgs e)
        {
            SwitchFrameInfoPage();
        }

        public void SwitchFrameInfoPage()
        {
            if (_frameInfoPages.Count == 0) { _frameInfoNavigationTimer.Stop(); FrameInfo.Content = null; return; }

            _frameInfoNavigationTimer.Stop();

            FrameInfo.NavigationService.RemoveBackEntry();

            _frameInfoPageIndex++;
            if (_frameInfoPageIndex >= _frameInfoPages.Count) _frameInfoPageIndex = 0;
            FrameInfo.Navigate(_frameInfoPages[_frameInfoPageIndex]);

            if (_frameInfoPages.Count > 1) _frameInfoNavigationTimer.Start();
        }

        public void LoadFrameInfoPagesList()
        {
            _frameInfoPages.Clear();

            if (Settings.InfoBoard.IsDatePageEnabled) _frameInfoPages.Add(new DatePage());
            if (Settings.InfoBoard.IsCountdownPageEnabled) _frameInfoPages.Add(new CountdownPage());

            if (_frameInfoPages.Count == 0) { _frameInfoNavigationTimer.Stop(); FrameInfo.Content = null; return; }

            FrameInfo.Navigate(_frameInfoPages[0]);

            if (_frameInfoPages.Count == 1)
            {
                _frameInfoNavigationTimer.Stop();
            }
            else
            {
                _frameInfoNavigationTimer.Start();
            }
        }
        #endregion

        #region Other Functions
        public static void Restart()
        {
            Process.Start(System.Windows.Forms.Application.ExecutablePath, "-m");

            IsClosingFromButton = true;
            System.Windows.Application.Current.Shutdown();
        }

        public static string GetDataPath()
        {
            string path;

            if (Settings.Storage.IsFilesSavingWithProgram)
            {
                path = System.AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                path = Settings.Storage.DataPath;
                if (!path.EndsWith("\\") || !path.EndsWith("/"))
                {
                    path += "\\";
                }

                if (!new DirectoryInfo(path).Exists)
                {
                    try { new DirectoryInfo(path).Create(); }
                    catch { }
                }
            }
            return path;
        }

        public static void SetTheme()
        {
            MainWindow window = Application.Current.MainWindow as MainWindow;

            if (Settings.Look.Theme == 1 || (Settings.Look.Theme == 0 && IsSystemThemeLight())) // 浅色模式
            {
                ThemeManager.SetRequestedTheme(window, ElementTheme.Light);
                ResourceDictionary resourceDictionary = new ResourceDictionary() { Source = new Uri("Style/Light.xaml", UriKind.Relative) };
                Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);

                if (window.ToggleButtonLock.IsChecked.Value) window.ToggleButtonLock.Foreground = new SolidColorBrush(Colors.White); else window.ToggleButtonLock.Foreground = new SolidColorBrush(Colors.Black);





            }
            else if (Settings.Look.Theme == 2 || (Settings.Look.Theme == 0 && (!IsSystemThemeLight()))) // 深色模式
            {
                ThemeManager.SetRequestedTheme(window, ElementTheme.Dark);
                ResourceDictionary resourceDictionary = new ResourceDictionary() { Source = new Uri("Style/Dark.xaml", UriKind.Relative) };
                Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);

                if (window.ToggleButtonLock.IsChecked.Value) window.ToggleButtonLock.Foreground = new SolidColorBrush(Colors.Black); else window.ToggleButtonLock.Foreground = new SolidColorBrush(Colors.White);




            }
        }

        public static bool StartAutomaticallyCreate(string exeName)
        {
            try
            {
                WshShell shell = new WshShell();
                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\" + exeName + ".lnk");
                shortcut.TargetPath = System.Windows.Forms.Application.ExecutablePath;
                shortcut.WorkingDirectory = System.Environment.CurrentDirectory;
                shortcut.WindowStyle = 1;
                shortcut.Description = exeName + "_link";
                shortcut.Save();
                return true;
            }
            catch (Exception) { }
            return false;
        }

        public static bool StartAutomaticallyDel(string exeName)
        {
            try
            {
                System.IO.File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\" + exeName + ".lnk");
                return true;
            }
            catch (Exception) { }
            return false;
        }

        public Rect GetTargetWorkArea()
        {
            List<Rect> monitorWorkAreas = new();
            WindowsHelper.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref WindowsHelper.RECT lprcMonitor, IntPtr dwData) =>
            {
                WindowsHelper.MONITORINFO mi = new WindowsHelper.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(mi);
                if (WindowsHelper.GetMonitorInfo(hMonitor, ref mi))
                {
                    WindowsHelper.GetDpiForMonitor(hMonitor, WindowsHelper.MonitorDpiType.Effective, out uint dpiX, out uint dpiY);
                    double scaleX = dpiX / 96.0;
                    monitorWorkAreas.Add(new Rect(mi.rcWork.Left / scaleX, mi.rcWork.Top / scaleX, (mi.rcWork.Right - mi.rcWork.Left) / scaleX, (mi.rcWork.Bottom - mi.rcWork.Top) / scaleX));
                }
                return true;
            }, IntPtr.Zero);

            int targetIndex = Settings.Look.TargetMonitor;
            if (monitorWorkAreas.Count == 0)
            {
                monitorWorkAreas.Add(SystemParameters.WorkArea);
                targetIndex = 0;
            }
            if (targetIndex < 0 || targetIndex >= monitorWorkAreas.Count) targetIndex = 0;
            return monitorWorkAreas[targetIndex];
        }

        public void SwitchLookMode(int mode)
        {
            BeginAnimation(LeftProperty, null);

            Rect targetWorkArea = GetTargetWorkArea();
            Left = targetWorkArea.Left;

            double liteModeWidth = ColumnLauncher.ActualWidth;

            switch (mode)
            {
                case 0: // 默认
                    BorderMain.ClearValue(WidthProperty);
                    BorderMain.ClearValue(HorizontalAlignmentProperty);
                    iconSwitchLeft.Visibility = Visibility.Visible;
                    iconSwitchRight.Visibility = Visibility.Collapsed;

                    ColumnCanvas.Width = new GridLength(1, GridUnitType.Star);
                    ColumnInfoBoard.Width = new GridLength(1, GridUnitType.Star);
                    ColumnClock.Width = GridLength.Auto;

                    SwitchFrameInfoPage();
                    _frameInfoNavigationTimer.Start();

                    Width = targetWorkArea.Width / 2;
                    break;

                case 1: // 简约（顶部为时钟）
                    BorderMain.Width = liteModeWidth;
                    BorderMain.HorizontalAlignment = HorizontalAlignment.Right;
                    iconSwitchLeft.Visibility = Visibility.Collapsed;
                    iconSwitchRight.Visibility = Visibility.Collapsed;

                    ColumnCanvas.Width = new GridLength(0);
                    ColumnInfoBoard.Width = new GridLength(0);
                    ColumnClock.Width = new GridLength(1, GridUnitType.Star);

                    _frameInfoNavigationTimer.Stop();
                    if (_frameInfoPages.Count > 0) FrameInfo.Navigate(_frameInfoPages[0]);

                    Width = (liteModeWidth + BorderMain.Margin.Left + BorderMain.Margin.Right) * windowScale.ScaleX;
                    break;

                case 2: // 简约（顶部为看板）
                    BorderMain.Width = liteModeWidth;
                    BorderMain.HorizontalAlignment = HorizontalAlignment.Right;
                    iconSwitchLeft.Visibility = Visibility.Collapsed;
                    iconSwitchRight.Visibility = Visibility.Collapsed;

                    ColumnCanvas.Width = new GridLength(0);
                    ColumnClock.Width = new GridLength(0);
                    ColumnInfoBoard.Width = new GridLength(1, GridUnitType.Star);

                    SwitchFrameInfoPage();
                    _frameInfoNavigationTimer.Start();

                    Width = (liteModeWidth + BorderMain.Margin.Left + BorderMain.Margin.Right) * windowScale.ScaleX;
                    break;
            }

            UpdateLayout();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                Left = targetWorkArea.Left + (targetWorkArea.Width - ActualWidth);
                Top = targetWorkArea.Top;
                Height = targetWorkArea.Height;
            }), DispatcherPriority.Render);
        }

        public static bool GetIsLightTheme()
        {
            if (Settings.Look.Theme == 1 || (Settings.Look.Theme == 0 && IsSystemThemeLight())) return true;
            else return false;
        }

        public static void CheckUpdate()
        {
            Process.Start("https://github.com/JoshZ-zzx/Blackboard-Sticker/releases");
        }
        #endregion
    }
}
