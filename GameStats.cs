using Route_Plugin_Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Route_AC4
{
    public unsafe class GameStats: GameStatsBase
    {
        #region alt pointer offset
        /* Variable of the hex value to add to the original offset when
         * the default offset doesnt work for the user, This is because of reasons 
         * but so i dont have to change the 2nd offset we keep it and just add 
         * this hex value to the original offset so we dont need a complicated system
        */
        static bool useAltOffset = false;
        private const int altOffsetHexValueOffset = 0x11F8;
        #endregion

        #region Memory Offsets Arrays
        // Offsets for direct memory reads (special cases)
        private readonly int[] characterPtrOffsets = []; // has no offsets
        private readonly int[] mainMenuPtrOffsets = []; // has no offsets
        private readonly int[] percentFtPtrOffsets = [0x74];
        private readonly int[] legendaryShipPtrOffsets = [0xBB8];
        private readonly int[] percentPtrOffsets = [0x284];
        private readonly int[] loadingPtrOffsets = [0x7D8];
        private readonly int[] fortsPtrOffsets = [0xA08];
        private readonly int[] xCoordPtrOffsets = [0x0, 0x40];
        private readonly int[] yCoordPtrOffsets = [0x0, 0x44];
        private readonly int[] zCoordPtrOffsets = [0x0, 0x48];
        #endregion

        #region Game State Flag
        // Tracks if the game is currently loading or at the main menu
        private bool isLoading = false;
        private bool isMainMenu = false;
        #endregion

        #region Base Addresses
        // Pre-calculated base addresses for memory reads
        private readonly nint collectiblesBaseAddress;
        private readonly nint missionBaseAddress;
        private readonly nint XYZBaseAddress;
        #endregion

        #region Collectible/Progress Offsets
        // Unique third offsets for each collectible type
        private const int ViewpointsThirdOffset = -0x1B30;
        private const int MyanThirdOffset = -0x1B1C;
        private const int TreasureThirdOffset = -0xBB8;
        private const int FragmentsThirdOffset = -0x1B58;
        private const int AssassinThirdOffset = -0xDD4;
        private const int NavalThirdOffset = 0x1950;
        private const int HeroUpgradeThirdOffset = -0x19F0; // Reserved for future use
        private const int LettersThirdOffset = -0x04EC;
        private const int ManuscriptsThirdOffset = -0x334;
        private const int MusicThirdOffset = 0x424;

        // Shared offsets for pointer traversal
        private const int FirstOffset = 0x2D0;
        private int SecondOffset = 0x8BC;
        private const int LastOffset = 0x18;
        private const int OffsetStep = 0x14;
        // Shared offsets for different Missions
        private const int MissionFirstOffset = 0x1C8;
        private const int MissionEndOffset = 0x648;
        private const int TemplarHuntFirstOffset = 0x6A8;
        private const int TemplarHuntEndOffset = 0x7C8;
        private const int MissionOffsetStep = 0x60;

        // Ranges for flag-based collectibles
        private const int ChestStartOffset = 0x67C;
        private const int ChestEndOffset = 0xA8C;
        private const int FragmentsStartOffset = -0xAA0;
        private const int FragmentsEndOffset = -0x690;
        private const int TavernStartOffset = 0x319C;
        private const int TavernEndOffset = 0x3228;
        private const int TreasureMapsStartOffset = 0x3250;
        private const int TreasureMapsEndOffset = 0x3408;
        private static readonly int[] SpecialTreasureMapOffsets = [0x33F4]; // Special treasure map offset we dont count
        private const int ViewpointStartOffset = 0x2BAC;
        private const int ViewpointEndOffset = 0x2EE0;
        private static readonly int[] SpecialViewpointOffsets = [0x2D8C, 0x2DA0,
        0x2DB4, 0x2DC8, 0x2DDC, 0x2DF0, 0x2E04, 0x2E18, 0x2E2C, 0x2E40, 0x2E54,
        0x2E68, 0x2E7C]; // Special viewpoint offsets we dont count
        #endregion

        #region Progress and Upgrade Tracking
        // Special activity counters (updated via memory or logic)
        private int completedStoryMissions = 0;
        private int totalTemplarHunts = 0;
        private int legendaryShips = 0;
        private int treasuremaps = 0;

        // Total upgrades and collectibles
        private int totalUpgrades = 0;
        private int totalFragments = 0;
        private int totalViewpoints = 0;

        #endregion

        #region Dictionarys for collectibles
        public Dictionary<string, int> LocationChestCounts { get; } = [];
        public Dictionary<string, int> LocationFragmentCounts { get; } = [];
        public Dictionary<string, int> LocationTavernCounts { get; } = [];
        public Dictionary<string, int> LocationTreasureMapCounts { get; } = [];
        public Dictionary<string, int> LocationViewpointsCounts { get; } = [];
        #endregion

        // Sets up our tracker to read AC4's memory
        // Calculates the starting point for finding all the collectibles
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0290",
        Justification = "NO")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0079",
        Justification = "because i said so")]
        public GameStats(IntPtr processHandle, IntPtr baseAddress)
    : base(processHandle, baseAddress)
        {
            this.collectiblesBaseAddress = (nint)baseAddress + 0x026BEAC0;
            this.missionBaseAddress = (nint)baseAddress + 0x0137D9F8;
            this.XYZBaseAddress = (nint)baseAddress + 0x026C16A0;
        }

        #region Memory Reading Methods
        // Simplifies reading collectibles that follow the standard pattern
        // Makes the code cleaner by removing duplicated memory reading logic
        private void CheckSecondOffset(int mainmenu, bool loading)
        {
            //Debug.WriteLine($"using alt offset?: {useAltOffset}");
            DetectStatuses(mainmenu, loading);
            int temppointer = ReadWithCache<int>("checksecondoffset", collectiblesBaseAddress, [FirstOffset, SecondOffset, 0x67C, LastOffset]);
            if (temppointer == IntPtr.Zero)
            {
                Debug.WriteLine("Swapped to 0x90C");
                SecondOffset = 0x90C;
                useAltOffset = true;
            }
            else if (!useAltOffset && temppointer == IntPtr.Zero)
            {
                Debug.WriteLine("Swapped to 0x8BC");
                SecondOffset = 0x8BC;
            }
        }

        private int ReadCollectible(int thirdOffset)
        {
            string cacheKey = $"collectible_{thirdOffset}";
            return ReadWithCache<int>(cacheKey, collectiblesBaseAddress, [FirstOffset, SecondOffset, thirdOffset, LastOffset]);
        }

        // function made by me
        private int ReadMission(int missionOffset)
        {
            string cacheKey = $"mission_{missionOffset}";
            return ReadWithCache<int>(cacheKey, missionBaseAddress, [missionOffset]);
        }

        // Used for things like chests and taverns that have many individual locations
        // Each location has its own memory address with a predictable pattern
        // startOffset and endOffset are comes from getstats() we pass them through lol
        private int CountCollectibles(int startOffset, int endOffset)
        {
            string cacheKey = $"count_{startOffset}_{endOffset}";

            // Try to get from cache using the base class method
            if (TryGetCachedValue(cacheKey, out int cachedCount))
            {
                return cachedCount;
            }

            // Count if not in cache
            int count = 0;
            for (int thirdOffset = startOffset; thirdOffset <= endOffset; thirdOffset += OffsetStep)
            {
                // skip these maps as the user should have them collected already or the map isnt used anyways
                if (SpecialTreasureMapOffsets.Contains(thirdOffset)) continue;
                if (SpecialViewpointOffsets.Contains(thirdOffset)) continue;

                int value = ReadCollectible(thirdOffset);
                count += value;

                if (CollectibleOffsets.ChestOffsetToLocation.TryGetValue(thirdOffset, out string? LocationNameChest))
                {
                    LocationChestCounts[LocationNameChest] = value;
                }
                if (CollectibleOffsets.FragmentOffsetToLocation.TryGetValue(thirdOffset, out string? LocationNameFragment))
                {
                    LocationFragmentCounts[LocationNameFragment] = value;
                }
                if (CollectibleOffsets.TavernOffsetToLocation.TryGetValue(thirdOffset, out string? LocationNameTavern))
                {
                    LocationTavernCounts[LocationNameTavern] = value;
                }
                if (CollectibleOffsets.TreasureMapOffsetToLocation.TryGetValue(thirdOffset, out string? LocationNameTreasureMap))
                {
                    LocationTreasureMapCounts[LocationNameTreasureMap] = value;
                }
                if (CollectibleOffsets.ViewpointOffsetToLocation.TryGetValue(thirdOffset, out string? LocationNameViewpoint))
                {
                    LocationViewpointsCounts[LocationNameViewpoint] = value;
                }
            }

            // Store in cache
            StoreInCache(cacheKey, count);
            return count;
        }

        // startOffset and endOffset comes from getstats() we pass them through lol
        // function made by me
        private int CountMissions(int startOffset, int endOffset)
        {
            string cacheKey = $"count_{startOffset}_{endOffset}";

            // Try to get from cache using the base class method
            if (TryGetCachedValue(cacheKey, out int cachedCount))
            {
                return cachedCount;
            }

            // Count if not in cache
            int count = 0;
            for (int missionOffset = startOffset; missionOffset <= endOffset; missionOffset += MissionOffsetStep)
            {
                count += ReadMission(missionOffset);
            }

            // Store in cache
            StoreInCache(cacheKey, count);
            return count;
        }

        // The main method that gets all the stats from the game
        // Reads everything from memory: percentage, collectibles, etc.
        // Also checks if you've completed any special activities
        public (int Percent, float PercentFloat, int Viewpoints, int Myan, int Treasure,
            int Fragments, int Assassin, int Naval, int Letters, int Manuscripts, int Music,
            int Forts, int Taverns, int TotalChests, float xCoord, float yCoord, float zCoord) 
            GetStats()
        {
            // check second offset first before reading collectibles
            
            // Reading collectibles using existing methods
            int percent = ReadWithCache<int>("percent", (nint)baseAddress + 0x49D9774, percentPtrOffsets);
            float percentFloat = ReadWithCache<float>("percentFloat", (nint)baseAddress + 0x049F1EE8, percentFtPtrOffsets);
            int forts = ReadWithCache<int>("forts", missionBaseAddress, fortsPtrOffsets);

            // for other functions
            int mainmenu = ReadWithCache<int>("mainmenu", (nint)baseAddress + 0x49D2204, mainMenuPtrOffsets);
            bool loading = ReadWithCache<bool>("loading", (nint)baseAddress + 0x04A1A6CC, loadingPtrOffsets);
            CheckSecondOffset(mainmenu, loading);
            //int character = ReadWithCache<int>("character", (nint)baseAddress + 0x23485C0, characterPtrOffsets);

            // Read all other collectibles
            int viewpoints = ReadCollectible(ViewpointsThirdOffset);
            int myan = ReadCollectible(MyanThirdOffset);
            int treasure = ReadCollectible(TreasureThirdOffset);
            int fragments = ReadCollectible(FragmentsThirdOffset);
            int assassin = ReadCollectible(AssassinThirdOffset);
            int naval = ReadCollectible(NavalThirdOffset);
            int letters = ReadCollectible(LettersThirdOffset);
            int manuscripts = ReadCollectible(ManuscriptsThirdOffset);
            int music = ReadCollectible(MusicThirdOffset);
            int taverns = CountCollectibles(TavernStartOffset, TavernEndOffset);
            int totalChests = CountCollectibles(ChestStartOffset, ChestEndOffset);
            totalFragments = CountCollectibles(FragmentsStartOffset, FragmentsEndOffset);
            totalViewpoints = CountCollectibles(ViewpointStartOffset, ViewpointEndOffset);
            totalUpgrades = ReadCollectible(HeroUpgradeThirdOffset);

            // legendary ship,templar hunt, storymissions, and treasuremaps counts
            legendaryShips = ReadWithCache<int>("legendaryships", missionBaseAddress, legendaryShipPtrOffsets);
            totalTemplarHunts = CountMissions(TemplarHuntFirstOffset, TemplarHuntEndOffset);
            completedStoryMissions = CountMissions(MissionFirstOffset, MissionEndOffset);
            treasuremaps = CountCollectibles(TreasureMapsStartOffset, TreasureMapsEndOffset);

            // DetectStatuses
            DetectStatuses(mainmenu, loading);

            // Read XYZ coordinates
            float xCoord = ReadWithCache<float>("Xcoord", XYZBaseAddress, xCoordPtrOffsets);
            float yCoord = ReadWithCache<float>("Ycoord", XYZBaseAddress, yCoordPtrOffsets);
            float zCoord = ReadWithCache<float>("Zcoord", XYZBaseAddress, zCoordPtrOffsets);

            //Debug.WriteLine($"AC4 PercentFloat: {percentFloat:F5}");
            // Return all the stats (including the basic ones that we got from memory)
            return (percent, percentFloat, viewpoints, myan, treasure, fragments, assassin, naval,
                letters, manuscripts, music, forts, taverns, totalChests, xCoord, yCoord, zCoord);
        }
        #endregion

        #region State Detection Methods
        private void DetectStatuses(int mainmenu, bool loading)
        {
            //loading status
            if (loading)
            {
                isLoading = true;
            }
            else
            {
                isLoading = false;
            }

            //main menu status
            if (mainmenu == 65540)
            {
                isMainMenu = true;
            }
            else
            {
                isMainMenu = false;
            }
        }
        #endregion

        #region Public Stats Interface
        // Tells the route tracker how many special activities we've completed.
        // This is how the UI knows when to check off story missions, legendary ships, and treasure maps.
        // Returns a tuple with all four counter values at once.
        public (int StoryMissions, int TemplarHunts, int LegendaryShips, int TreasureMaps) GetSpecialActivityCounts()
        {
            return (completedStoryMissions, totalTemplarHunts, legendaryShips, treasuremaps);
        }

        public override (bool IsLoading, bool IsMainMenu) GetGameStatus()
        {
            return (isLoading, isMainMenu);
        }

        // This is the primary method that the UI uses to get all stats
        // Returns everything in a dictionary so different games can have different stats
        public override Dictionary<string, object> GetStatsAsDictionary()
        {
            // Get stats using existing methods
            var (Percent, PercentFloat, Viewpoints, Myan, Treasure, Fragments, Assassin, Naval, Letters, Manuscripts, Music, Forts, Taverns, TotalChests, xCoord, yCoord, zCoord) = GetStats();
            var (StoryMissions, TemplarHunts, LegendaryShips, TreasureMaps) = GetSpecialActivityCounts();
            var (IsLoading, IsMainMenu) = GetGameStatus();

            // Return everything in a dictionary
            return new Dictionary<string, object>
            {
                // Core stats
                ["Completion Percentage"] = Percent,
                ["Exact Percentage"] = (float)Math.Round(PercentFloat, 2),
                ["Viewpoints"] = Viewpoints,
                ["Myan Stones"] = Myan,
                ["Buried Treasure"] = Treasure,
                ["Animus Fragments"] = Fragments,
                ["Assassin Contracts"] = Assassin,
                ["Naval Contracts"] = Naval,
                ["Letter Bottles"] = Letters,
                ["Manuscripts"] = Manuscripts,
                ["Music Sheets"] = Music,
                ["Forts"] = Forts,
                ["Taverns"] = Taverns,
                ["Chests"] = TotalChests,

                // Special activities
                ["Story Missions"] = StoryMissions,
                ["Templar Hunts"] = TemplarHunts,
                ["Legendary Ships"] = LegendaryShips,
                ["Treasure Maps"] = TreasureMaps,

                // Other stats
                ["Hero Upgrades"] = totalUpgrades,

                // Game state
                ["Is Loading"] = IsLoading,
                ["Is Main Menu"] = IsMainMenu,

                // Coordinates
                ["X Coordinate"] = xCoord,
                ["Y Coordinate"] = yCoord,
                ["Z Coordinate"] = zCoord
            };
        }
        #endregion
    }
}
