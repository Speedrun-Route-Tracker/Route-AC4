using System.IO;
using Route_Plugin_Base;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    public class AC4Plugin : IPluginBase
    {
        private const string ProcessName = "AC4BFSP";
        private const string RouteFileName = "ac4_route_plugin_v7.tsv";

        private static readonly IReadOnlyDictionary<string, object> NoStats = new Dictionary<string, object>();

        private readonly GameConnection _connection = new(ProcessName);
        private GameStats? _gameStats;
        private Ac4SaveReader? _saveReader;
        private Ac4Settings? _settings;
        private IReadOnlyDictionary<string, object> _lastStats = NoStats;
        private Ac4Borderless? _borderless;

        // Route file state: what is loaded, and what the file looked like when it was loaded.
        private string _loadedRouteFile = "";
        private bool _routeHasConditions;
        private (DateTime Written, long Length) _loadedStamp;
        private (DateTime Written, long Length) _seenStamp;
        private DateTime _lastRouteCheckUtc = DateTime.MinValue;

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
                if (!_routeHasConditions && IsInitialized)
                    status += " - save not read (route has no AC4 columns)";
                return _borderless is { Status.Length: > 0 } ? $"{status} - {_borderless.Status}" : status;
            }
        }
        public override IReadOnlyDictionary<string, object> Stats => _lastStats;

        private static readonly string[] OnOff = ["Off", "On"];
        private static readonly TimeSpan RouteCheckInterval = TimeSpan.FromSeconds(1);
        private static readonly string[] SlotNames = ["Top slot", "Middle slot", "Bottom slot"];

        private static string PluginDirectory => Path.GetDirectoryName(typeof(AC4Plugin).Assembly.Location) is { Length: > 0 } dir
            ? dir
            : AppContext.BaseDirectory;

        private static string RoutesDirectory => Path.Combine(PluginDirectory, "Routes");

        /// <summary>The route file to use: the chosen one if it exists, otherwise the one that comes with the plugin.</summary>
        private string CurrentRouteFile
        {
            get
            {
                string chosen = Settings.RouteFile.Trim();
                return chosen.Length > 0 && File.Exists(Path.Combine(RoutesDirectory, chosen)) ? chosen : RouteFileName;
            }
        }

        private static string[] RouteFileChoices(string current)
        {
            List<string> files = [];
            try
            {
                if (Directory.Exists(RoutesDirectory))
                    files.AddRange(Directory.EnumerateFiles(RoutesDirectory, "*.tsv").Select(f => Path.GetFileName(f)!));
            }
            catch (IOException)
            {
            }

            if (!files.Contains(current, StringComparer.OrdinalIgnoreCase))
                files.Add(current);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return [.. files];
        }

        // Loaded on first use so the options window works before the plugin has been started.
        private Ac4Settings Settings => _settings ??= Ac4Settings.Load(PluginDirectory, Log);

        public override IReadOnlyList<PluginOption> Options =>
        [
            new PluginOption("slot", "Save slot", PluginOptionKind.Choice, SlotNames[Math.Clamp(Settings.Slot, 0, 2)],
                "Which of the three save slots in the game's load menu to follow. The newest save of that slot is used.",
                SlotNames),
            new PluginOption("saveFolder", "Save folder", PluginOptionKind.Folder, Settings.SaveFolder,
                "Leave empty to find the folder automatically. Set it if you keep your saves somewhere else."),
            new PluginOption("routeFile", "Route", PluginOptionKind.Choice, CurrentRouteFile,
                "The route file to track, from the Routes folder next to the plugin. A route without all the AC4 columns is tracked by hand and the save is not read.",
                RouteFileChoices(CurrentRouteFile)),
            new PluginOption("autoReload", "Auto reload route", PluginOptionKind.Choice, Settings.AutoReloadRoute ? "On" : "Off",
                "Reload the route by itself when its file is saved. Entries you completed or skipped stay as they are.",
                OnOff),
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
                    case "routeFile":
                        Settings.RouteFile = option.Value.Trim();
                        break;
                    case "autoReload":
                        Settings.AutoReloadRoute = option.Value == "On";
                        break;
                    case "borderless":
                        Settings.Borderless = option.Value == "On";
                        Borderless.ResetFailure();
                        break;
                }
            }

            Settings.Save(PluginDirectory, Log);
            _saveReader?.Reset();
            if (IsInitialized)
                LoadRoute(force: false);
            Log($"Options changed: {SlotNames[Settings.Slot]}, folder '{(Settings.SaveFolder.Length == 0 ? "automatic" : Settings.SaveFolder)}'.");
        }

        private Ac4Borderless Borderless => _borderless ??= new Ac4Borderless(Log);

        public override void Initialize()
        {
            string pluginDirectory = PluginDirectory;

            _loadedRouteFile = "";
            Route.Clear();
            LoadRoute(force: true);

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

        // Reads the chosen route file and swaps it in. Entries that exist in both routes (same name and type) keep their
        // skipped and manual state, so an edit of the file or a switch to another route does not lose what you did by hand.
        private void LoadRoute(bool force)
        {
            string file = CurrentRouteFile;
            bool sameFile = file.Equals(_loadedRouteFile, StringComparison.OrdinalIgnoreCase);
            if (!force && sameFile && !RouteFileChanged())
                return;

            Ac4RouteLoadResult? result = TryLoad(file);
            if (result == null || result.Error != null)
            {
                // Not usable (still being saved, no Name column, ...): keep what is showing and look again only when the file changes.
                _seenStamp = _loadedStamp = StampOf(Path.Combine(RoutesDirectory, file));
                Log($"Could not use {file}{(result?.Error != null ? $": {result.Error}" : "")}. Keeping the current route.");

                if (Route.Count == 0 && !file.Equals(RouteFileName, StringComparison.OrdinalIgnoreCase))
                {
                    Settings.RouteFile = "";
                    result = TryLoad(RouteFileName);
                    file = RouteFileName;
                }

                if (result == null || result.Error != null)
                    return;
            }

            if (result.Parts.Count == 0 && Route.Count > 0)
            {
                _seenStamp = _loadedStamp = StampOf(Path.Combine(RoutesDirectory, file));
                Log($"{file} has no entries (it may still be saving), keeping the current route.");
                return;
            }

            CarryOverState(Route, result.Parts, result.HasConditions);

            Route.Clear();
            Route.AddRange(result.Parts);
            _loadedRouteFile = file;
            _routeHasConditions = result.HasConditions;
            _loadedStamp = _seenStamp = StampOf(Path.Combine(RoutesDirectory, file));
            Log($"Loaded {Route.Count} route entries from {file}{(result.HasConditions ? "" : " (hand tracking, save not read)")}.");

            if (result.HasConditions && _saveReader?.Snapshot is { } save)
            {
                foreach (RoutePart part in Route)
                    part.ApplyGameCompletion(part is Ac4RoutePart ac4Part && ac4Part.Parsed.IsMet(save));
            }
        }

        private Ac4RouteLoadResult? TryLoad(string file)
        {
            try
            {
                return Ac4RouteLoader.LoadFile(Path.Combine(RoutesDirectory, file), Log);
            }
            catch (IOException ex)
            {
                Log($"Could not read {file}: {ex.Message}");
                return null;
            }
        }

        private static (DateTime, long) StampOf(string path)
        {
            try
            {
                FileInfo info = new(path);
                return info.Exists ? (info.LastWriteTimeUtc, info.Length) : default;
            }
            catch (IOException)
            {
                return default;
            }
        }

        // True when the loaded file was saved again. It must look the same on two checks in a row, so a file an editor is
        // still writing is not read halfway.
        private bool RouteFileChanged()
        {
            (DateTime, long) now = StampOf(Path.Combine(RoutesDirectory, _loadedRouteFile));
            if (now == _loadedStamp)
            {
                _seenStamp = now;
                return false;
            }

            bool steady = now == _seenStamp;
            _seenStamp = now;
            return steady;
        }

        // Match old and new parts by name and type (not by position, so inserting a row does not shift anything).
        // A completed entry is kept when the new route is checked against the save (the save decides it again anyway)
        // or when it was completed by hand.
        private static void CarryOverState(List<RoutePart> oldParts, List<RoutePart> newParts, bool newHasConditions)
        {
            Dictionary<string, Queue<RoutePart>> byKey = [];
            foreach (RoutePart part in oldParts)
            {
                string key = $"{part.Name}|{part.Type}";
                if (!byKey.TryGetValue(key, out Queue<RoutePart>? queue))
                    byKey[key] = queue = new Queue<RoutePart>();
                queue.Enqueue(part);
            }

            foreach (RoutePart part in newParts)
            {
                if (byKey.TryGetValue($"{part.Name}|{part.Type}", out Queue<RoutePart>? queue) && queue.Count > 0)
                {
                    RoutePart old = queue.Dequeue();
                    part.IsSkipped = old.IsSkipped;
                    part.IsManuallyCompleted = old.IsManuallyCompleted;
                    part.IsCompleted = old.IsManuallyCompleted || (newHasConditions && old.IsCompleted);
                }
            }
        }

        public override void Tick()
        {
            // Checked before the game connection so editing the route works while the game is closed.
            if (Settings.AutoReloadRoute && DateTime.UtcNow - _lastRouteCheckUtc >= RouteCheckInterval)
            {
                _lastRouteCheckUtc = DateTime.UtcNow;
                LoadRoute(force: false);
            }

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

            if (_routeHasConditions)
                _saveReader.Refresh();
            Ac4SaveSnapshot? save = _saveReader.Snapshot;

            if (save != null && _routeHasConditions)
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
