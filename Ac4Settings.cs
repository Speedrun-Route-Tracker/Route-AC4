using System.IO;
using System.Text.Json;

namespace Route_AC4
{
    /// <summary>
    ///  The plugin's own settings, stored next to the plugin as ac4-settings.json. Until the host has a plugin
    ///  options window, edit the file by hand.
    /// </summary>
    internal sealed class Ac4Settings
    {
        private const string FileName = "ac4-settings.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>Save slot to follow: 0 = top slot, 1 = middle slot, 2 = bottom slot.</summary>
        public int Slot { get; set; }

        /// <summary>Folder that holds the .save files. Empty means find it automatically.</summary>
        public string SaveFolder { get; set; } = "";

        public static Ac4Settings Load(string pluginDirectory, Action<string> log)
        {
            string path = Path.Combine(pluginDirectory, FileName);
            try
            {
                if (File.Exists(path))
                {
                    Ac4Settings? loaded = JsonSerializer.Deserialize<Ac4Settings>(File.ReadAllText(path));
                    if (loaded != null)
                    {
                        loaded.Slot = Math.Clamp(loaded.Slot, 0, 2);
                        return loaded;
                    }
                }
                else
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(new Ac4Settings(), JsonOptions));
                }
            }
            catch (Exception ex)
            {
                log($"Could not read {FileName}, using defaults: {ex.Message}");
            }

            return new Ac4Settings();
        }
    }
}
