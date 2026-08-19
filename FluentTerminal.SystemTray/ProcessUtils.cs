using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace FluentTerminal.SystemTray
{
    public static class ProcessUtils
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        /// <summary>
        /// Returns true when at least one visible top-level window, or a visible child of a
        /// visible top-level window, belongs to a process with the supplied executable name.
        /// The child-window check matters for Windows 10 UWP apps whose frame can be hosted
        /// by ApplicationFrameHost.exe while the CoreWindow still belongs to the UWP process.
        /// </summary>
        public static bool HasVisibleWindowForProcessName(string processName)
        {
            var processIds = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    processIds.Add((uint)process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (processIds.Count == 0)
            {
                return false;
            }

            var found = false;
            EnumWindows((topLevelWindow, _) =>
            {
                if (!IsWindowVisible(topLevelWindow))
                {
                    return true;
                }

                GetWindowThreadProcessId(topLevelWindow, out var topLevelProcessId);
                if (processIds.Contains(topLevelProcessId))
                {
                    found = true;
                    return false;
                }

                EnumChildWindows(topLevelWindow, (childWindow, __) =>
                {
                    if (!IsWindowVisible(childWindow))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(childWindow, out var childProcessId);
                    if (!processIds.Contains(childProcessId))
                    {
                        return true;
                    }

                    found = true;
                    return false;
                }, IntPtr.Zero);

                return !found;
            }, IntPtr.Zero);

            return found;
        }

        /// <summary>
        /// Kill a process, and all of its children, grandchildren, etc.
        /// </summary>
        /// <param name="pid">Process ID.</param>
        public static void KillTree(int pid)
        {
            var searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid);
            foreach (ManagementObject mo in searcher.Get())
            {
                KillTree(Convert.ToInt32(mo["ProcessID"]));
            }

            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
            catch (Win32Exception)
            {
                // Ignore access is denied
            }
        }
    }
}
