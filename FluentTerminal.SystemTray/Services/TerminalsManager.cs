using FluentTerminal.App.Services;
using FluentTerminal.Models;
using FluentTerminal.Models.Enums;
using FluentTerminal.Models.Requests;
using FluentTerminal.Models.Responses;
using FluentTerminal.SystemTray.Services.ConPty;
using FluentTerminal.SystemTray.Services.WinPty;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel;
using FluentTerminal.Models.Messages;
using Microsoft.Toolkit.Mvvm.Messaging;

namespace FluentTerminal.SystemTray.Services
{
    public struct TerminalSessionInfo
    {
        public DateTime StartTime { get; set; }
        public string ProfileName { get; set; }
        public ITerminalSession Session { get; set; }
    }

    public class TerminalsManager
    {
        private readonly Dictionary<byte, TerminalSessionInfo> _terminals = new Dictionary<byte, TerminalSessionInfo>();

        public event EventHandler<TerminalOutput> DisplayOutputRequested;

        public event EventHandler<TerminalExitStatus> TerminalExited;

        private static readonly Regex EscapeSequencePattern = new Regex(@"((\x9B|\x1B\[)[0-?]*[ -\/]*[@-~])|((\x9D|\x1B\]).*\x07)", RegexOptions.Compiled);

        private readonly Dictionary<byte, string> _cachedLogPath = new Dictionary<byte, string>();

        private ApplicationSettings _applicationSettings;

        public TerminalsManager(ISettingsService settingsService)
        {
            _applicationSettings = settingsService.GetApplicationSettings();
            WeakReferenceMessenger.Default.Register<TerminalsManager, ApplicationSettingsChangedMessage>(this, (r, m) => r.OnApplicationSettingsChanged(m));
        }

        private void OnApplicationSettingsChanged(ApplicationSettingsChangedMessage message)
        {
            _applicationSettings = message.ApplicationSettings;
        }

        public void DisplayTerminalOutput(byte terminalId, byte[] output)
        {
            if (_applicationSettings.EnableLogging && Directory.Exists(_applicationSettings.LogDirectoryPath))
            {
                var logOutput = output;
                if (_applicationSettings.PrintableOutputOnly)
                {
                    var strOutput = Encoding.UTF8.GetString(logOutput);
                    strOutput = EscapeSequencePattern.Replace(strOutput, "");
                    logOutput = Encoding.UTF8.GetBytes(strOutput);
                }

                try
                {
                    using var logFileStream = System.IO.File.Open(GetLogFilePath(terminalId), System.IO.FileMode.Append);
                    logFileStream.Write(logOutput, 0, logOutput.Length);
                }
                catch (Exception e)
                {
                    Logger.Instance.Debug("DisplayTerminalOutput failed. Exception: {0}", e);
                }
            }

            DisplayOutputRequested?.Invoke(this, new TerminalOutput
            {
                TerminalId = terminalId,
                Data = output
            });
        }

        private string GetLogFilePath(byte terminalId)
        {
            if (!_terminals.ContainsKey(terminalId))
                return string.Empty;

            if (!_cachedLogPath.ContainsKey(terminalId))
            {
                var sb = new StringBuilder();
                sb.Append(_applicationSettings.LogDirectoryPath);
                sb.Append(Path.DirectorySeparatorChar);
                sb.Append(_terminals[terminalId].StartTime.ToString("yyyyMMddhhmmssfff"));
                sb.Append("_");
                sb.Append(_terminals[terminalId].ProfileName);
                sb.Append(".log");

                _cachedLogPath.Add(terminalId, sb.ToString());
            }

            return _cachedLogPath[terminalId];
        }

