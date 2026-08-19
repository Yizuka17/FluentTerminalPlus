using FluentTerminal.Models;
using FluentTerminal.Models.Enums;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentTerminal.App.Services.Utilities;
using FluentTerminal.Models.Messages;
using Microsoft.Toolkit.Mvvm.Messaging;

namespace FluentTerminal.App.Services.Implementation
{
    public class SettingsService : ISettingsService
    {
        public const string CurrentThemeKey = "CurrentTheme";
        public const string DefaultShellProfileKey = "DefaultShellProfile";
        public const string DefaultSshProfileKey = "DefaultSshProfile";

        private static readonly Guid BuiltInWindowsPowerShellProfileId =
            Guid.Parse("813f2298-210a-481a-bdbf-c17bc637a3e2");

        private static readonly Guid PowerShell7ProfileId =
            Guid.Parse("5aae4468-bbd7-44b2-9b88-c6660dbee24c");

        private readonly IDefaultValueProvider _defaultValueProvider;
        private readonly IApplicationDataContainer _keyBindings;
        private readonly IApplicationDataContainer _localSettings;
        private readonly IApplicationDataContainer _roamingSettings;
        private readonly IApplicationDataContainer _shellProfiles;
        private readonly IApplicationDataContainer _sshProfiles;
        private readonly IApplicationDataContainer _themes;

        public SettingsService(IDefaultValueProvider defaultValueProvider, ApplicationDataContainers containers)
        {
            _defaultValueProvider = defaultValueProvider;
            _localSettings = containers.LocalSettings;
            _roamingSettings = containers.RoamingSettings;

            _themes = containers.Themes;
            _keyBindings = containers.KeyBindings;
            _shellProfiles = containers.ShellProfiles;
            _sshProfiles = containers.SshProfiles;

            foreach (var theme in _defaultValueProvider.GetPreInstalledThemes())
            {
                if (GetTheme(theme.Id) == null)
                {
                    _themes.WriteValueAsJson(theme.Id.ToString(), theme);
                }
            }

            foreach (var shellProfile in _defaultValueProvider.GetPreinstalledShellProfiles())
            {
                if (GetShellProfile(shellProfile.Id) == null)
                {
                    _shellProfiles.WriteValueAsJson(shellProfile.Id.ToString(), shellProfile);
                }
            }

            // Plus adds PowerShell 7 as a separate built-in profile. Do not repurpose or overwrite the
            // upstream Windows PowerShell 5.1 profile: users should be able to select either shell.
            if (_shellProfiles.ReadValueFromJson(PowerShell7ProfileId.ToString(), default(ShellProfile)) == null)
            {
                var powerShell7 = CreatePowerShell7Profile();
                _shellProfiles.WriteValueAsJson(powerShell7.Id.ToString(), powerShell7);
            }
        }

        private static ShellProfile CreatePowerShell7Profile()
        {
            return new ShellProfile
            {
                Id = PowerShell7ProfileId,
                Name = "PowerShell 7",
                MigrationVersion = ShellProfile.CurrentMigrationVersion,
                Arguments = string.Empty,
                // Do not persist a versioned Program Files/WindowsApps path. The full-trust broker
                // resolves pwsh.exe at launch time, including Microsoft Store app execution aliases.
                Location = "pwsh.exe",
                PreInstalled = true,
                WorkingDirectory = string.Empty,
                UseConPty = true,
                UseBuffer = false,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["TERM"] = "xterm-256color"
                }
            };
        }

        private static ShellProfile NormalizePlusBuiltInShellProfile(ShellProfile profile)
        {
            if (profile == null || !profile.PreInstalled)
            {
                return profile;
            }

            if (profile.Id == BuiltInWindowsPowerShellProfileId)
            {
                profile.Name = "Windows PowerShell";
            }
            else if (profile.Id == PowerShell7ProfileId)
            {
                // Migrate profiles created by early Plus builds, which stored
                // C:\Program Files\PowerShell\7\pwsh.exe and therefore missed Store installs.
                profile.Name = "PowerShell 7";
                profile.Location = "pwsh.exe";
                profile.UseConPty = true;
            }

            return profile;
        }

