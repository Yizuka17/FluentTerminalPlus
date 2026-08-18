using FluentTerminal.App.Services;
using FluentTerminal.Models;
using GlobalHotKey;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace FluentTerminal.SystemTray.Services
{
    /// <summary>
    /// Compatibility shell for Fluent Terminal's global window-toggle feature.
    ///
    /// FluentTerminalPlus intentionally disables the legacy global hotkey for the MVP:
    /// upstream uses a machine-wide hotkey and launches the hard-coded ftcmd:// protocol,
    /// both of which conflict with a side-by-side Fluent Terminal installation. Keeping
    /// the service interface intact avoids touching the AppService/message plumbing while
    /// making the feature inert until Plus gets its own optional implementation.
    /// </summary>
    public class ToggleWindowService : IDisposable
    {
        private readonly HotKeyManager _hotKeyManager;
        private bool _disposedValue;

        public ToggleWindowService(Dispatcher dispatcher, HotKeyManager hotKeyManager,
            INotificationService notificationService, ISettingsService settingsService)
        {
            _hotKeyManager = hotKeyManager;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void SetHotKeys(IEnumerable<KeyBinding> keyBindings)
        {
            // Intentionally disabled for the FluentTerminalPlus MVP.
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _hotKeyManager?.Dispose();
                }

                _disposedValue = true;
            }
        }
    }
}
