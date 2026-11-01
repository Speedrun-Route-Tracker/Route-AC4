using System.Runtime.InteropServices;
using Route_Plugin_Base.Utils;

namespace Route_AC4
{
    /// <summary>Reads and writes the game's code. Separate from the patch logic so the logic can be tested.</summary>
    internal interface ICodeMemory
    {
        bool Read(nint address, byte[] buffer);
        bool Write(nint address, byte[] data);
    }

    /// <summary>
    ///  Makes the game window borderless by patching two instructions in AC4BFSP.exe (the Cheat Engine script, done
    ///  by the plugin). Nothing is allocated in the game: two existing instructions are replaced in place, so there is
    ///  nothing to free. When the game closes the patch disappears with it. The patch is still undone when the option
    ///  is turned off, the plugin shuts down or the tracker exits, so the game is left as it was.
    /// </summary>
    internal sealed partial class Ac4Borderless
    {
        private readonly record struct Site(int Offset, byte[] Original, byte[] Patched);

        // Offsets from the start of AC4BFSP.exe. The original bytes are checked before anything is written, so a
        // different build of the game (Steam, Ubisoft) is left alone instead of being corrupted.
        private static readonly Site[] Sites =
        [
            new(0x555E, [0x0F, 0x84, 0xBA, 0x00, 0x00, 0x00], [0xE9, 0xBB, 0x00, 0x00, 0x00, 0x90]),
            new(0x55F0, [0x74], [0xEB])
        ];

        private readonly Action<string> _log;
        private nint _patchedBase;
        private int _failedProcessId;

        public Ac4Borderless(Action<string> log) => _log = log;

        /// <summary>True while the patch written by this class is in the running game.</summary>
        public bool IsApplied { get; private set; }

        /// <summary>Short text for the status line, empty when there is nothing to say.</summary>
        public string Status { get; private set; } = "";

        /// <summary>Call every tick while connected. Does nothing unless the wanted state differs from the actual one.</summary>
        public void Update(MemoryHandler game, bool wanted)
        {
            if (wanted == IsApplied)
            {
                if (!wanted)
                    Status = "";
                return;
            }

            if (!game.IsProcessRunning)
            {
                Forget();
                return;
            }

            if (wanted)
            {
                if (_failedProcessId == game.ProcessId)
                    return; // already tried on this run of the game and it did not work, do not retry twice a second

                using WindowsCodeMemory memory = new(game.ProcessId);
                if (!memory.IsOpen)
                {
                    Fail(game.ProcessId, "borderless needs Route Tracker to run as administrator");
                    return;
                }

                Apply(memory, game.BaseAddress, game.ProcessId);
            }
            else
            {
                using WindowsCodeMemory memory = new(game.ProcessId);
                if (memory.IsOpen)
                    Restore(memory, _patchedBase);
                IsApplied = false;
                Status = "";
            }
        }

        /// <summary>Undo the patch if it is in the running game. Safe to call at any time, also when the game is gone.</summary>
        public void RestoreNow(MemoryHandler? game)
        {
            if (!IsApplied)
                return;

            try
            {
                if (game != null && game.IsProcessRunning)
                {
                    using WindowsCodeMemory memory = new(game.ProcessId);
                    if (memory.IsOpen)
                        Restore(memory, _patchedBase);
                }
            }
            catch (Exception ex)
            {
                _log($"Borderless: could not undo the patch: {ex.Message}");
            }

            Forget();
        }

        /// <summary>The game closed (or was never patched by us): nothing in it needs undoing any more.</summary>
        public void Forget()
        {
            IsApplied = false;
            _patchedBase = 0;
            _failedProcessId = 0;
            Status = "";
        }

        /// <summary>The user changed the option: allow another attempt even if the last one failed.</summary>
        public void ResetFailure() => _failedProcessId = 0;

