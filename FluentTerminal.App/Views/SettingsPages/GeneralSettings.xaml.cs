using FluentTerminal.App.Services;
using FluentTerminal.App.Services.Utilities;
using FluentTerminal.App.ViewModels.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;
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
}

namespace FluentTerminal.App
{
    public sealed partial class App
    {
        private static int _trackedWindowCount;

        internal static void NotifyTrackedWindowCreated()
        {
            Interlocked.Increment(ref _trackedWindowCount);
        }

        internal static void NotifyTrackedWindowClosed()
        {
            var remaining = Interlocked.Decrement(ref _trackedWindowCount);
            if (remaining > 0)
            {
                return;
            }

            // Keep the counter sane even if Windows sends a duplicate close notification.
            Interlocked.Exchange(ref _trackedWindowCount, 0);

            var values = ApplicationData.Current.LocalSettings.Values;
            var exitTray = values.TryGetValue(Constants.ExitTrayWhenLastWindowClosedKey, out var value) &&
                           value is bool enabled && enabled;

            if (exitTray && Windows.UI.Xaml.Application.Current is App app)
            {
                // ReSharper disable once AssignmentIsFullyDiscarded
                _ = app.QuitTrayAfterLastWindowClosedAsync();
            }
        }

        private async Task QuitTrayAfterLastWindowClosedAsync()
        {
            try
            {
                await _trayProcessCommunicationService.QuitApplicationAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Instance.Debug("Failed to exit tray process after the last window closed. Exception: {0}", e);
            }
        }
    }
}