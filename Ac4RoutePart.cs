using Route_Plugin_Base;

namespace Route_AC4
{
    /// <summary>
    ///  A route step of the AC4 route file. <see cref="RoutePart.Location"/> is the area name used for the
    ///  per-location checks (chests, viewpoints, ...).
    /// </summary>
    public class Ac4RoutePart : RoutePart
    {
        /// <summary>How many of this collectible type must be collected overall.</summary>
        public int Condition { get; set; }

        /// <summary>How many must be collected in <see cref="RoutePart.Location"/>.</summary>
        public int LocationCondition { get; set; }
    }
}
