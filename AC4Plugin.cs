using System.IO;
using Route_Plugin_Base;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    public class AC4Plugin : IPluginBase
    {
        private const string ProcessName = "AC4BFSP";
        private const string RouteFileName = "ac4_route_plugin_v5.tsv";

        private static readonly IReadOnlyDictionary<string, object> NoStats = new Dictionary<string, object>();

        private readonly GameConnection _connection = new(ProcessName);
        private GameStats? _gameStats;
        private Ac4SaveReader? _saveReader;
        private Ac4Settings? _settings;
        private IReadOnlyDictionary<string, object> _lastStats = NoStats;
        private Ac4Borderless? _borderless;

        public AC4Plugin()
        {
            _connection.Log += Log;
            _connection.Disconnected += () =>
            {
                _gameStats?.Dispose();
                _gameStats = null;
                _lastStats = NoStats;
                _borderless?.Forget(); // the game is gone and took the patch with it
            };
        }

        public override string Name => "Assassin's Creed 4";
        public override string GameName => "Assassin's Creed 4: Black Flag";
        public override Version Version => typeof(AC4Plugin).Assembly.GetName().Version ?? new Version(1, 0, 0);

        public override bool IsConnected => _connection.IsConnected;
        public override string Status
        {
            get
            {
                string status = _connection.IsConnected && _saveReader != null
                    ? $"{_connection.Status} - save: {_saveReader.Status}"
                    : _connection.Status;
                return _borderless is { Status.Length: > 0 } ? $"{status} - {_borderless.Status}" : status;
            }
        }
        public override IReadOnlyDictionary<string, object> Stats => _lastStats;

        private static readonly string[] OnOff = ["Off", "On"];
        private static readonly string[] SlotNames = ["Top slot", "Middle slot", "Bottom slot"];

        private static string PluginDirectory => Path.GetDirectoryName(typeof(AC4Plugin).Assembly.Location) is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory;

        // Loaded on first use so the options window works before the plugin has been started.
        private Ac4Settings Settings => _settings ??= Ac4Settings.Load(PluginDirectory, Log);

        public override IReadOnlyList<PluginOption> Options =>
        [
            new PluginOption("slot", "Save slot", PluginOptionKind.Choice, SlotNames[Math.Clamp(Settings.Slot, 0, 2)],
                "Which of the three save slots in the game's load menu to follow. The newest save of that slot is used.",
                SlotNames),
            new PluginOption("saveFolder", "Save folder", PluginOptionKind.Folder, Settings.SaveFolder,
                "Leave empty to find the folder automatically. Set it if you keep your saves somewhere else."),
            new PluginOption("borderless", "Borderless window", PluginOptionKind.Choice, Settings.Borderless ? "On" : "Off",
                "Removes the game's window border while the tracker is connected (needs the tracker run as administrator). " +
                "It is undone when you turn it off or close the tracker, and disappears on its own when the game closes.",
                OnOff)
        ];

        public override void ApplyOptions(IReadOnlyList<PluginOption> options)
        {
            foreach (PluginOption option in options)
            {
                switch (option.Key)
                {
                    case "slot":
                        Settings.Slot = Math.Max(0, Array.IndexOf(SlotNames, option.Value));
                        break;
                    case "saveFolder":
                        Settings.SaveFolder = option.Value.Trim();
                        break;
                    case "borderless":
                        Settings.Borderless = option.Value == "On";
                        Borderless.ResetFailure();
                        break;
                }
            }

            Settings.Save(PluginDirectory, Log);
            _saveReader?.Reset();
            Log($"Options changed: {SlotNames[Settings.Slot]}, folder '{(Settings.SaveFolder.Length == 0 ? "automatic" : Settings.SaveFolder)}'.");
        }

        private Ac4Borderless Borderless => _borderless ??= new Ac4Borderless(Log);

        public override void Initialize()
        {
            string pluginDirectory = PluginDirectory;

            Route.Clear();
            Route.AddRange(Ac4RouteLoader.Load(Path.Combine(pluginDirectory, "Routes", RouteFileName), Log));
            Log($"Loaded {Route.Count} route entries.");

            _saveReader = new Ac4SaveReader(Settings, Log);

            IsInitialized = true;
        }

        public override void Shutdown()
        {
            // Put the game back as it was before letting go of it, in case it keeps running after the tracker closes.
            _borderless?.RestoreNow(_connection.Memory);
            _connection.Disconnect();
            _saveReader = null;
            IsInitialized = false;
        }

        public override void Tick()
        {
            if (!_connection.EnsureConnected())
                return;

            _gameStats ??= new GameStats(_connection.Memory!);

            Borderless.Update(_connection.Memory!, Settings.Borderless);

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

            if (save != null)
            {
                foreach (RoutePart part in Route)
                    part.ApplyGameCompletion(part is Ac4RoutePart ac4Part && ac4Part.Parsed.IsMet(save));
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
