using FluentTerminal.App.Services;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Windows.ApplicationModel;
using Windows.Storage;

namespace FluentTerminal.SystemTray
{
    public class SystemTrayApplicationContext : ApplicationContext
    {
        private const string FrontendProcessName = "FluentTerminal.App";
        private const int FrontendLifetimePollIntervalMs = 500;
        private const int InitialGracePeriodTicks = 10;
        private const int ConsecutiveNoVisibleWindowTicksBeforeExit = 2;

        private readonly NotifyIcon _notifyIcon;
        private readonly Timer _frontendLifetimeTimer;
        private bool _trayIconDisposed;
        private bool _hasSeenVisibleFrontendWindow;
        private int _lifetimePollCount;
        private int _consecutiveNoVisibleWindowTicks;

        public SystemTrayApplicationContext()
        {
            var openMenuItem = new MenuItem(Localize("Show", "显示", "顯示"), new EventHandler(OpenAppAsync));
            var newWindowItem = new MenuItem(Localize("New terminal", "新建终端", "新增終端機"), new EventHandler(NewWindow));
            var settingsMenuItem = new MenuItem(Localize("Show settings", "设置", "設定"), new EventHandler(ShowSettings));
            var exitMenuItem = new MenuItem(Localize("Exit", "退出", "結束"), new EventHandler(Exit));

            openMenuItem.DefaultItem = true;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.DoubleClick += OpenAppAsync;
            _notifyIcon.Text = "FluentTerminalPlus";

            if (SystemUsesLightTheme())
            {
                _notifyIcon.Icon = Properties.Resources.Icon_mono_light;
            }
            else
            {
                _notifyIcon.Icon = Properties.Resources.Icon_mono_dark;
            }

            _notifyIcon.ContextMenu = new ContextMenu(new MenuItem[] { openMenuItem, newWindowItem, settingsMenuItem, exitMenuItem });
            _notifyIcon.Visible = true;
            Application.ApplicationExit += Application_ApplicationExit;

            _frontendLifetimeTimer = new Timer
            {
                Interval = FrontendLifetimePollIntervalMs
            };
            _frontendLifetimeTimer.Tick += FrontendLifetimeTimer_Tick;
            _frontendLifetimeTimer.Start();
        }

        private void Exit(object sender, EventArgs e)
        {
            ExitApplication();
        }

        private void FrontendLifetimeTimer_Tick(object sender, EventArgs e)
        {
            if (_trayIconDisposed)
            {
                return;
            }

            var values = ApplicationData.Current.LocalSettings.Values;
            var exitWhenNoWindows =
                values.TryGetValue(Constants.ExitTrayWhenLastWindowClosedKey, out var exitValue) &&
                exitValue is bool enabled && enabled;

            if (!exitWhenNoWindows)
            {
                _lifetimePollCount = 0;
                _consecutiveNoVisibleWindowTicks = 0;
                _hasSeenVisibleFrontendWindow = false;
                return;
            }

            _lifetimePollCount++;

            bool hasVisibleFrontendWindow;
            try
            {
                hasVisibleFrontendWindow = ProcessUtils.HasVisibleWindowForProcessName(FrontendProcessName);
            }
            catch (Exception e)
            {
                Logger.Instance.Debug("Failed to inspect FluentTerminalPlus frontend windows. Exception: {0}", e);
                _consecutiveNoVisibleWindowTicks = 0;
                return;
            }

            if (hasVisibleFrontendWindow)
            {
                _hasSeenVisibleFrontendWindow = true;
                _consecutiveNoVisibleWindowTicks = 0;
                return;
            }

            // The full-trust helper can start before the first UWP frame/CoreWindow is visible.
            // Give activation a short grace period so startup cannot kill the helper prematurely.
            if (!_hasSeenVisibleFrontendWindow && _lifetimePollCount < InitialGracePeriodTicks)
            {
                return;
            }

            _consecutiveNoVisibleWindowTicks++;
            if (_consecutiveNoVisibleWindowTicks < ConsecutiveNoVisibleWindowTicksBeforeExit)
            {
                return;
            }

            ExitApplication();
        }

        private void ExitApplication()
        {
            DisposeTrayIcon();
            Application.Exit();
        }

        private void Application_ApplicationExit(object sender, EventArgs e)
        {
            DisposeTrayIcon();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.ApplicationExit -= Application_ApplicationExit;

                if (_frontendLifetimeTimer != null)
                {
                    _frontendLifetimeTimer.Stop();
                    _frontendLifetimeTimer.Tick -= FrontendLifetimeTimer_Tick;
                    _frontendLifetimeTimer.Dispose();
                }

                DisposeTrayIcon();
            }

            base.Dispose(disposing);
        }

        private void DisposeTrayIcon()
        {
            if (_trayIconDisposed)
            {
                return;
            }

            _trayIconDisposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        private static string Localize(string english, string simplifiedChinese, string traditionalChinese)
        {
            var language = CultureInfo.CurrentUICulture.Name ?? string.Empty;
            if (language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
                language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                language.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                language.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase))
            {
                return traditionalChinese;
            }

            return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? simplifiedChinese
                : english;
        }

        private void NewWindow(object sender, EventArgs e)
        {
            Process.Start("fluentterminalplus.exe", "new");
        }

        private async void OpenAppAsync(object sender, EventArgs e)
        {
            var appListEntries = await Package.Current.GetAppListEntriesAsync();
            await appListEntries.First().LaunchAsync();
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            Process.Start("fluentterminalplus.exe", "settings");
        }

        /// <summary>
        /// Checks whether the new light system theme in Windows 10 1903+ is used
        /// </summary>
        private bool SystemUsesLightTheme()
        {
            try
            {
                using (var regKey = RegistryKey.OpenRemoteBaseKey(RegistryHive.CurrentUser, string.Empty).OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (regKey.GetValueNames().Contains("SystemUsesLightTheme"))
                    {
                        var value = regKey.GetValue("SystemUsesLightTheme");
                        return value is int intValue && intValue == 1;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
