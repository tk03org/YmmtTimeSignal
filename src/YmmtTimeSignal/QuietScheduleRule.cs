using System;
using System.Runtime.Serialization;

namespace YmmtTimeSignal
{
    [DataContract]
    public class QuietScheduleRule
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; } = false;

        [DataMember(Name = "startHour")]
        public int StartHour { get; set; } = 0;

        [DataMember(Name = "startMinute")]
        public int StartMinute { get; set; } = 0;

        [DataMember(Name = "startInclusive")]
        public bool StartInclusive { get; set; } = true;

        [DataMember(Name = "endHour")]
        public int EndHour { get; set; } = 0;

        [DataMember(Name = "endMinute")]
        public int EndMinute { get; set; } = 0;

        [DataMember(Name = "endInclusive")]
        public bool EndInclusive { get; set; } = false;

        [DataMember(Name = "isPinpoint")]
        public bool IsPinpoint { get; set; } = false;

        [DataMember(Name = "isRepeat")]
        public bool IsRepeat { get; set; } = true;

        [DataMember(Name = "activatedAtTicks")]
        public long ActivatedAtTicks { get; set; } = 0;

        public QuietScheduleRule() { }

        public QuietScheduleRule(bool enabled, int startH, int startM, bool startInc, int endH, int endM, bool endInc, bool isPinpoint, bool isRepeat)
        {
            Enabled = enabled;
            StartHour = Math.Max(0, Math.Min(23, startH));
            StartMinute = Math.Max(0, Math.Min(59, startM));
            StartInclusive = startInc;
            EndHour = Math.Max(0, Math.Min(23, endH));
            EndMinute = Math.Max(0, Math.Min(59, endM));
            EndInclusive = endInc;
            IsPinpoint = isPinpoint;
            IsRepeat = isRepeat;
        }

        public void Activate(DateTime now)
        {
            ActivatedAtTicks = now.Ticks;
        }

        public bool IsMutedAt(DateTime now)
        {
            if (!Enabled) return false;

            if (IsPinpoint)
            {
                if (now.Hour != StartHour || now.Minute != StartMinute)
                {
                    return false;
                }

                if (!IsRepeat && ActivatedAtTicks > 0)
                {
                    var activatedAt = new DateTime(ActivatedAtTicks, DateTimeKind.Local);
                    var targetMinute = GetPinpointTargetDateTime(activatedAt);
                    var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local);
                    return currentMinute == targetMinute;
                }

                return true;
            }

            int nowMinutes = now.Hour * 60 + now.Minute;
            int startMinutes = StartHour * 60 + StartMinute;
            int endMinutes = EndHour * 60 + EndMinute;

            if (startMinutes < endMinutes)
            {
                // Normal same-day range: Start < End
                bool inRange = (StartInclusive ? (nowMinutes >= startMinutes) : (nowMinutes > startMinutes))
                            && (EndInclusive ? (nowMinutes <= endMinutes) : (nowMinutes < endMinutes));

                if (!inRange) return false;

                if (!IsRepeat && ActivatedAtTicks > 0)
                {
                    var activatedAt = new DateTime(ActivatedAtTicks, DateTimeKind.Local);
                    var (winStart, winEnd) = GetSameDayWindow(activatedAt, startMinutes, endMinutes);
                    return now >= winStart && now <= winEnd;
                }

                return true;
            }
            else
            {
                // Overnight span or same-time 24h span: Start >= End
                if (!IsRepeat && ActivatedAtTicks > 0)
                {
                    var activatedAt = new DateTime(ActivatedAtTicks, DateTimeKind.Local);
                    var (winStart, winEnd) = GetOvernightWindow(activatedAt, startMinutes, endMinutes);
                    return now >= winStart && now <= winEnd;
                }

                if (startMinutes == endMinutes)
                {
                    // Start == End repeating span: 24h span
                    if (nowMinutes == startMinutes)
                    {
                        return StartInclusive || EndInclusive;
                    }
                    return true;
                }
                else
                {
                    // Start > End repeating overnight
                    bool startMatch = StartInclusive ? (nowMinutes >= startMinutes) : (nowMinutes > startMinutes);
                    bool endMatch = EndInclusive ? (nowMinutes <= endMinutes) : (nowMinutes < endMinutes);
                    return startMatch || endMatch;
                }
            }
        }

        public bool HasExpired(DateTime now)
        {
            if (!Enabled || IsRepeat || ActivatedAtTicks == 0) return false;

            var activatedAt = new DateTime(ActivatedAtTicks, DateTimeKind.Local);

            if (IsPinpoint)
            {
                var targetMinute = GetPinpointTargetDateTime(activatedAt);
                var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local);
                return currentMinute > targetMinute;
            }

            int startMinutes = StartHour * 60 + StartMinute;
            int endMinutes = EndHour * 60 + EndMinute;

            if (startMinutes < endMinutes)
            {
                var (_, winEnd) = GetSameDayWindow(activatedAt, startMinutes, endMinutes);
                return now > winEnd;
            }
            else
            {
                var (_, winEnd) = GetOvernightWindow(activatedAt, startMinutes, endMinutes);
                return now > winEnd;
            }
        }

        private DateTime GetPinpointTargetDateTime(DateTime activatedAt)
        {
            var todayTarget = new DateTime(activatedAt.Year, activatedAt.Month, activatedAt.Day, StartHour, StartMinute, 59, DateTimeKind.Local);
            if (activatedAt <= todayTarget)
            {
                return new DateTime(todayTarget.Year, todayTarget.Month, todayTarget.Day, StartHour, StartMinute, 0, DateTimeKind.Local);
            }
            else
            {
                var tomorrow = todayTarget.AddDays(1);
                return new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, StartHour, StartMinute, 0, DateTimeKind.Local);
            }
        }

        private (DateTime Start, DateTime End) GetSameDayWindow(DateTime activatedAt, int startMin, int endMin)
        {
            var baseDate = activatedAt.Date;
            var winStart = baseDate.AddMinutes(startMin);
            if (!StartInclusive) winStart = winStart.AddSeconds(1);

            var winEnd = baseDate.AddMinutes(endMin);
            if (EndInclusive) winEnd = winEnd.AddSeconds(59);
            else winEnd = winEnd.AddSeconds(-1);

            if (activatedAt > winEnd)
            {
                baseDate = baseDate.AddDays(1);
                winStart = baseDate.AddMinutes(startMin);
                if (!StartInclusive) winStart = winStart.AddSeconds(1);

                winEnd = baseDate.AddMinutes(endMin);
                if (EndInclusive) winEnd = winEnd.AddSeconds(59);
                else winEnd = winEnd.AddSeconds(-1);
            }

            return (winStart, winEnd);
        }

        private (DateTime Start, DateTime End) GetOvernightWindow(DateTime activatedAt, int startMin, int endMin)
        {
            var baseDate = activatedAt.Date;
            if (startMin > endMin)
            {
                // Check if activatedAt is currently in the morning portion of yesterday's span
                var morningEnd = baseDate.AddMinutes(endMin);
                if (EndInclusive) morningEnd = morningEnd.AddSeconds(59);
                else morningEnd = morningEnd.AddSeconds(-1);

                if (activatedAt <= morningEnd)
                {
                    // Belongs to span that started yesterday
                    var prevStart = baseDate.AddDays(-1).AddMinutes(startMin);
                    if (!StartInclusive) prevStart = prevStart.AddSeconds(1);
                    return (prevStart, morningEnd);
                }
            }

            // Starts today or tomorrow
            var winStart = baseDate.AddMinutes(startMin);
            if (!StartInclusive) winStart = winStart.AddSeconds(1);

            var winEnd = baseDate.AddDays(1).AddMinutes(endMin);
            if (EndInclusive) winEnd = winEnd.AddSeconds(59);
            else winEnd = winEnd.AddSeconds(-1);

            return (winStart, winEnd);
        }

        public string GetSummaryText(int slotIndex)
        {
            string repeatStr = IsRepeat ? "繰り返す" : "1回のみ";
            if (IsPinpoint)
            {
                return $"{StartHour:D2}:{StartMinute:D2} のみ停止 ({repeatStr})";
            }
            else
            {
                string sInc = StartInclusive ? "含" : "除";
                string eInc = EndInclusive ? "含" : "除";
                return $"{StartHour:D2}:{StartMinute:D2}[{sInc}] ～ {EndHour:D2}:{EndMinute:D2}[{eInc}] ({repeatStr})";
            }
        }
    }
}
