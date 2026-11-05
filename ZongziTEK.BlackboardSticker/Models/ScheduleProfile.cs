using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ZongziTEK.BlackboardSticker.Models
{
    public class SubjectEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
    }
    public class TimeSlot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public bool IsExercise { get; set; }
        public string FollowingSlotId { get; set; }
    }
    public class DayPlan
    {
        public List<TimeSlot> Slots { get; set; } // null inherits the default
        public Dictionary<string, string> Subjects { get; set; } = new();
    }
    public class DatePlan
    {
        public DateTime Date { get; set; }
        public DayOfWeek TimeSource { get; set; }
        public DayOfWeek CourseSource { get; set; }
        public DayPlan Plan { get; set; } = new();
    }
    public class ScheduleProfile
    {
        public int Version { get; set; } = 1;
        public bool SetupCompleted { get; set; }
        public double DefaultLessonMinutes { get; set; }
        public double ExerciseMinutes { get; set; }
        public double PreparationMinutes { get; set; } = 2;
        public double ReminderLeadSeconds { get; set; } = 30;
        public double DismissalSeconds { get; set; } = 60;
        public List<SubjectEntry> Subjects { get; set; } = new();
        public List<TimeSlot> DefaultSlots { get; set; } = new();
        public Dictionary<DayOfWeek, DayPlan> Week { get; set; } = new();
        public List<DatePlan> Exceptions { get; set; } = new();
        public DayPlan Day(DayOfWeek day)
        {
            if (!Week.ContainsKey(day)) Week[day] = new DayPlan();
            return Week[day];
        }
        public List<TimeSlot> Slots(DayOfWeek day) => Day(day).Slots ?? DefaultSlots;
        public DayPlan Resolve(DateTime date)
        {
            var exception = Exceptions.FirstOrDefault(x => x.Date.Date == date.Date);
            return exception?.Plan ?? new DayPlan { Slots = Slots(date.DayOfWeek), Subjects = Day(date.DayOfWeek).Subjects };
        }
        public string SubjectName(DayPlan plan, TimeSlot slot)
        {
            return plan.Subjects.TryGetValue(slot.Id, out var id) ? Subjects.FirstOrDefault(x => x.Id == id)?.Name ?? "" : "";
        }
        public List<Lesson> Lessons(DateTime date)
        {
            var plan = Resolve(date);
            return plan.Slots.OrderBy(x => x.Start).Select(x => new Lesson(x.IsExercise ? "眼保健操" : SubjectName(plan, x), x.Start, x.End, x.IsExercise, false)).ToList();
        }
        public DatePlan Compose(DateTime date, DayOfWeek times, DayOfWeek courses, out int surplus)
        {
            var slots = Clone(Slots(times));
            var source = Slots(courses).Where(x => !x.IsExercise).OrderBy(x => x.Start).ToList();
            var target = slots.Where(x => !x.IsExercise).OrderBy(x => x.Start).ToList();
            var plan = new DayPlan { Slots = slots };
            for (int i = 0; i < Math.Min(source.Count, target.Count); i++)
                if (Day(courses).Subjects.TryGetValue(source[i].Id, out var subject)) plan.Subjects[target[i].Id] = subject;
            surplus = Math.Max(0, source.Count - target.Count);
            return new DatePlan { Date = date.Date, TimeSource = times, CourseSource = courses, Plan = plan };
        }
        public static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        public void Validate()
        {
            if (Version != 1) throw new InvalidOperationException("不支持此课表版本，请使用匹配版本的软件。");
            if (PreparationMinutes < 0 || ReminderLeadSeconds < 0 || DismissalSeconds < 0 ||
                double.IsNaN(PreparationMinutes + ReminderLeadSeconds + DismissalSeconds) ||
                double.IsInfinity(PreparationMinutes + ReminderLeadSeconds + DismissalSeconds) ||
                PreparationMinutes > 1440 || ReminderLeadSeconds > 86400 || DismissalSeconds > 86400)
                throw new InvalidOperationException("提醒时间必须是有效的非负数，且不超过一天。");
            if (Subjects.Any(x => string.IsNullOrWhiteSpace(x.Name)) || Subjects.Select(x => x.Name.Trim()).Distinct().Count() != Subjects.Count)
                throw new InvalidOperationException("学科名称不能为空或重复。");
            ValidateSlots(DefaultSlots);
            foreach (var day in Week.Values) if (day.Slots != null) ValidateSlots(day.Slots);
            foreach (var day in Exceptions) ValidateSlots(day.Plan.Slots);
        }
        public static void ValidateSlots(List<TimeSlot> slots)
        {
            var ordered = slots.OrderBy(x => x.Start).ToList();
            if (ordered.Select(x => x.Id).Distinct().Count() != ordered.Count) throw new InvalidOperationException("课次标识重复。");
            for (int i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                if (s.Start < TimeSpan.Zero || s.End > TimeSpan.FromDays(1) || s.End <= s.Start || (i > 0 && s.Start < ordered[i - 1].End))
                    throw new InvalidOperationException("时段起止时间无效或存在重叠。");
                if (s.IsExercise && !string.IsNullOrEmpty(s.FollowingSlotId))
                {
                    var next = ordered.FirstOrDefault(x => x.Id == s.FollowingSlotId && !x.IsExercise);
                    if (next == null || next.Start < s.End) throw new InvalidOperationException("眼保健操必须关联其后的教学课次。");
                }
            }
        }
        public static ScheduleProfile Migrate(Timetable old)
        {
            var result = new ScheduleProfile();
            var lists = new[] { old.Sunday, old.Monday, old.Tuesday, old.Wednesday, old.Thursday, old.Friday, old.Saturday };
            for (int d = 0; d < 7; d++)
            {
                var plan = result.Day((DayOfWeek)d); plan.Slots = new();
                foreach (var lesson in lists[d] ?? new List<Lesson>())
                {
                    var slot = new TimeSlot { Start = lesson.StartTime, End = lesson.EndTime };
                    plan.Slots.Add(slot);
                    if (string.IsNullOrWhiteSpace(lesson.Subject)) continue;
                    var subject = result.Subjects.FirstOrDefault(x => x.Name == lesson.Subject);
                    if (subject == null) result.Subjects.Add(subject = new SubjectEntry { Name = lesson.Subject });
                    plan.Subjects[slot.Id] = subject.Id;
                }
            }
            result.DefaultSlots = Clone(result.Slots(DayOfWeek.Monday));
            result.SetupCompleted = lists.Any(x => x != null && x.Count > 0);
            return result;
        }
    }
    public class ReminderState
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public DateTime Until { get; set; }
        public bool BeforeClass { get; set; }
    }
    public static class ReminderEngine
    {
        public static ReminderState Evaluate(ScheduleProfile profile, DateTime now)
        {
            var plan = profile.Resolve(now.Date);
            var slots = plan.Slots.OrderBy(x => x.Start).ToList();
            ReminderState dismissal = null;
            foreach (var slot in slots.Where(x => !x.IsExercise))
            {
                var name = profile.SubjectName(plan, slot);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var start = now.Date + slot.Start;
                var from = start.AddMinutes(-profile.PreparationMinutes).AddSeconds(-profile.ReminderLeadSeconds);
                foreach (var exercise in slots.Where(x => x.IsExercise && (x.FollowingSlotId == slot.Id || (string.IsNullOrEmpty(x.FollowingSlotId) && x.End == slot.Start))))
                    if (now.Date + exercise.Start < from) from = now.Date + exercise.Start;
                if (now >= from && now < start)
                {
                    bool exerciseNow = slots.Any(x => x.IsExercise && (x.FollowingSlotId == slot.Id || (string.IsNullOrEmpty(x.FollowingSlotId) && x.End == slot.Start)) && now.TimeOfDay >= x.Start && now.TimeOfDay < x.End);
                    return new ReminderState { Key = now.ToString("yyyyMMdd") + slot.Id + "begin", Title = (exerciseNow ? "眼保健操｜" : "") + name + "课即将开始", Until = start, BeforeClass = true };
                }
                var end = now.Date + slot.End;
                if (now >= end && now < end.AddSeconds(profile.DismissalSeconds))
                    dismissal = new ReminderState { Key = now.ToString("yyyyMMdd") + slot.Id + "end", Title = name + "课已下课", Until = end.AddSeconds(profile.DismissalSeconds) };
            }
            return dismissal;
        }
    }
}
