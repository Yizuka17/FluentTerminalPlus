using FluentTerminal.App.Services;
using FluentTerminal.App.Services.Utilities;
using FluentTerminal.App.ViewModels.Settings;
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
        // ApplicationViewAdapter already gives us a reliable close notification. The old
        // counter could drift when UWP views were consolidated in an unexpected order, so
        // use the application's real view-model state instead.
        internal static void NotifyTrackedWindowCreated()
        {
        }

        internal static void NotifyTrackedWindowClosed()
        {
            if (Windows.UI.Xaml.Application.Current is App app)
            {
                app.ExitAfterLastWindowClosed();
            }
        }

        private void ExitAfterLastWindowClosed()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            var exitTray = values.TryGetValue(Constants.ExitTrayWhenLastWindowClosedKey, out var value) &&
                           value is bool enabled && enabled;

            if (!exitTray || _mainViewModels.Count > 0 || _settingsViewModel != null)
            {
                return;
            }

            Logger.Instance.Debug("Last FluentTerminalPlus window closed; releasing AppService deferral and exiting UWP process.");

            // The tray owns the AppServiceConnection and the UWP side owns this deferral.
            // Completing it breaks the lifetime cycle immediately instead of asking the tray
            // to quit over the very connection that is keeping this process alive.
            var deferral = _appServiceDeferral;
            _appServiceDeferral = null;
            _appServiceConnection = null;
            deferral?.Complete();

            Windows.UI.Xaml.Application.Current.Exit();
        }
    }
}