        internal bool Apply(ICodeMemory memory, nint moduleBase, int processId)
        {
            // Check every site first, then write, so a mismatch never leaves the game half patched.
            bool[] alreadyPatched = new bool[Sites.Length];
            for (int i = 0; i < Sites.Length; i++)
            {
                byte[] current = new byte[Sites[i].Original.Length];
                if (!memory.Read(moduleBase + Sites[i].Offset, current))
                {
                    Fail(processId, "borderless could not read the game's code");
                    return false;
                }

                if (current.AsSpan().SequenceEqual(Sites[i].Patched))
                    alreadyPatched[i] = true; // someone else (Cheat Engine) already did this one, leave it alone
                else if (!current.AsSpan().SequenceEqual(Sites[i].Original))
                {
                    Fail(processId, "borderless does not fit this version of the game");
                    return false;
                }
            }

            List<int> written = [];
            for (int i = 0; i < Sites.Length; i++)
            {
                if (alreadyPatched[i])
                    continue;

                if (!memory.Write(moduleBase + Sites[i].Offset, Sites[i].Patched))
                {
                    foreach (int done in written)
                        memory.Write(moduleBase + Sites[done].Offset, Sites[done].Original);
                    Fail(processId, "borderless could not write to the game");
                    return false;
                }
                written.Add(i);
            }

            if (written.Count == 0)
            {
                // Everything was patched by someone else: nothing for us to undo later.
                _log("Borderless: the game was already patched by another tool, left as it is.");
                return true;
            }

            _patchedBase = moduleBase;
            IsApplied = true;
            Status = "borderless on";
            _log("Borderless: patch applied.");
            return true;
        }

        internal void Restore(ICodeMemory memory, nint moduleBase)
        {
            foreach (Site site in Sites)
            {
                byte[] current = new byte[site.Patched.Length];
                // Only undo what is still our patch: if the bytes are something else, the game or another tool changed them.
                if (memory.Read(moduleBase + site.Offset, current) && current.AsSpan().SequenceEqual(site.Patched))
                    memory.Write(moduleBase + site.Offset, site.Original);
            }

            _log("Borderless: patch removed.");
        }

        private void Fail(int processId, string reason)
        {
            _failedProcessId = processId;
            Status = reason;
            _log($"Borderless: {reason}.");
        }

        /// <summary>Opens the game with write rights only for the moment it is needed, then closes the handle.</summary>
        private sealed partial class WindowsCodeMemory : ICodeMemory, IDisposable
        {
            private const uint ProcessVmOperation = 0x0008;
            private const uint ProcessVmRead = 0x0010;
            private const uint ProcessVmWrite = 0x0020;
            private const uint ProcessQueryInformation = 0x0400;
            private const uint PageExecuteReadWrite = 0x40;

            private nint _handle;

            public WindowsCodeMemory(int processId)
            {
                try
                {
                    _handle = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation, false, processId);
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                {
                    _handle = 0; // not Windows
                }
            }

            public bool IsOpen => _handle != 0;

            public unsafe bool Read(nint address, byte[] buffer)
            {
                fixed (byte* p = buffer)
                    return ReadProcessMemory(_handle, address, p, buffer.Length, out nint read) && read == buffer.Length;
            }

            public unsafe bool Write(nint address, byte[] data)
            {
                // Code pages are read-only: allow writing, write, then put the old protection back.
                if (!VirtualProtectEx(_handle, address, data.Length, PageExecuteReadWrite, out uint oldProtect))
                    return false;

                bool ok;
                fixed (byte* p = data)
                    ok = WriteProcessMemory(_handle, address, p, data.Length, out nint written) && written == data.Length;

                VirtualProtectEx(_handle, address, data.Length, oldProtect, out _);
                FlushInstructionCache(_handle, address, data.Length);
                return ok;
            }

            public void Dispose()
            {
                if (_handle != 0)
                {
                    CloseHandle(_handle);
                    _handle = 0;
                }
            }

            [LibraryImport("kernel32.dll", SetLastError = true)]
            private static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool CloseHandle(nint hObject);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static unsafe partial bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, byte* lpBuffer, nint nSize, out nint lpNumberOfBytesRead);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static unsafe partial bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte* lpBuffer, nint nSize, out nint lpNumberOfBytesWritten);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool VirtualProtectEx(nint hProcess, nint lpAddress, nint dwSize, uint flNewProtect, out uint lpflOldProtect);

            [LibraryImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool FlushInstructionCache(nint hProcess, nint lpBaseAddress, nint dwSize);
        }
    }
}