        public string ExportSettings()
        {
            var config = new
            {
                App = GetApplicationSettings(),
                KeyBindings = GetCommandKeyBindings(),
                TerminalOptions = GetTerminalOptions(),
                Themes = new List<TerminalTheme>(),
                Profiles = new List<ShellProfile>(),
                SshProfiles = new List<SshProfile>(),
                DefaultSettings = GetDefaultSettings(),
            };

            foreach (var theme in GetThemes().Where(x => !x.PreInstalled))
            {
                config.Themes.Add(theme);
            }

            foreach (var profile in GetShellProfiles())
            {
                config.Profiles.Add(profile);
            }

            foreach (var profile in GetSshProfiles())
            {
                config.SshProfiles.Add(profile);
            }

            return JsonConvert.SerializeObject(config, PreserveDictionaryKeyCaseContractResolver.SerializerSettings);
        }

        private Dictionary<string, string> GetDefaultSettings()
        {
            return new Dictionary<string, string>
            {
                [DefaultShellProfileKey] = GetDefaultShellProfileId().ToString(),
                [CurrentThemeKey] = GetCurrentThemeId().ToString(),
            };
        }

        public void ImportSettings(string serializedSettings)
        {
            var config = new
            {
                App = GetApplicationSettings(),
                KeyBindings = new Dictionary<string, ICollection<KeyBinding>>(),
                Themes = new List<TerminalTheme>(),
                Profiles = new List<ShellProfile>(),
                SshProfiles = new List<SshProfile>(),
                TerminalOptions = GetTerminalOptions(),
                DefaultSettings = new Dictionary<string, string>(),
            };

            JsonConvert.PopulateObject(serializedSettings, config);

            SaveApplicationSettings(config.App);

            foreach (var pair in config.KeyBindings)
            {
                SaveKeyBindings(pair.Key, pair.Value);
            }

            foreach (var theme in config.Themes.Where(x => !x.PreInstalled))
            {
                var existingTheme = GetTheme(theme.Id);
                if (existingTheme?.PreInstalled == true)
                {
                    continue;
                }
                SaveTheme(theme, existingTheme != null);
            }

            foreach (var profile in config.Profiles)
            {
                var existingProfile = GetShellProfile(profile.Id);
                var isNew = existingProfile == default;

                if (!isNew && existingProfile.PreInstalled)
                {
                    profile.Name = existingProfile.Name;
                    profile.Location = existingProfile.Location;

                    SaveShellProfile(profile, false);
                    continue;
                }
                SaveShellProfile(profile, isNew);
            }

            foreach (var profile in config.SshProfiles)
            {
                var existingProfile = GetSshProfile(profile.Id);
                var isNew = existingProfile == default;

                SaveSshProfile(profile, isNew);
            }

            SaveTerminalOptions(config.TerminalOptions);

            if (config.DefaultSettings.ContainsKey(DefaultShellProfileKey))
            {
                SaveDefaultShellProfileId(new Guid(config.DefaultSettings[DefaultShellProfileKey]));
            }

            if (config.DefaultSettings.ContainsKey(CurrentThemeKey))
            {
                SaveCurrentThemeId(new Guid(config.DefaultSettings[CurrentThemeKey]));
            }
        }

        public void DeleteShellProfile(Guid id)
        {
            _shellProfiles.Delete(id.ToString());
            WeakReferenceMessenger.Default.Send(new ShellProfileDeletedMessage(id));
        }

        public void DeleteSshProfile(Guid id)
        {
            _sshProfiles.Delete(id.ToString());
            WeakReferenceMessenger.Default.Send(new ShellProfileDeletedMessage(id));
            WeakReferenceMessenger.Default.Send(new KeyBindingsChangedMessage());
        }

