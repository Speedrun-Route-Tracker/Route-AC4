using Route_Plugin_Base;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    public class AC4Plugin : IPluginBase
    {
        private const string ProcessName = "AC4BFSP";
        private const string RouteFileName = "AC4 100 % Route - Main Route.tsv";

        private static readonly IReadOnlyDictionary<string, object> NoStats = new Dictionary<string, object>();

        private readonly GameConnection _connection = new(ProcessName);
        private GameStats? _gameStats;
        private IReadOnlyDictionary<string, object> _lastStats = NoStats;

        public AC4Plugin()
        {
            _connection.Log += Log;
            _connection.Disconnected += () =>
            {
                _gameStats?.Dispose();
                _gameStats = null;
                _lastStats = NoStats;
            };
        }

        public override string Name => "Assassin's Creed 4";
        public override string GameName => "Assassin's Creed 4: Black Flag";
        public override Version Version => typeof(AC4Plugin).Assembly.GetName().Version ?? new Version(1, 0, 0);

        public override bool IsConnected => _connection.IsConnected;
        public override string Status => _connection.Status;
        public override IReadOnlyDictionary<string, object> Stats => _lastStats;

        public override void Initialize()
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(AC4Plugin).Assembly.Location) is { Length: > 0 } dir
                ? dir
                : AppContext.BaseDirectory;

            Route.Clear();
            Route.AddRange(Ac4RouteLoader.Load(Path.Combine(pluginDirectory, "Routes", RouteFileName), Log));
            Log($"Loaded {Route.Count} route entries.");

            IsInitialized = true;
        }

        public override void Shutdown()
        {
            _connection.Disconnect();
            IsInitialized = false;
        }

        public override void Tick()
        {
            if (!_connection.EnsureConnected())
                return;

            _gameStats ??= new GameStats(_connection.Memory!);

            Dictionary<string, object> stats = _gameStats.GetStatsAsDictionary();
            _lastStats = stats;

            // Pointers are not valid on loading screens and in the main menu. Reading them then would
            // look like every collectible was lost, so leave the route alone until gameplay resumes.
            (bool isLoading, bool isMainMenu) = _gameStats.GetGameStatus();
            if (isLoading || isMainMenu)
                return;

            bool gameFinished = stats.TryGetValue("Exact Percentage", out object? percent)
                && percent is float exact
                && Math.Round(exact, 2) >= 100f;

            foreach (RoutePart part in Route)
            {
                bool done = gameFinished || (part is Ac4RoutePart ac4Part && IsComplete(ac4Part, stats, _gameStats));
                part.ApplyGameCompletion(done);
            }
        }

        // Collectibles that are tracked per location (chest in a specific area, viewpoint in a specific area, ...)
        private static readonly Dictionary<string, Func<GameStats, string, int, bool>> LocationCheckers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Chest"] = (stats, location, condition) => stats.LocationChestCounts.TryGetValue(location, out int count) && count >= condition,
            ["Animus Fragment"] = (stats, location, condition) => stats.LocationFragmentCounts.TryGetValue(location, out int count) && count >= condition,
            ["Taverns"] = (stats, location, condition) => stats.LocationTavernCounts.TryGetValue(location, out int count) && count >= condition,
            ["Treasure Map"] = (stats, location, condition) => stats.LocationTreasureMapCounts.TryGetValue(location, out int count) && count >= condition,
            ["Viewpoint"] = (stats, location, condition) => stats.LocationViewpointsCounts.TryGetValue(location, out int count) && count >= condition
        };

        // Route file type -> key in the stats dictionary
        private static readonly Dictionary<string, string> StatKeyMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Story"] = "Story Missions",
            ["Myan Stones"] = "Myan Stones",
            ["Burired Treasure"] = "Buried Treasure",  // the route file really is spelled "Burired"
            ["Assassin Contracts"] = "Assassin Contracts",
            ["Naval Contracts"] = "Naval Contracts",
            ["Letters"] = "Letter Bottles",
            ["Manuscripts"] = "Manuscripts",
            ["Shanty"] = "Music Sheets",
            ["Forts"] = "Forts",
            ["Legendary Ships"] = "Legendary Ships",
            ["Templar Hunts"] = "Templar Hunts",
            ["Upgrades"] = "Hero Upgrades",
            ["Modern Day"] = "ModernDay Missions"
        };

        private static bool IsComplete(Ac4RoutePart part, Dictionary<string, object> stats, GameStats gameStats)
        {
            string type = part.Type.Trim();

            if (!string.IsNullOrEmpty(part.Location) && LocationCheckers.TryGetValue(type, out var locationChecker))
                return locationChecker(gameStats, part.Location, part.LocationCondition);

            if (StatKeyMappings.TryGetValue(type, out string? statKey))
                return stats.TryGetValue(statKey, out object? value) && value is int count && count >= part.Condition;

            return false;
        }
    }
}
