using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AcSaveLib.Games.Ac4;

namespace Route_AC4
{
    /// <summary>
    ///  Finds the AC4 save of the chosen slot, copies it into our own folder (the live file is never read directly) and
    ///  parses the copy. The file is only copied and parsed again when its modified time changed.
    /// </summary>
    internal sealed partial class Ac4SaveReader
    {
        private const int Ac4LauncherGameId = 437;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

        private readonly Ac4Settings _settings;
        private readonly Action<string> _log;
        private readonly string _copyFolder;
        private readonly Stopwatch _sinceCheck = Stopwatch.StartNew();
        private bool _checkedOnce;

        // title cache so unchanged files are not opened every check
        private readonly Dictionary<string, (DateTime Modified, int Slot, string Time)> _titles = [];

        private string? _currentSource;
        private DateTime _currentModified;

        public Ac4SaveReader(Ac4Settings settings, Action<string> log)
        {
            _settings = settings;
            _log = log;
            _copyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RouteTracker", "AC4", "SaveCopy");
        }

        /// <summary>Latest parsed save of the chosen slot, or null if none was found or readable yet.</summary>
        public Ac4SaveSnapshot? Snapshot { get; private set; }

        /// <summary>Short text for the status line.</summary>
        public string Status { get; private set; } = "looking for a save";

        /// <summary>Forget the loaded save, so the next <see cref="Refresh"/> looks again (after the settings changed).</summary>
        public void Reset()
        {
            Snapshot = null;
            _currentSource = null;
            _checkedOnce = false;
            Status = "looking for a save";
        }

        /// <summary>Check for a new save now and then. Returns true when the snapshot was replaced.</summary>
        public bool Refresh()
        {
            if (_checkedOnce && _sinceCheck.Elapsed < CheckInterval)
                return false;

            _checkedOnce = true;
            _sinceCheck.Restart();

            try
            {
                string? folder = FindSaveFolder();
                if (folder == null)
                {
                    SetStatus("no save folder found");
                    return false;
                }

                string? source = FindSlotFile(folder);
                if (source == null)
                {
                    SetStatus($"no save in the {SlotName(_settings.Slot)} slot");
                    return false;
                }

                DateTime modified = File.GetLastWriteTimeUtc(source);
                if (source == _currentSource && modified == _currentModified && Snapshot != null)
                    return false;

                Directory.CreateDirectory(_copyFolder);
                string copy = Path.Combine(_copyFolder, $"slot{_settings.Slot}.save");
                File.Copy(source, copy, overwrite: true);

                Ac4SaveSnapshot snapshot = Ac4SaveSnapshot.From(Ac4SaveFile.Load(copy));
                Snapshot = snapshot;
                _currentSource = source;
                _currentModified = modified;
                SetStatus($"{SlotName(_settings.Slot)} slot save loaded");
                return true;
            }
            catch (Exception ex)
            {
                // The game may be writing the file right now. Keep the last good snapshot and try again later.
                SetStatus($"save could not be read ({ex.GetType().Name})");
                _log($"Reading the save failed: {ex.Message}");
                _currentSource = null;
                return false;
            }
        }

        private void SetStatus(string text)
        {
            if (Status == text)
                return;
            Status = text;
            _log($"Save: {text}");
        }

        public static string SlotName(int slot) => slot switch { 0 => "top", 1 => "middle", _ => "bottom" };

        private string? FindSlotFile(string folder)
        {
            string? best = null;
            string bestTime = "";

            foreach (string file in Directory.EnumerateFiles(folder, "*.save"))
            {
                // Only files ending in exactly .save are saves (.upload and .delete files are not).
                if (!file.EndsWith(".save", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryReadTitle(file, out int slot, out string time) || slot != _settings.Slot)
                    continue;

                if (best == null || string.CompareOrdinal(time, bestTime) > 0)
                {
                    best = file;
                    bestTime = time;
                }
            }

            return best;
        }

        // The title is plain text at the start of the file: AC4BF[slot]-tm_yyyy_MM_dd HH:mm:ss
        private bool TryReadTitle(string file, out int slot, out string time)
        {
            slot = -1;
            time = "";

            DateTime modified = File.GetLastWriteTimeUtc(file);
            if (_titles.TryGetValue(file, out var cached) && cached.Modified == modified)
            {
                slot = cached.Slot;
                time = cached.Time;
                return slot >= 0;
            }

            try
            {
                byte[] header = new byte[0x200];
                using (FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Read(header, 0, header.Length) < header.Length)
                        return false;
                }

                // The title is padded with NULs on both sides (Trim() does not remove NUL), so trim those explicitly.
                string title = Encoding.Unicode.GetString(header, 4, header.Length - 4).Trim('\0', ' ', '\t', '\r', '\n');
                Match m = TitleRegex().Match(title);
                if (m.Success)
                {
                    slot = int.Parse(m.Groups["slot"].Value);
                    time = m.Groups["time"].Value;
                }
            }
            catch (IOException)
            {
                return false; // being written, try again at the next check
            }

            _titles[file] = (modified, slot, time);
            return slot >= 0;
        }

        private string? FindSaveFolder()
        {
            if (!string.IsNullOrWhiteSpace(_settings.SaveFolder))
                return Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : null;

            string? best = null;
            DateTime bestWrite = DateTime.MinValue;

            foreach (string root in LauncherSaveRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string userFolder in Directory.EnumerateDirectories(root))
                {
                    string candidate = Path.Combine(userFolder, Ac4LauncherGameId.ToString());
                    if (!Directory.Exists(candidate))
                        continue;

                    DateTime write = Directory.GetLastWriteTimeUtc(candidate);
                    if (best == null || write > bestWrite)
                    {
                        best = candidate;
                        bestWrite = write;
                    }
                }
            }

            return best;
        }

        // Where the Ubisoft launcher keeps saves: on Windows, and inside a Proton prefix on Linux.
        private static IEnumerable<string> LauncherSaveRoots()
        {
            const string relative = "Ubisoft/Ubisoft Game Launcher/savegames";

            foreach (string? programFiles in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramFiles")
            })
            {
                if (!string.IsNullOrEmpty(programFiles))
                    yield return Path.Combine(programFiles, relative);
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string steamRoot in new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam")
            })
            {
                string compat = Path.Combine(steamRoot, "steamapps", "compatdata");
                if (!Directory.Exists(compat))
                    continue;

                foreach (string prefix in Directory.EnumerateDirectories(compat))
                {
                    yield return Path.Combine(prefix, "pfx", "drive_c", "Program Files (x86)", relative);
                    yield return Path.Combine(prefix, "pfx", "drive_c", "Program Files", relative);
                }
            }
        }

        [GeneratedRegex(@"^AC4BF\[(?<slot>\d)\]-tm_(?<time>.+)$")]
        private static partial Regex TitleRegex();
    }
}