        public void DeleteTheme(Guid id)
        {
            _themes.Delete(id.ToString());

            foreach (var profile in GetShellProfiles())
            {
                if (profile.TerminalThemeId == id)
                {
                    profile.TerminalThemeId = Guid.Empty;
                    SaveShellProfile(profile);
                }
            }

            WeakReferenceMessenger.Default.Send(new ThemeDeletedMessage(id));
        }

        public ApplicationSettings GetApplicationSettings()
        {
            return _roamingSettings.ReadValueFromJson(nameof(ApplicationSettings), _defaultValueProvider.GetDefaultApplicationSettings());
        }

        public TerminalTheme GetCurrentTheme()
        {
            var id = GetCurrentThemeId();
            var theme = GetTheme(id);
            if (theme == null)
            {
                id = _defaultValueProvider.GetDefaultThemeId();
                SaveCurrentThemeId(id);
                theme = GetTheme(id);
            }
            return theme;
        }

        public Guid GetCurrentThemeId()
        {
            if (_roamingSettings.TryGetValue(CurrentThemeKey, out object value))
            {
                return (Guid)value;
            }
            return _defaultValueProvider.GetDefaultThemeId();
        }

        public ShellProfile GetDefaultShellProfile()
        {
            var id = GetDefaultShellProfileId();
            var profile = GetShellProfile(id);
            if (profile == null)
            {
                id = PowerShell7ProfileId;
                SaveDefaultShellProfileId(id);
                profile = GetShellProfile(id);
            }
            return profile;
        }

        public ShellProfile GetShellProfile(Guid id)
        {
            return NormalizePlusBuiltInShellProfile(
                _shellProfiles.ReadValueFromJson(id.ToString(), default(ShellProfile)));
        }

        public SshProfile GetSshProfile(Guid id)
        {
            return _sshProfiles.ReadValueFromJson(id.ToString(), default(SshProfile));
        }

        public Guid GetDefaultShellProfileId()
        {
            if (_localSettings.TryGetValue(DefaultShellProfileKey, out object value))
            {
                return (Guid)value;
            }
            return PowerShell7ProfileId;
        }

        public IDictionary<string, ICollection<KeyBinding>> GetCommandKeyBindings()
        {
            var keyBindings = new Dictionary<string, ICollection<KeyBinding>>();

            foreach (Command command in Enum.GetValues(typeof(Command)))
            {
                keyBindings.Add(command.ToString(), _keyBindings.ReadValueFromJson<Collection<KeyBinding>>(command.ToString(), null) ?? _defaultValueProvider.GetDefaultKeyBindings(command));
            }
            return keyBindings;
        }

        public IEnumerable<ShellProfile> GetShellProfiles()
        {
            return _shellProfiles.GetAll()
                .Select(x => JsonConvert.DeserializeObject<ShellProfile>((string)x))
                .Select(MoshBackwardCompatibilityFixProfile)
                .Select(NormalizePlusBuiltInShellProfile)
                .ToList();
        }

        public IEnumerable<SshProfile> GetSshProfiles()
        {
            return _sshProfiles.GetAll().Select(x => JsonConvert.DeserializeObject<ShellProfile>((string)x))
                .Select(MoshBackwardCompatibilityFixProfile).Cast<SshProfile>();
        }

        public IEnumerable<ShellProfile> GetAllProfiles()
        {
            return GetShellProfiles().Union(GetSshProfiles());
        }

        private ShellProfile MoshBackwardCompatibilityFixProfile(ShellProfile profile)
        {
            var fixedProfile = MoshBackwardCompatibility.FixProfile(profile);

            if (ReferenceEquals(fixedProfile, profile))
            {
                return fixedProfile;
            }

            if (fixedProfile is SshProfile sshProfile)
            {
                DeleteSshProfile(profile.Id);
                SaveSshProfile(sshProfile);
            }
            else
            {
                DeleteShellProfile(profile.Id);
                SaveShellProfile(fixedProfile);
            }

            return fixedProfile;
        }

        public IEnumerable<TabTheme> GetTabThemes()
        {
            return _defaultValueProvider.GetDefaultTabThemes();
        }

