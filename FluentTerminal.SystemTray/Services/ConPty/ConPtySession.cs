using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentTerminal.Models;
using FluentTerminal.Models.Requests;
using Newtonsoft.Json;

namespace FluentTerminal.SystemTray.Services.ConPty
{
    public class ConPtySession : ITerminalSession
    {
        private TerminalsManager _terminalsManager;
        private Terminal _terminal;
        private BufferedReader _reader;
        private bool _enableBuffer;
        private bool _disposed;

        public byte Id { get; private set; }
        public string ShellExecutableName { get; private set; }
        public event EventHandler<int> ConnectionClosed;

        public void Start(CreateTerminalRequest request, TerminalsManager terminalsManager)
        {
            _enableBuffer = false; // request.Profile.UseBuffer;
            _reader?.Dispose();
            _reader = null;

            Id = request.Id;
            _terminalsManager = terminalsManager;

            var shellLocation = ResolveShellLocation(request.Profile.Location);
            ShellExecutableName = Path.GetFileNameWithoutExtension(shellLocation);
            var cwd = ResolveWorkingDirectory(request.Profile);
            var args = BuildCommandLine(shellLocation, request.Profile.Arguments);

            _terminal = new Terminal();
            _terminal.OutputReady += OnTerminalOutputReady;
            _terminal.Exited += OnTerminalExited;

            Task.Factory.StartNew(() => _terminal.Start(
                args,
                cwd,
                terminalsManager.GetDefaultEnvironmentVariableString(request.Profile.EnvironmentVariables),
                request.Size.Columns,
                request.Size.Rows));
        }

        internal static string ResolveShellLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return location;
            }

            // Upstream's built-in profile points to Windows PowerShell 5.1. Prefer PS7 without
            // forcing a settings migration; machines without PS7 keep the original fallback.
            if (location.EndsWith(@"\WindowsPowerShell\v1.0\powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var powerShell7 = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
                if (System.IO.File.Exists(powerShell7))
                {
                    return powerShell7;
                }
            }

            return location;
        }

