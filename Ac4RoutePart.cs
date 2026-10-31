using System.IO;
using Route_Plugin_Base;

namespace Route_AC4
{
    /// <summary>
    ///  A route step of the AC4 route file. <see cref="RoutePart.Location"/> is the area name shown as the current location.
    /// </summary>
    public class Ac4RoutePart : RoutePart
    {
        /// <summary>How many of this collectible type must be collected overall. Informational: completion comes from <see cref="Condition"/>.</summary>
        public int TotalCounters { get; set; }

        /// <summary>How many must be collected in <see cref="RoutePart.Location"/>. Informational: completion comes from <see cref="Condition"/>.</summary>
        public int TotalCountersLocation { get; set; }

        /// <summary>The condition text from the route file, kept as written.</summary>
        public string Condition { get; set; } = "";

        /// <summary>The parsed form of <see cref="Condition"/>, checked against the save file every time it changes.</summary>
        internal Ac4Condition Parsed { get; set; } = Ac4Condition.Empty;
    }
}
