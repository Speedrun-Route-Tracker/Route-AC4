using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Route_AC4
{
    internal enum Ac4ConditionKind
    {
        /// <summary>"0x... started (entry exists in save)": the mission has an entry in the save.</summary>
        Started,
        /// <summary>"0x... sync>=100 [name]": the mission's highest synchronization is at least the number.</summary>
        Sync,
        /// <summary>"0x... collected [name]": the item is in the gained inventory items.</summary>
        Collected,
        /// <summary>"0x... accomplished [name]": the accomplishment is completed.</summary>
        Accomplished
    }

    internal readonly record struct Ac4Clause(Ac4ConditionKind Kind, ulong Id, int MinSync);

    /// <summary>
    ///  The Condition column of the route file. It is free text with a fixed beginning: an 0x ID followed by what to
    ///  check. Anything after that (names in [brackets], notes in (parentheses)) is for people and is ignored.
    ///  Several clauses separated by commas must all be true.
    /// </summary>
    internal sealed partial class Ac4Condition
    {
        public static Ac4Condition Empty { get; } = new([], false);

        private Ac4Condition(IReadOnlyList<Ac4Clause> clauses, bool isValid)
        {
            Clauses = clauses;
            IsValid = isValid;
        }

        public IReadOnlyList<Ac4Clause> Clauses { get; }

        /// <summary>False when the text could not be read. Such a condition is never met.</summary>
        public bool IsValid { get; }

        public static Ac4Condition Parse(string text)
        {
            List<Ac4Clause> clauses = [];
            foreach (string part in SplitClauses(text))
            {
                Match m = ClauseRegex().Match(part);
                if (!m.Success || !ulong.TryParse(m.Groups["id"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id))
                    return new Ac4Condition([], false);

                if (m.Groups["started"].Success)
                    clauses.Add(new Ac4Clause(Ac4ConditionKind.Started, id, 0));
                else if (m.Groups["sync"].Success)
                    clauses.Add(new Ac4Clause(Ac4ConditionKind.Sync, id, int.Parse(m.Groups["sync"].Value, CultureInfo.InvariantCulture)));
                else if (m.Groups["collected"].Success)
                    clauses.Add(new Ac4Clause(Ac4ConditionKind.Collected, id, 0));
                else
                    clauses.Add(new Ac4Clause(Ac4ConditionKind.Accomplished, id, 0));
            }

            return clauses.Count == 0 ? new Ac4Condition([], false) : new Ac4Condition(clauses, true);
        }

        public bool IsMet(Ac4SaveSnapshot save)
        {
            if (!IsValid)
                return false;

            foreach (Ac4Clause clause in Clauses)
            {
                bool met = clause.Kind switch
                {
                    Ac4ConditionKind.Started => save.MissionSync.ContainsKey(clause.Id),
                    Ac4ConditionKind.Sync => save.MissionSync.TryGetValue(clause.Id, out int sync) && sync >= clause.MinSync,
                    Ac4ConditionKind.Collected => save.GainedItems.Contains(clause.Id),
                    Ac4ConditionKind.Accomplished => save.CompletedAccomplishments.Contains(clause.Id),
                    _ => false
                };
                if (!met)
                    return false;
            }

            return true;
        }

        // Splits on commas that are not inside [brackets] or (parentheses), so notes may contain commas.
        private static List<string> SplitClauses(string text)
        {
            List<string> parts = [];
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c is '[' or '(')
                    depth++;
                else if (c is ']' or ')')
                    depth = Math.Max(0, depth - 1);
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text[start..i].Trim());
                    start = i + 1;
                }
            }

            parts.Add(text[start..].Trim());
            parts.RemoveAll(string.IsNullOrEmpty);
            return parts;
        }

        [GeneratedRegex(@"^0x(?<id>[0-9A-Fa-f]+)\s+(?:(?<started>started)|sync>=(?<sync>\d+)|(?<collected>collected)|(?<accomplished>accomplished))")]
        private static partial Regex ClauseRegex();
    }
}