        internal static string ResolveWorkingDirectory(ShellProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.WorkingDirectory) || !Directory.Exists(profile.WorkingDirectory))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return profile.WorkingDirectory;
        }

        internal static string BuildCommandLine(string location, string arguments)
        {
            return !string.IsNullOrWhiteSpace(location)
                ? $"\"{location}\" {arguments}"
                : arguments;
        }

        private void OnTerminalExited(object sender, EventArgs e)
        {
            Close();
        }

        private void OnTerminalOutputReady(object sender, EventArgs e)
        {
            if (_reader == null)
            {
                _reader = new BufferedReader(
                    _terminal.ConsoleOutStream,
                    bytes => _terminalsManager.DisplayTerminalOutput(Id, bytes),
                    _enableBuffer);
            }
        }

        public void Close()
        {
            _reader?.Dispose();
            ConnectionClosed?.Invoke(this, _terminal?.ExitCode ?? -1);
        }

        public void Resize(TerminalSize size)
        {
            _terminal?.Resize(size.Columns, size.Rows);
        }

        public void Write(byte[] data)
        {
            _terminal?.WriteToPseudoConsole(data);
        }

        public void Pause(bool value)
        {
            _reader?.SetPaused(value);
        }

        ~ConPtySession()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _terminal?.Dispose();
            }

            _disposed = true;
            _reader?.Dispose();
        }

        public void Dispose()
        {
            if (_terminal != null)
            {
                _terminal.Exited -= OnTerminalExited;
                _terminal.OutputReady -= OnTerminalOutputReady;
            }

            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Proxies one ConPTY session hosted by a separately elevated SystemTray process. The UWP UI and
    /// the normal broker remain at medium integrity; only this tab's helper receives an admin token.
    /// </summary>
    public sealed class ElevatedConPtySession : ITerminalSession
    {
        private NamedPipeServerStream _pipe;
        private Process _helperProcess;
        private TerminalsManager _terminalsManager;
        private bool _disposed;
        private bool _paused;
        private int _exitRaised;

        public byte Id { get; private set; }
        public string ShellExecutableName { get; private set; }
        public event EventHandler<int> ConnectionClosed;

        public void Start(CreateTerminalRequest request, TerminalsManager terminalsManager)
        {
            Id = request.Id;
            _terminalsManager = terminalsManager;

            var shellLocation = ConPtySession.ResolveShellLocation(request.Profile.Location);
            ShellExecutableName = Path.GetFileNameWithoutExtension(shellLocation);

            var pipeName = $"FluentTerminalPlus-Elevated-{Process.GetCurrentProcess().Id}-{Guid.NewGuid():N}";
            _pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            LaunchElevatedHelper(pipeName);
            WaitForHelperConnection();

            var startRequest = new ElevatedStartRequest
            {
                CommandLine = ConPtySession.BuildCommandLine(shellLocation, request.Profile.Arguments),
                WorkingDirectory = ConPtySession.ResolveWorkingDirectory(request.Profile),
                Environment = terminalsManager.GetDefaultEnvironmentVariableString(request.Profile.EnvironmentVariables),
                Columns = request.Size.Columns,
                Rows = request.Size.Rows
            };

            ElevatedPipeProtocol.Write(
                _pipe,
                ElevatedMessageType.Start,
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(startRequest)));

            var response = ElevatedPipeProtocol.Read(_pipe);
            if (response == null)
            {
                throw new IOException("Elevated terminal helper disconnected during startup.");
            }

            if (response.Type == ElevatedMessageType.Error)
            {
                throw new InvalidOperationException(Encoding.UTF8.GetString(response.Payload));
            }

            if (response.Type != ElevatedMessageType.Started)
            {
                throw new InvalidDataException($"Unexpected elevated helper response: {response.Type}");
            }

            Task.Factory.StartNew(
                ReadLoop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void LaunchElevatedHelper(string pipeName)
        {
            var executablePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Could not determine the SystemTray executable path.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"--elevated-session \"{pipeName}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(executablePath)
            };

            _helperProcess = Process.Start(startInfo);
            if (_helperProcess == null)
            {
                throw new InvalidOperationException("Windows did not start the elevated terminal helper.");
            }
        }

        private void WaitForHelperConnection()
        {
            var result = _pipe.BeginWaitForConnection(null, null);
            try
            {
                if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(2)))
                {
                    throw new TimeoutException("Timed out waiting for the elevated terminal helper.");
                }

                _pipe.EndWaitForConnection(result);
            }
            finally
            {
                result.AsyncWaitHandle.Close();
            }
        }

        private void ReadLoop()
        {
            try
            {
                while (!_disposed && _pipe?.IsConnected == true)
                {
                    var message = ElevatedPipeProtocol.Read(_pipe);
                    if (message == null)
                    {
                        break;
                    }

                    switch (message.Type)
                    {
                        case ElevatedMessageType.Output:
                            if (!_paused)
                            {
                                _terminalsManager.DisplayTerminalOutput(Id, message.Payload);
                            }
                            break;

                        case ElevatedMessageType.Exit:
                            var exitCode = message.Payload.Length >= sizeof(int)
                                ? System.BitConverter.ToInt32(message.Payload, 0)
                                : -1;
                            RaiseConnectionClosed(exitCode);
                            return;

                        case ElevatedMessageType.Error:
                            var text = Encoding.UTF8.GetString(message.Payload);
                            _terminalsManager.DisplayTerminalOutput(
                                Id,
                                Encoding.UTF8.GetBytes($"\r\n[FluentTerminalPlus elevated helper] {text}\r\n"));
                            RaiseConnectionClosed(-1);
                            return;
                    }
                }

                // If the helper is killed or otherwise disappears without an Exit frame, do not leave
                // a dead administrator tab hanging indefinitely.
                if (!_disposed)
                {
                    RaiseConnectionClosed(-1);
                }
            }
            catch (IOException)
            {
                if (!_disposed)
                {
                    RaiseConnectionClosed(-1);
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected while closing a tab.
            }
        }

        public void Write(byte[] data)
        {
            if (!_disposed && _pipe?.IsConnected == true)
            {
                ElevatedPipeProtocol.Write(_pipe, ElevatedMessageType.Input, data ?? Array.Empty<byte>());
            }
        }

        public void Resize(TerminalSize size)
        {
            if (_disposed || _pipe?.IsConnected != true)
            {
                return;
            }

            var payload = new ElevatedResizeRequest
            {
                Columns = size.Columns,
                Rows = size.Rows
            };

            ElevatedPipeProtocol.Write(
                _pipe,
                ElevatedMessageType.Resize,
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
        }

        public void Pause(bool value)
        {
            _paused = value;
        }

        public void Close()
        {
            if (!_disposed && _pipe?.IsConnected == true)
            {
                try
                {
                    ElevatedPipeProtocol.Write(_pipe, ElevatedMessageType.Close, Array.Empty<byte>());
                }
                catch (IOException)
                {
                    // The helper may already be exiting.
                }
                catch (ObjectDisposedException)
                {
                    // Closing raced with disposal.
                }
            }

            RaiseConnectionClosed(-1);
            Dispose();
        }

        private void RaiseConnectionClosed(int exitCode)
        {
            if (Interlocked.Exchange(ref _exitRaised, 1) == 0)
            {
                ConnectionClosed?.Invoke(this, exitCode);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pipe?.Dispose();
            _pipe = null;

            _helperProcess?.Dispose();
            _helperProcess = null;
        }
    }

    /// <summary>
    /// Special SystemTray mode used by an elevated helper. It owns exactly one ConPTY session and
    /// exchanges framed VT data with the normal broker over a random per-tab named pipe.
    /// </summary>
    internal static class ElevatedConPtyHost
    {
        public static void Run(string pipeName)
        {
            using (var pipe = new NamedPipeClientStream(
                       ".",
                       pipeName,
                       PipeDirection.InOut,
                       PipeOptions.Asynchronous))
            {
                pipe.Connect((int)TimeSpan.FromMinutes(2).TotalMilliseconds);

                var startMessage = ElevatedPipeProtocol.Read(pipe);
                if (startMessage == null || startMessage.Type != ElevatedMessageType.Start)
                {
                    return;
                }

                var request = JsonConvert.DeserializeObject<ElevatedStartRequest>(
                    Encoding.UTF8.GetString(startMessage.Payload));

                if (request == null)
                {
                    ElevatedPipeProtocol.Write(
                        pipe,
                        ElevatedMessageType.Error,
                        Encoding.UTF8.GetBytes("Invalid elevated terminal start request."));
                    return;
                }

                Terminal terminal = null;
                BufferedReader reader = null;

                try
                {
                    terminal = new Terminal();
                    terminal.OutputReady += (sender, args) =>
                    {
                        ElevatedPipeProtocol.Write(pipe, ElevatedMessageType.Started, Array.Empty<byte>());
                        reader = new BufferedReader(
                            terminal.ConsoleOutStream,
                            bytes => ElevatedPipeProtocol.Write(pipe, ElevatedMessageType.Output, bytes),
                            false);
                    };

                    terminal.Exited += (sender, args) =>
                    {
                        try
                        {
                            ElevatedPipeProtocol.Write(
                                pipe,
                                ElevatedMessageType.Exit,
                                System.BitConverter.GetBytes(terminal.ExitCode));
                        }
                        catch (IOException)
                        {
                            // Parent already disconnected.
                        }
                        catch (ObjectDisposedException)
                        {
                            // Parent already disconnected.
                        }
                    };

                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            terminal.Start(
                                request.CommandLine,
                                request.WorkingDirectory,
                                request.Environment,
                                request.Columns,
                                request.Rows);
                        }
                        catch (Exception ex)
                        {
                            TrySendError(pipe, ex);
                        }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                    while (pipe.IsConnected)
                    {
                        var message = ElevatedPipeProtocol.Read(pipe);
                        if (message == null)
                        {
                            break;
                        }

                        switch (message.Type)
                        {
                            case ElevatedMessageType.Input:
                                terminal.WriteToPseudoConsole(message.Payload);
                                break;

                            case ElevatedMessageType.Resize:
                                var resize = JsonConvert.DeserializeObject<ElevatedResizeRequest>(
                                    Encoding.UTF8.GetString(message.Payload));
                                if (resize != null)
                                {
                                    terminal.Resize(resize.Columns, resize.Rows);
                                }
                                break;

                            case ElevatedMessageType.Close:
                                return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TrySendError(pipe, ex);
                }
                finally
                {
                    reader?.Dispose();
                    terminal?.Dispose();
                }
            }
        }

        private static void TrySendError(Stream pipe, Exception exception)
        {
            try
            {
                ElevatedPipeProtocol.Write(
                    pipe,
                    ElevatedMessageType.Error,
                    Encoding.UTF8.GetBytes(exception.ToString()));
            }
            catch
            {
                // There is nowhere left to report the error if the parent pipe is gone.
            }
        }
    }

    internal enum ElevatedMessageType : byte
    {
        Start = 1,
        Started = 2,
        Input = 3,
        Resize = 4,
        Close = 5,
        Output = 6,
        Exit = 7,
        Error = 8
    }

    internal sealed class ElevatedPipeMessage
    {
        public ElevatedMessageType Type { get; set; }
        public byte[] Payload { get; set; }
    }

    internal sealed class ElevatedStartRequest
    {
        public string CommandLine { get; set; }
        public string WorkingDirectory { get; set; }
        public string Environment { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
    }

    internal sealed class ElevatedResizeRequest
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
    }

    internal static class ElevatedPipeProtocol
    {
        private const int HeaderSize = 5;
        private const int MaxPayloadSize = 16 * 1024 * 1024;

        public static void Write(Stream stream, ElevatedMessageType type, byte[] payload)
        {
            payload = payload ?? Array.Empty<byte>();

            var header = new byte[HeaderSize];
            header[0] = (byte)type;
            Buffer.BlockCopy(System.BitConverter.GetBytes(payload.Length), 0, header, 1, sizeof(int));

            lock (stream)
            {
                stream.Write(header, 0, header.Length);
                if (payload.Length > 0)
                {
                    stream.Write(payload, 0, payload.Length);
                }
                stream.Flush();
            }
        }

        public static ElevatedPipeMessage Read(Stream stream)
        {
            var header = new byte[HeaderSize];
            if (!ReadExact(stream, header, header.Length))
            {
                return null;
            }

            var payloadLength = System.BitConverter.ToInt32(header, 1);
            if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            {
                throw new InvalidDataException($"Invalid elevated pipe payload length: {payloadLength}");
            }

            var payload = new byte[payloadLength];
            if (payloadLength > 0 && !ReadExact(stream, payload, payloadLength))
            {
                return null;
            }

            return new ElevatedPipeMessage
            {
                Type = (ElevatedMessageType)header[0],
                Payload = payload
            };
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
