using Route_Plugin_Base;
using System.Diagnostics;

namespace Route_AC4
{
    public class AC4Plugin : IGamePlugin
    {
        public string GameName => "Assassin's Creed 4";

        public GameStatsBase Create(
            IntPtr processHandle,
            IntPtr baseAddress)
        {
            return new GameStats(processHandle, baseAddress);
        }

        // ==========MY NOTES==============
        // Centralized mappings to eliminate repetitive code and improve maintainability
        private static readonly Dictionary<string, Func<GameStats, string, int, bool>> LocationCheckers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chest"] = (stats, location, condition) => stats.LocationChestCounts.TryGetValue(location, out int count) && count >= condition,
            ["Animus Fragment"] = (stats, location, condition) => stats.LocationFragmentCounts.TryGetValue(location, out int count) && count >= condition,
            ["Taverns"] = (stats, location, condition) => stats.LocationTavernCounts.TryGetValue(location, out int count) && count >= condition,
            ["Treasure Map"] = (stats, location, condition) => stats.LocationTreasureMapCounts.TryGetValue(location, out int count) && count >= condition,
            ["Viewpoint"] = (stats, location, condition) => stats.LocationViewpointsCounts.TryGetValue(location, out int count) && count >= condition
        };

        private static readonly Dictionary<string, string> StatKeyMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Story"] = "Story Missions",
            ["Myan Stones"] = "Myan Stones",
            ["Burired Treasure"] = "Buried Treasure",  // Keeping your intentional route file typo -> correct stat name
            ["Assassin Contracts"] = "Assassin Contracts",
            ["Naval Contracts"] = "Naval Contracts",
            ["Letters"] = "Letter Bottles",
            ["Manuscripts"] = "Manuscripts",
            ["Shanty"] = "Music Sheets",
            ["Forts"] = "Forts",
            ["Legendary Ships"] = "Legendary Ships",
            ["Templar Hunts"] = "Templar Hunts",
            ["Upgrades"] = "Hero Upgrades"
        };

        public bool CheckCompletion(RouteEntry entry, GameStatsEventArgs stats, GameStatsBase Stats)
        {
            string normalizedType = entry.Type.Trim();
            if (Stats is not Route_AC4.GameStats ac4Stats)
            {
                return false;
            }

            // Handle location-based collectibles using centralized mapping
            if (!string.IsNullOrEmpty(entry.Location) && LocationCheckers.TryGetValue(normalizedType, out var locationChecker))
            {
                bool result = locationChecker(ac4Stats, entry.Location, entry.LocationCondition);
                if (!result)
                {
                    Debug.WriteLine($"Location '{entry.Location}' check failed for {normalizedType} '{entry.Name}' - needed: {entry.LocationCondition}");
                }
                return result;
            }

            // Handle general stat-based completion using centralized mapping
            if (StatKeyMappings.TryGetValue(normalizedType, out string? statKey))
            {
                bool result = stats.GetValue<int>(statKey, 0) >= entry.Condition;
                return result;
            }

            Debug.WriteLine($"Unknown entry type: '{normalizedType}' for entry '{entry.Name}'");
            return false;
        }
    }
}
