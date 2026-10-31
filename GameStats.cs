using System.IO;
using Route_Plugin_Base;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    /// <summary>
    ///  The only things the plugin still reads from game memory: whether the game is loading or in the main menu, and
    ///  the two completion percentages. Everything else comes from the save file.
    /// </summary>
    public class GameStats : GameStatsBase
    {
        // Pointer chains, all relative to the main module. Offsets are applied after each pointer is followed.
        private const int MainMenuAddress = 0x49D2204;   // reads 65540 in the main menu
        private const int LoadingAddress = 0x04A1A6CC;   // 0 when not loading, 1 when loading
        private const int PercentAddress = 0x49D9774;
        private const int PercentFloatAddress = 0x049F1EE8;

        private const int MainMenuValue = 65540;

        private static readonly int[] MainMenuOffsets = [];
        private static readonly int[] LoadingOffsets = [0x7D8];
        private static readonly int[] PercentOffsets = [0x284];
        private static readonly int[] PercentFloatOffsets = [0x74];

        private bool _isLoading;
        private bool _isMainMenu;

        public GameStats(MemoryHandler memory)
            : base(memory)
        {
        }

        public override (bool IsLoading, bool IsMainMenu) GetGameStatus() => (_isLoading, _isMainMenu);

        public override Dictionary<string, object> GetStatsAsDictionary()
        {
            int mainMenu = ReadWithCache<int>("mainmenu", baseAddress + MainMenuAddress, MainMenuOffsets);
            bool loading = ReadWithCache<bool>("loading", baseAddress + LoadingAddress, LoadingOffsets);
            int percent = ReadWithCache<int>("percent", baseAddress + PercentAddress, PercentOffsets);
            float percentFloat = ReadWithCache<float>("percentFloat", baseAddress + PercentFloatAddress, PercentFloatOffsets);

            _isLoading = loading;
            _isMainMenu = mainMenu == MainMenuValue;

            return new Dictionary<string, object>
            {
                ["Completion Percentage"] = percent,
                ["Exact Percentage"] = (float)Math.Round(percentFloat, 2),
                ["Is Loading"] = _isLoading,
                ["Is Main Menu"] = _isMainMenu
            };
        }
    }
}
