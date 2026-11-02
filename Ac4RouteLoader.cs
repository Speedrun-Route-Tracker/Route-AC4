using System.IO;
using Route_Plugin_Base;

namespace Route_AC4
{
    /// <summary>What reading a route file produced.</summary>
    /// <param name="Parts">The route entries.</param>
    /// <param name="HasConditions">
    ///  True when the file has every AC4 column (see <see cref="Ac4RouteLoader.RequiredColumns"/>), so the entries can be
    ///  checked against the save. False for any other route: it is still shown, but only hand tracking works.
    /// </param>
    /// <param name="Error">Why the file could not be used (null when it could).</param>
    internal sealed record Ac4RouteLoadResult(List<RoutePart> Parts, bool HasConditions, string? Error);

    /// <summary>
    ///  Reads a route TSV. The first row is the header and columns are found by name, so their order does not matter.
    ///  Name is required. Type, Coordinates and Location are used when present. When the file has all of Name, Type,
    ///  Condition, Coordinates, Location, Total Counters and Total Counters Location, the entries get conditions that are
    ///  checked against the save file; any other route is tracked by hand only.
    ///  A route name may contain line breaks, so short lines are merged with the lines that follow until
    ///  all columns are present.
    /// </summary>
    internal static class Ac4RouteLoader
    {
        public static readonly string[] RequiredColumns =
            ["Name", "Type", "Condition", "Coordinates", "Location", "Total Counters", "Total Counters Location"];

        private const int MergeLimit = 50;

        /// <summary>Entries of the file only (kept for callers that do not care about the mode).</summary>
        public static List<RoutePart> Load(string path, Action<string> log) => LoadFile(path, log).Parts;

        public static Ac4RouteLoadResult LoadFile(string path, Action<string> log)
        {
            List<RoutePart> parts = [];

            if (!File.Exists(path))
            {
                log($"Route file not found: {path}");
                return new Ac4RouteLoadResult(parts, false, "file not found");
            }

            string[] lines = File.ReadAllLines(path);
            int first = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
            if (first < 0)
                return new Ac4RouteLoadResult(parts, false, "the file is empty");

            string[] header = lines[first].Split('\t').Select(h => h.Trim()).ToArray();
            Dictionary<string, int> column = new(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < header.Length; c++)
                column.TryAdd(header[c], c);

            if (!column.TryGetValue("Name", out int nameColumn))
                return new Ac4RouteLoadResult(parts, false, "the first row has no Name column");

            bool hasConditions = RequiredColumns.All(column.ContainsKey);
            int expectedColumns = header.Length;

            string Cell(string[] cells, string name)
                => column.TryGetValue(name, out int index) && index < cells.Length ? cells[index].Trim() : "";

            for (int i = first + 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                int startLine = i + 1;
                string buffer = lines[i];
                string[] cells = buffer.Split('\t');

                int merged = 0;
                while (cells.Length < expectedColumns && i + 1 < lines.Length && merged < MergeLimit)
                {
                    i++;
                    merged++;
                    buffer += Environment.NewLine + lines[i];
                    cells = buffer.Split('\t');
                }

                if (cells.Length < expectedColumns)
                {
                    log($"Skipping route entry at line {startLine}: not enough columns.");
                    continue;
                }

                string name = cells[nameColumn].Trim();
                if (string.IsNullOrEmpty(name) || name.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    continue; // blank name or a repeated header row

                Ac4RoutePart part = new()
                {
                    Name = name,
                    IsCompleted = false,
                    Type = Cell(cells, "Type"),
                    Coordinates = Cell(cells, "Coordinates"),
                    Location = Cell(cells, "Location"),
                    Id = parts.Count + 1
                };

                if (hasConditions)
                {
                    if (!int.TryParse(Cell(cells, "Total Counters"), out int totalCounters))
                    {
                        log($"Skipping route entry at line {startLine}: invalid total counters '{Cell(cells, "Total Counters")}'.");
                        continue;
                    }

                    if (!int.TryParse(Cell(cells, "Total Counters Location"), out int totalCountersLocation))
                    {
                        log($"Skipping route entry at line {startLine}: invalid total counters location '{Cell(cells, "Total Counters Location")}'.");
                        continue;
                    }

                    string conditionText = Cell(cells, "Condition");
                    Ac4Condition condition = Ac4Condition.Parse(conditionText);
                    if (!condition.IsValid)
                        log($"Route entry at line {startLine} ('{name}') has a condition that cannot be read and will never complete: '{conditionText}'.");

                    part.TotalCounters = totalCounters;
                    part.TotalCountersLocation = totalCountersLocation;
                    part.Condition = conditionText;
                    part.Parsed = condition;
                }

                parts.Add(part);
            }

            if (!hasConditions)
            {
                string missing = string.Join(", ", RequiredColumns.Where(c => !column.ContainsKey(c)));
                log($"{Path.GetFileName(path)} is missing the columns {missing}, so it is tracked by hand and the save file is not read.");
            }

            return new Ac4RouteLoadResult(parts, hasConditions, null);
        }
    }
}
