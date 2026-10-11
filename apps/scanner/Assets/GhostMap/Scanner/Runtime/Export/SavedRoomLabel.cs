using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GhostMap.Scanner.Export
{
    /// <summary>
    /// Task R7: one line per saved room, as plan section 19 shows them:
    /// <c>Bedroom · 11 Oct, 14:30</c>. Read back from the bundle's file name
    /// (<see cref="ExportBundleBuilder.FileNameFor"/>), so no index file is
    /// needed beside the zips.
    /// </summary>
    public static class SavedRoomLabel
    {
        private static readonly Regex Pattern = new Regex(
            @"^GhostMap-(?<room>.+)-(?<date>\d{8})-(?<time>\d{4})(-\d+)?\.zip$",
            RegexOptions.CultureInvariant);

        public static string Describe(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return string.Empty;
            }

            Match match = Pattern.Match(fileName);

            if (!match.Success ||
                !DateTime.TryParseExact(
                    match.Groups["date"].Value + match.Groups["time"].Value,
                    "yyyyMMddHHmm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime when))
            {
                return fileName;
            }

            string room = match.Groups["room"].Value.Replace('_', ' ');
            return $"{room} · {when.ToString("d MMM, HH:mm", CultureInfo.InvariantCulture)}";
        }
    }
}
