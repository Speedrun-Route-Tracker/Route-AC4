using System.Diagnostics;
using Route_Plugin_Base;
using ProcessMemory;

namespace Route_AC4
{
    public class RoutePluginAC4: IPluginBase
    {
        public new List<RoutePart> Route = [];
        public new bool IsInitialized = false;
        private ProcessMemoryHandler? _memoryHandler;

        public override void Initialize()
        {
            Process? gameProc = Process.GetProcessesByName("AC4BFSP").FirstOrDefault();
            if (gameProc == null)
            {
                this.IsInitialized = false;
                this._memoryHandler = null;
                return;
            }

            this._memoryHandler = new ProcessMemoryHandler((uint)gameProc.Id, true);
            if (!this._memoryHandler.ProcessRunning)
            {
                this.IsInitialized = false;
                return;
            }
        }

        public override void Tick()
        {

        }
    }
}
