using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using ZongziTEK.BlackboardSticker.Models;

namespace ZongziTEK.BlackboardSticker.Helpers
{
    public static class ScheduleStore
    {
        public static ScheduleProfile Current { get; private set; }
        public static string ProfilePath => Path.Combine(MainWindow.GetDataPath(), "Schedule.json");
        public static void Initialize()
        {
            if (Current != null) return;
            if (File.Exists(ProfilePath))
            {
                try { Current = Read(ProfilePath); }
                catch (Exception error)
                {
                    if (!File.Exists(ProfilePath + ".bak")) throw new IOException("课表文件损坏，请从备份恢复。", error);
                    Current = Read(ProfilePath + ".bak");
                    System.Windows.MessageBox.Show("课表文件读取失败，已载入上一份备份。原文件仍保留。", "课表恢复");
                }
            }
            else
            {
                var oldPath = Path.Combine(MainWindow.GetDataPath(), MainWindow.TimetableFileName);
                Current = File.Exists(oldPath) ? ScheduleProfile.Migrate(JsonConvert.DeserializeObject<Timetable>(File.ReadAllText(oldPath)) ?? new Timetable()) : new ScheduleProfile();
            }
        }
        private static ScheduleProfile Read(string path)
        {
            var profile = JsonConvert.DeserializeObject<ScheduleProfile>(File.ReadAllText(path)) ?? throw new InvalidDataException("课表为空。");
            profile.Validate(); return profile;
        }
        public static void Save(ScheduleProfile profile)
        {
            profile.Validate();
            AtomicWrite(ProfilePath, JsonConvert.SerializeObject(profile, Formatting.Indented));
            Current = ScheduleProfile.Clone(profile);
        }
        public static void AtomicWrite(string path, string text)
        {
            AtomicTextFile.Write(path, text);
        }
    }
}
