using System.IO;
using Route_Plugin_Base;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    public class AC4Plugin : IPluginBase
    {
        private const string ProcessName = "AC4BFSP";
        private const string RouteFileName = "ac4_route_plugin_v2.tsv";

        private static readonly IReadOnlyDictionary<string, object> NoStats = new Dictionary<string, object>();

        private readonly GameConnection _connection = new(ProcessName);
        private GameStats? _gameStats;
        private Ac4SaveReader? _saveReader;
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
        public override string Status => _connection.IsConnected && _saveReader != null
            ? $"{_connection.Status} - save: {_saveReader.Status}"
            : _connection.Status;
        public override IReadOnlyDictionary<string, object> Stats => _lastStats;

        public override void Initialize()
        {
            string pluginDirectory = Path.GetDirectoryName(typeof(AC4Plugin).Assembly.Location) is { Length: > 0 } dir
                ? dir
                : AppContext.BaseDirectory;

            Route.Clear();
            Route.AddRange(Ac4RouteLoader.Load(Path.Combine(pluginDirectory, "Routes", RouteFileName), Log));
            Log($"Loaded {Route.Count} route entries.");

            Ac4Settings settings = Ac4Settings.Load(pluginDirectory, Log);
            _saveReader = new Ac4SaveReader(settings, Log);

            IsInitialized = true;
        }

        public override void Shutdown()
        {
            _connection.Disconnect();
            _saveReader = null;
            IsInitialized = false;
        }

        public override void Tick()
        {
            if (!_connection.EnsureConnected())
                return;

            _gameStats ??= new GameStats(_connection.Memory!);

            Dictionary<string, object> stats = _gameStats.GetStatsAsDictionary();

            // The save is only trusted during gameplay: while the game loads or sits in the main menu the file may be
            // mid-write or belong to another slot, so the route is left alone until gameplay resumes.
            (bool isLoading, bool isMainMenu) = _gameStats.GetGameStatus();
            if (isLoading || isMainMenu || _saveReader == null)
            {
                _lastStats = stats;
                return;
            }

            _saveReader.Refresh();
            Ac4SaveSnapshot? save = _saveReader.Snapshot;

            bool gameFinished = stats.TryGetValue("Exact Percentage", out object? percent)
                && percent is float exact
                && Math.Round(exact, 2) >= 100f;

            if (save != null || gameFinished)
            {
                foreach (RoutePart part in Route)
                {
                    bool done = gameFinished || (part is Ac4RoutePart ac4Part && save != null && ac4Part.Parsed.IsMet(save));
                    part.ApplyGameCompletion(done);
                }
            }

            AddRouteCounts(stats);
            _lastStats = stats;
        }

        // Adds "done / total" per route type to the stats window.
        private void AddRouteCounts(Dictionary<string, object> stats)
        {
            Dictionary<string, (int Done, int Total)> counts = new(StringComparer.OrdinalIgnoreCase);
            foreach (RoutePart part in Route)
            {
                string type = part.Type.Trim();
                if (string.IsNullOrEmpty(type) || part.IsSkipped)
                    continue;

                counts.TryGetValue(type, out (int Done, int Total) current);
                counts[type] = (current.Done + (part.IsCompleted ? 1 : 0), current.Total + 1);
            }

            foreach ((string type, (int done, int total)) in counts)
                stats[type] = $"{done} / {total}";
        }
    }
}
