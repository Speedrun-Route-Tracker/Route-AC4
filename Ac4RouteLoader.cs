using System.IO;
using Route_Plugin_Base;

namespace Route_AC4
{
    /// <summary>
    ///  Reads the AC4 route TSV: Name, Type, Condition, Coordinates, Location, Total Counters, Total Counters Location.
    ///  A route name may contain line breaks, so short lines are merged with the lines that follow until
    ///  all seven columns are present.
    /// </summary>
    internal static class Ac4RouteLoader
    {
        private const int ExpectedColumns = 7;
        private const int MergeLimit = 50;

        public static List<RoutePart> Load(string path, Action<string> log)
        {
            List<RoutePart> parts = [];

            if (!File.Exists(path))
            {
                log($"Route file not found: {path}");
                return parts;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                int startLine = i + 1;
                string buffer = lines[i];
                string[] cells = buffer.Split('\t');

                int merged = 0;
                while (cells.Length < ExpectedColumns && i + 1 < lines.Length && merged < MergeLimit)
                {
                    i++;
                    merged++;
                    buffer += Environment.NewLine + lines[i];
                    cells = buffer.Split('\t');
                }

                if (cells.Length < ExpectedColumns)
                {
                    log($"Skipping route entry at line {startLine}: not enough columns.");
                    continue;
                }

                string name = cells[0].Trim();
                if (string.IsNullOrEmpty(name) || name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    continue; // blank name or the header row

                if (!int.TryParse(cells[5].Trim(), out int totalCounters))
                {
                    log($"Skipping route entry at line {startLine}: invalid total counters '{cells[5].Trim()}'.");
                    continue;
                }

                if (!int.TryParse(cells[6].Trim(), out int totalCountersLocation))
                {
                    log($"Skipping route entry at line {startLine}: invalid total counters location '{cells[6].Trim()}'.");
                    continue;
                }

                string conditionText = cells[2].Trim();
                Ac4Condition condition = Ac4Condition.Parse(conditionText);
                if (!condition.IsValid)
                    log($"Route entry at line {startLine} ('{name}') has a condition that cannot be read and will never complete: '{conditionText}'.");

                parts.Add(new Ac4RoutePart
                {
                    Name = name,
                    IsCompleted = false,
                    Type = cells[1].Trim(),
                    Coordinates = cells[3].Trim(),
                    Location = cells[4].Trim(),
                    TotalCounters = totalCounters,
                    TotalCountersLocation = totalCountersLocation,
                    Condition = conditionText,
                    Parsed = condition,
                    Id = parts.Count + 1
                });
            }

            return parts;
        }
    }
}
