using FluentTerminal.App.Services;
using FluentTerminal.App.Services.Utilities;
using FluentTerminal.App.ViewModels.Settings;
using System;
using System.Linq;
using System.Threading;
using Windows.Globalization;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace FluentTerminal.App.Views.SettingsPages
{
    public sealed partial class GeneralSettings : Page
    {
        private bool _loadingExitTrayWhenLastWindowClosed;

        public GeneralPageViewModel ViewModel { get; private set; }

        public GeneralSettings()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is GeneralPageViewModel viewModel)
            {
                ViewModel = viewModel;
                LoadExitTrayWhenLastWindowClosedSetting();
                // ReSharper disable once AssignmentIsFullyDiscarded
                _ = ViewModel.OnNavigatedToAsync();
            }
        }

        private void LoadExitTrayWhenLastWindowClosedSetting()
        {
            _loadingExitTrayWhenLastWindowClosed = true;

            var values = ApplicationData.Current.LocalSettings.Values;
            ExitTrayWhenLastWindowClosedToggle.IsOn =
                values.TryGetValue(Constants.ExitTrayWhenLastWindowClosedKey, out var value) &&
                value is bool enabled && enabled;

            _loadingExitTrayWhenLastWindowClosed = false;
        }

        private void ExitTrayWhenLastWindowClosed_Toggled(object sender, RoutedEventArgs e)
        {
            if (_loadingExitTrayWhenLastWindowClosed)
            {
                return;
            }

            ApplicationData.Current.LocalSettings.Values[Constants.ExitTrayWhenLastWindowClosedKey] =
                ExitTrayWhenLastWindowClosedToggle.IsOn;
        }
    }

    public sealed class PlusLocalizedStrings
    {
        public string ExitTrayWhenAllWindowsClosed => Localize(
            "Exit tray process when all windows are closed",
            "关闭所有窗口时退出托盘进程",
            "關閉所有視窗時結束系統匣程序");

        public string RunAsAdministrator => Localize(
            "Run as administrator",
            "以管理员身份运行",
            "以系統管理員身分執行");

        private static string Localize(string english, string simplifiedChinese, string traditionalChinese)
        {
            var language = ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
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
    }
}

namespace FluentTerminal.App
{
    public sealed partial class App
    {
        private static int _trackedWindowCount;

        internal static void NotifyTrackedWindowCreated()
        {
            var count = Interlocked.Increment(ref _trackedWindowCount);
            PublishTrackedWindowCount(count);
        }

        internal static void NotifyTrackedWindowClosed()
        {
            var remaining = Interlocked.Decrement(ref _trackedWindowCount);
            if (remaining < 0)
            {
                Interlocked.Exchange(ref _trackedWindowCount, 0);
                remaining = 0;
            }

            PublishTrackedWindowCount(remaining);
        }

        private static void PublishTrackedWindowCount(int count)
        {
            ApplicationData.Current.LocalSettings.Values[Constants.ActiveFrontendWindowCountKey] = count;
        }
    }
}