        public TerminalOptions GetTerminalOptions()
        {
            return _roamingSettings.ReadValueFromJson(nameof(TerminalOptions), _defaultValueProvider.GetDefaultTerminalOptions());
        }

        public TerminalTheme GetTheme(Guid id)
        {
            return _themes.ReadValueFromJson(id.ToString(), default(TerminalTheme));
        }

        public IEnumerable<TerminalTheme> GetThemes()
        {
            return _themes.GetAll().Select(x => JsonConvert.DeserializeObject<TerminalTheme>((string)x)).ToList();
        }

        public void ResetKeyBindings()
        {
            foreach (Command command in Enum.GetValues(typeof(Command)))
            {
                _keyBindings.WriteValueAsJson(command.ToString(), _defaultValueProvider.GetDefaultKeyBindings(command));
            }

            WeakReferenceMessenger.Default.Send(new KeyBindingsChangedMessage());
        }

        public void SaveApplicationSettings(ApplicationSettings applicationSettings)
        {
            _roamingSettings.WriteValueAsJson(nameof(ApplicationSettings), applicationSettings);
            WeakReferenceMessenger.Default.Send(new ApplicationSettingsChangedMessage(applicationSettings.Clone()));
        }

        public void NotifyApplicationSettingsChanged(ApplicationSettings applicationSettings)
        {
            WeakReferenceMessenger.Default.Send(new ApplicationSettingsChangedMessage(applicationSettings.Clone()));
        }

        public void SaveCurrentThemeId(Guid id)
        {
            _roamingSettings.SetValue(CurrentThemeKey, id);

            WeakReferenceMessenger.Default.Send(new CurrentThemeChangedMessage(id));
        }

        public void SaveDefaultShellProfileId(Guid id)
        {
            _localSettings.SetValue(DefaultShellProfileKey, id);

            WeakReferenceMessenger.Default.Send(new DefaultShellProfileChangedMessage(id));
        }

        public void SaveKeyBindings(string command, ICollection<KeyBinding> keyBindings)
        {
            if (!Enum.TryParse<Command>(command, true, out var enumValue))
            {
                throw new InvalidOperationException();
            }

            _keyBindings.WriteValueAsJson(enumValue.ToString(), keyBindings);
            WeakReferenceMessenger.Default.Send(new KeyBindingsChangedMessage());
        }

        public void SaveShellProfile(ShellProfile shellProfile, bool newShell = false)
        {
            _shellProfiles.WriteValueAsJson(shellProfile.Id.ToString(), shellProfile);

            WeakReferenceMessenger.Default.Send(new KeyBindingsChangedMessage());

            if (newShell)
            {
                WeakReferenceMessenger.Default.Send(new ShellProfileAddedMessage(shellProfile));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new ShellProfileChangedMessage(shellProfile));
            }
        }

        public void SaveSshProfile(SshProfile sshProfile, bool newShell = false)
        {
            _sshProfiles.WriteValueAsJson(sshProfile.Id.ToString(), sshProfile);

            WeakReferenceMessenger.Default.Send(new KeyBindingsChangedMessage());

            if (newShell)
            {
                WeakReferenceMessenger.Default.Send(new ShellProfileAddedMessage(sshProfile));
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new ShellProfileChangedMessage(sshProfile));
            }
        }

        public void SaveTerminalOptions(TerminalOptions terminalOptions)
        {
            _roamingSettings.WriteValueAsJson(nameof(TerminalOptions), terminalOptions);
            WeakReferenceMessenger.Default.Send(new TerminalOptionsChangedMessage(terminalOptions));
        }

        public void SaveTheme(TerminalTheme theme, bool newTheme = false)
        {
            _themes.WriteValueAsJson(theme.Id.ToString(), theme);

            if (theme.Id == GetCurrentThemeId())
            {
                WeakReferenceMessenger.Default.Send(new CurrentThemeChangedMessage(theme.Id));
            }

            if (newTheme)
            {
                WeakReferenceMessenger.Default.Send(new ThemeAddedMessage(theme));
            }
        }
    }
}
