using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Windows.ApplicationModel;

namespace FluentTerminal.SystemTray
{
    public class SystemTrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private bool _trayIconDisposed;

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
        }

        private void Exit(object sender, EventArgs e)
        {
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
