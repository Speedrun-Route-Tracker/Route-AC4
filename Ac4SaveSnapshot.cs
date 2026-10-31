using System.IO;
using AcSaveLib.Games.Ac4;

namespace Route_AC4
{
    /// <summary>
    ///  Just the parts of a save the route conditions need, as sets for fast lookups.
    /// </summary>
    internal sealed class Ac4SaveSnapshot
    {
        /// <summary>Mission ID to its highest synchronization. A mission is only present once it was started.</summary>
        public Dictionary<ulong, int> MissionSync { get; } = [];

        /// <summary>IDs of every inventory item the player has gained (chests, fragments, upgrades, blueprints, ...).</summary>
        public HashSet<ulong> GainedItems { get; } = [];

        /// <summary>IDs of the accomplishments marked completed.</summary>
        public HashSet<ulong> CompletedAccomplishments { get; } = [];

        public static Ac4SaveSnapshot From(Ac4SaveFile save)
        {
            Ac4SaveSnapshot snapshot = new();

            foreach (var mission in save.Narrative.AllMissions.Concat(save.Side.AllMissions))
            {
                snapshot.MissionSync.TryGetValue(mission.Id, out int existing);
                snapshot.MissionSync[mission.Id] = Math.Max(existing, mission.HighestSynchronization);
            }

            foreach (var character in save.PlayerProgression.Characters)
                foreach (ulong item in character.GainedInventoryItems)
                    snapshot.GainedItems.Add(item);

            foreach (var entry in save.Accomplishments.Entries)
                if (entry.Completed)
                    snapshot.CompletedAccomplishments.Add(entry.ObjectId);

            return snapshot;
        }
    }
}
