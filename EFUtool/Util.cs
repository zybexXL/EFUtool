using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EFUtool
{
    public static class Util
    {
        // convert Unix Epoch timestamp to .Net Datetime (seconds since 1970)
        public static DateTime FromUnixTime(int unixTime)
        {
            if (unixTime == 0) return DateTime.MinValue;
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unixTime);
        }

        public static uint ToUnixTime(DateTime date)
        {
            DateTime origin = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (date <= origin) return 0;

            try     // might still overflow? (negative)
            {
                date = date.AddTicks(-(date.Ticks % TimeSpan.TicksPerSecond)); // truncate ms
                return Convert.ToUInt32(date.Subtract(origin).TotalSeconds);
            }
            catch { }
            return 0;
        }

        public static bool DateEqualsDST(DateTime date1, DateTime date2)
        {
            if (date1 == date2) return true;
            if (Math.Abs(date1.Ticks - date2.Ticks) < TimeSpan.TicksPerSecond) return true;
            if (Math.Abs(Math.Abs(date1.Ticks - date2.Ticks) - TimeSpan.TicksPerHour) < TimeSpan.TicksPerSecond * 2) return true;
            return false;
        }

        public static bool DateEqualsDST(long date1, long date2)
        {
            if (date1 == date2) return true;
            if (Math.Abs(date1 - date2) < TimeSpan.TicksPerSecond) return true;
            if (Math.Abs(date1 - date2) < TimeSpan.TicksPerHour + TimeSpan.TicksPerSecond * 2) return true;
            return false;
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < (1 << 10)) return $"{bytes} B";
            if (bytes < (1 << 20)) return $"{bytes/1024.0:N2} KB";
            bytes = bytes >> 10;
            if (bytes < (1 << 20)) return $"{bytes / 1024.0:N2} MB";
            bytes = bytes >> 10;
            if (bytes < (1 << 20)) return $"{bytes / 1024.0:N2} GB";
            bytes = bytes >> 10;
            if (bytes < (1 << 20)) return $"{bytes / 1024.0:N2} TB";
            bytes = bytes >> 10;
            if (bytes < (1 << 20)) return $"{bytes / 1024.0:N2} PB";
            bytes = bytes >> 10;
            return $"{bytes / 1024.0:N2} EB";
        }
    }
}