        private static bool IsPowerShell7Location(string location)
        {
            return !string.IsNullOrWhiteSpace(location) &&
                   string.Equals(Path.GetFileName(location), "pwsh.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePowerShell7Location(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || System.IO.File.Exists(location))
            {
                return location;
            }

            if (!IsPowerShell7Location(location))
            {
                return location;
            }

            // First use the persistent PATH rather than the SystemTray process PATH. Desktop Bridge
            // may inherit a truncated PATH, while the user PATH normally contains the Store app
            // execution alias directory (%LOCALAPPDATA%\Microsoft\WindowsApps).
            var pathValues = new[]
            {
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
                Environment.GetEnvironmentVariable("Path")
            };

            foreach (var pathValue in pathValues)
            {
                if (string.IsNullOrWhiteSpace(pathValue))
                {
                    continue;
                }

                foreach (var rawDirectory in pathValue.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var directory = Environment.ExpandEnvironmentVariables(rawDirectory.Trim().Trim('"'));
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    try
                    {
                        var candidate = Path.Combine(directory, "pwsh.exe");
                        if (System.IO.File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Ignore malformed PATH entries and continue searching.
                    }
                }
            }

            // Explicitly try the App Execution Alias used by Microsoft Store packages even if the
            // user's PATH has been customized and no longer contains WindowsApps.
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "pwsh.exe");
                if (System.IO.File.Exists(alias))
                {
                    return alias;
                }
            }

            var programFilesRoots = new[]
            {
                Environment.GetEnvironmentVariable("ProgramW6432"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            foreach (var root in programFilesRoots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                var normalInstall = Path.Combine(root, "PowerShell", "7", "pwsh.exe");
                if (System.IO.File.Exists(normalInstall))
                {
                    return normalInstall;
                }

                // Last-resort Store package lookup. The package directory is versioned, so never
                // persist this path in the profile; resolve it afresh for each terminal launch.
                var windowsApps = Path.Combine(root, "WindowsApps");
                try
                {
                    string newestStorePwsh = null;
                    foreach (var packageDirectory in Directory.EnumerateDirectories(
                                 windowsApps,
                                 "Microsoft.PowerShell_*__8wekyb3d8bbwe",
                                 SearchOption.TopDirectoryOnly))
                    {
                        var candidate = Path.Combine(packageDirectory, "pwsh.exe");
                        if (!System.IO.File.Exists(candidate))
                        {
                            continue;
                        }

                        if (newestStorePwsh == null ||
                            string.Compare(candidate, newestStorePwsh, StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            newestStorePwsh = candidate;
                        }
                    }

                    if (newestStorePwsh != null)
                    {
                        return newestStorePwsh;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // WindowsApps enumeration can be ACL-restricted; the alias lookup above is the
                    // normal Store path and does not require directory enumeration.
                }
                catch (DirectoryNotFoundException)
                {
                    // No Store package directory on this installation.
                }
            }

            return location;
        }

        public CreateTerminalResponse CreateTerminal(CreateTerminalRequest request)
        {
            if (_terminals.ContainsKey(request.Id))
            {
                // App terminated without cleaning up, removing orphaned sessions
                foreach (var item in _terminals.Values)
                {
                    item.Session.Dispose();
                }
                _terminals.Clear();
            }

            request.Profile.Location = Utilities.ResolveLocation(request.Profile.Location);

            if (IsPowerShell7Location(request.Profile.Location))
            {
                var resolvedPowerShell7 = ResolvePowerShell7Location(request.Profile.Location);
                if (string.IsNullOrWhiteSpace(resolvedPowerShell7) || !System.IO.File.Exists(resolvedPowerShell7))
                {
                    return new CreateTerminalResponse
                    {
                        Error = "PowerShell 7 (pwsh.exe) was not found. FluentTerminalPlus checked the persistent PATH, the Microsoft Store app execution alias, Program Files, and installed Store package locations."
                    };
                }

                request.Profile.Location = resolvedPowerShell7;
            }

            ITerminalSession terminal = null;
            try
            {
                if (request.Profile.RunAsAdministrator)
                {
                    if (request.SessionType == SessionType.WinPty)
                    {
                        return new CreateTerminalResponse
                        {
                            Error = "Administrator terminals require ConPTY."
                        };
                    }

                    terminal = new ElevatedConPtySession();
                }
                else if (request.SessionType == SessionType.WinPty)
                {
                    terminal = new WinPtySession();
                }
                else
                {
                    terminal = new ConPtySession();
                }
                terminal.Start(request, this);
            }
            catch (Exception e)
            {
                terminal?.Dispose();
                return new CreateTerminalResponse { Error = e.ToString() };
            }

            var name = string.IsNullOrEmpty(request.Profile.Name) ? terminal.ShellExecutableName : request.Profile.Name;
            terminal.ConnectionClosed += OnTerminalConnectionClosed;
            _terminals.Add(terminal.Id, new TerminalSessionInfo
            {
                ProfileName = name,
                StartTime = DateTime.Now,
                Session = terminal
            });
            return new CreateTerminalResponse
            {
                Success = true,
                Name = name
            };
        }

        public void Write(byte id, byte[] data)
        {
            if (_terminals.TryGetValue(id, out TerminalSessionInfo sessionInfo))
            {
                try
                {
                    sessionInfo.Session.Write(data);
                }
                catch (IOException e)
                {
                    Logger.Instance.Error($"TerminalsManager.Write: sending user input to terminal with id '{id}' failed with exception: {e}");
                }
            }
        }

        public void ResizeTerminal(byte id, TerminalSize size)
        {
            if (_terminals.TryGetValue(id, out TerminalSessionInfo sessionInfo))
            {
                try
                {
                    sessionInfo.Session.Resize(size);
                }
                catch (Exception e)
                {
                    Logger.Instance.Error($"ResizeTerminal: resizing of terminal with id '{id}' failed with exception: {e}");
                }
            }
            else
            {
                Debug.WriteLine($"ResizeTerminal: terminal with id '{id}' was not found");
            }
        }

        public void CloseTerminal(byte id)
        {
            if (_terminals.TryGetValue(id, out TerminalSessionInfo sessionInfo))
            {
                _terminals.Remove(sessionInfo.Session.Id);
                sessionInfo.Session.Close();
            }
        }

        public PauseTerminalOutputResponse PauseTermimal(byte id, bool pause)
        {
            var response = new PauseTerminalOutputResponse()
            {
                Success = true
            };
            if (_terminals.TryGetValue(id, out TerminalSessionInfo sessionInfo))
            {
                sessionInfo.Session.Pause(value: pause);
            }
            return response;
        }

        public string GetDefaultEnvironmentVariableString(Dictionary<string, string> additionalVariables)
        {
            // Desktop Bridge/full-trust launches can occasionally inherit a truncated process PATH.
            // Build a case-insensitive environment map and restore PATH from the persistent machine +
            // user values before passing the block to ConPTY. This makes every tab independent of the
            // launcher's transient PATH and removes the need for a PowerShell-profile workaround.
            var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
            {
                if (item.Key != null)
                {
                    environmentVariables[item.Key.ToString()] = item.Value?.ToString() ?? string.Empty;
                }
            }

            var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);

            if (!string.IsNullOrWhiteSpace(machinePath) || !string.IsNullOrWhiteSpace(userPath))
            {
                if (string.IsNullOrWhiteSpace(machinePath))
                {
                    environmentVariables["Path"] = userPath;
                }
                else if (string.IsNullOrWhiteSpace(userPath))
                {
                    environmentVariables["Path"] = machinePath;
                }
                else
                {
                    environmentVariables["Path"] = machinePath.TrimEnd(';') + ";" + userPath.TrimStart(';');
                }
            }

            environmentVariables["TERM_PROGRAM"] = "FluentTerminalPlus";
            environmentVariables["TERM_PROGRAM_VERSION"] = $"{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";

            if (additionalVariables != null)
            {
                foreach (var kvp in additionalVariables)
                {
                    environmentVariables[kvp.Key] = kvp.Value;
                }
            }

            var builder = new StringBuilder();

            foreach (var item in environmentVariables)
            {
                builder.Append(item.Key).Append("=").Append(item.Value).Append("\0");
            }
            builder.Append('\0');

            return builder.ToString();
        }

        private void OnTerminalConnectionClosed(object sender, int exitcode)
        {
            if (sender is ITerminalSession terminal)
            {
                _terminals.Remove(terminal.Id);
                TerminalExited?.Invoke(this, new TerminalExitStatus(terminal.Id, exitcode));
                terminal.Dispose();
            }
        }
    }
}
