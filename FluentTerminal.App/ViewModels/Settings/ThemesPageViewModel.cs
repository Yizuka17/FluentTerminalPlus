using FluentTerminal.App.Services;
using FluentTerminal.App.Services.Utilities;
using FluentTerminal.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FluentTerminal.Models.Messages;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Mvvm.Messaging;
using System.Windows.Input;

namespace FluentTerminal.App.ViewModels.Settings
{
    public class ThemesPageViewModel : ObservableObject,
        IRecipient<TerminalOptionsChangedMessage>
    {
        private readonly IDefaultValueProvider _defaultValueProvider;
        private readonly IDialogService _dialogService;
        private readonly ISettingsService _settingsService;
        private ThemeViewModel _selectedTheme;
        private double _backgroundOpacity;
        private readonly IThemeParserFactory _themeParserFactory;
        private readonly IFileSystemService _fileSystemService;
        private readonly IImageFileSystemService _imageFileSystemService;

        public event EventHandler<string> SelectedThemeBackgroundColorChanged;
        public event EventHandler<ImageFile> SelectedThemeBackgroundImageChanged;
        public event EventHandler<ThemeViewModel> SelectedThemeChanged;

        public ThemesPageViewModel(ISettingsService settingsService,
                                   IDialogService dialogService,
                                   IDefaultValueProvider defaultValueProvider,
                                   IThemeParserFactory themeParserFactory,
                                   IFileSystemService fileSystemService,
                                   IImageFileSystemService imageFileSystemService)
        {
            _settingsService = settingsService;
            _dialogService = dialogService;
            _defaultValueProvider = defaultValueProvider;
            _themeParserFactory = themeParserFactory;
            _fileSystemService = fileSystemService;
            _imageFileSystemService = imageFileSystemService;

            CreateThemeCommand = new RelayCommand(CreateTheme);
            ImportThemeCommand = new AsyncRelayCommand(ImportThemeAsync);
            CloneCommand = new RelayCommand<ThemeViewModel>(CloneTheme);

            BackgroundOpacity = _settingsService.GetTerminalOptions().BackgroundOpacity;

            EnsureWindowsTerminalThemes(_settingsService);

            var activeThemeId = _settingsService.GetCurrentThemeId();
            foreach (var theme in _settingsService.GetThemes())
            {
                var viewModel = new ThemeViewModel(theme, _settingsService, _dialogService, _fileSystemService, _imageFileSystemService, false);
                viewModel.Activated += OnThemeActivated;
                viewModel.Deleted += OnThemeDeleted;

                if (theme.Id == activeThemeId)
                {
                    viewModel.IsActive = true;
                }
                Themes.Add(viewModel);
            }

            SelectedTheme = Themes.First(t => t.IsActive);

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public static void EnsureWindowsTerminalThemes(ISettingsService settingsService)
        {
            foreach (var theme in GetWindowsTerminalThemes())
            {
                if (settingsService.GetTheme(theme.Id) == null)
                {
                    settingsService.SaveTheme(theme, true);
                }
            }
        }

        private static IEnumerable<TerminalTheme> GetWindowsTerminalThemes()
        {
            yield return CreateTheme(
                "8a4acdfa-9fbe-4bbc-a73d-0aef8504f6ee", "Windows Terminal Default Dark",
                "#0C0C0C", "#CCCCCC", "#FFFFFF", "#000000",
                "#0C0C0C", "#C50F1F", "#13A10E", "#C19C00", "#0037DA", "#881798", "#3A96DD", "#CCCCCC",
                "#767676", "#E74856", "#16C60C", "#F9F1A5", "#3B78FF", "#B4009E", "#61D6D6", "#F2F2F2");

            yield return CreateTheme(
                "b77e7d74-53bb-4543-9fac-c9145e0f9230", "Windows Terminal Default Light",
                "#FFFFFF", "#0C0C0C", "#0C0C0C", "#FFFFFF",
                "#0C0C0C", "#C50F1F", "#13A10E", "#C19C00", "#0037DA", "#881798", "#3A96DD", "#767676",
                "#767676", "#E74856", "#16C60C", "#C19C00", "#3B78FF", "#B4009E", "#008080", "#0C0C0C");

            yield return CreateTheme(
                "ccb64e97-8518-4391-9fe5-3ca55b037c9d", "One Half Dark",
                "#282C34", "#DCDFE4", "#FFFFFF", "#282C34",
                "#282C34", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#DCDFE4",
                "#5A6374", "#E06C75", "#98C379", "#E5C07B", "#61AFEF", "#C678DD", "#56B6C2", "#DCDFE4");

            yield return CreateTheme(
                "172a8ad3-6acf-4f1b-956a-536fc93fa0ba", "One Half Light",
                "#FAFAFA", "#383A42", "#4F525D", "#FAFAFA",
                "#383A42", "#E45649", "#50A14F", "#C18301", "#0184BC", "#A626A4", "#0997B3", "#FAFAFA",
                "#4F525D", "#DF6C75", "#98C379", "#E4C07A", "#61AFEF", "#C577DD", "#56B5C1", "#FFFFFF");

            yield return CreateTheme(
                "e907b17e-fcaa-4751-a1de-639615b59ef3", "Tango Dark",
                "#000000", "#D3D7CF", "#FFFFFF", "#000000",
                "#000000", "#CC0000", "#4E9A06", "#C4A000", "#3465A4", "#75507B", "#06989A", "#D3D7CF",
                "#555753", "#EF2929", "#8AE234", "#FCE94F", "#729FCF", "#AD7FA8", "#34E2E2", "#EEEEEC");

            yield return CreateTheme(
                "47a1367e-9cad-49c0-adbe-72faad6a0f46", "Tango Light",
                "#FFFFFF", "#555753", "#000000", "#FFFFFF",
                "#000000", "#CC0000", "#4E9A06", "#C4A000", "#3465A4", "#75507B", "#06989A", "#D3D7CF",
                "#555753", "#EF2929", "#8AE234", "#FCE94F", "#729FCF", "#AD7FA8", "#34E2E2", "#EEEEEC");

            yield return CreateTheme(
                "bc2d2bf6-4f85-4d4f-a96e-354e6d54006a", "Solarized Dark",
                "#073642", "#839496", "#FFFFFF", "#073642",
                "#073642", "#DC322F", "#859900", "#B58900", "#268BD2", "#D33682", "#2AA198", "#EEE8D5",
                "#002B36", "#CB4B16", "#586E75", "#657B83", "#839496", "#6C71C4", "#93A1A1", "#FDF6E3");

            yield return CreateTheme(
                "61bd010a-1253-44ba-9bc8-692c2c2b03ce", "Solarized Light",
                "#FDF6E3", "#657B83", "#002B36", "#FDF6E3",
                "#002B36", "#DC322F", "#859900", "#B58900", "#268BD2", "#D33682", "#2AA198", "#EEE8D5",
                "#073642", "#CB4B16", "#586E75", "#657B83", "#839496", "#6C71C4", "#93A1A1", "#FDF6E3");

            yield return CreateTheme(
                "65ee7f6d-125a-4d8d-a56e-bfd9f40a7ea0", "Gruvbox Dark",
                "#282828", "#EBDBB2", "#EBDBB2", "#282828",
                "#282828", "#CC241D", "#98971A", "#D79921", "#458588", "#B16286", "#689D6A", "#A89984",
                "#928374", "#FB4934", "#B8BB26", "#FABD2F", "#83A598", "#D3869B", "#8EC07C", "#EBDBB2");

            yield return CreateTheme(
                "f16c8fd8-e067-498b-a57c-6b331455cf87", "Gruvbox Light",
                "#FBF1C7", "#3C3836", "#3C3836", "#FBF1C7",
                "#282828", "#CC241D", "#98971A", "#D79921", "#458588", "#B16286", "#689D6A", "#A89984",
                "#928374", "#9D0006", "#79740E", "#B57614", "#076678", "#8F3F71", "#427B58", "#3C3836");

            yield return CreateTheme(
                "a01bceef-3596-41b2-b5d8-329959e9f93b", "Catppuccin Mocha Dark",
                "#1E1E2E", "#CDD6F4", "#F5E0DC", "#1E1E2E",
                "#45475A", "#F38BA8", "#A6E3A1", "#F9E2AF", "#89B4FA", "#F5C2E7", "#94E2D5", "#BAC2DE",
                "#585B70", "#F38BA8", "#A6E3A1", "#F9E2AF", "#89B4FA", "#F5C2E7", "#94E2D5", "#A6ADC8");

            yield return CreateTheme(
                "ab647e1e-03f4-4d29-9b45-8e0a97a86c78", "Catppuccin Latte Light",
                "#EFF1F5", "#4C4F69", "#DC8A78", "#EFF1F5",
                "#5C5F77", "#D20F39", "#40A02B", "#DF8E1D", "#1E66F5", "#EA76CB", "#179299", "#ACB0BE",
                "#6C6F85", "#D20F39", "#40A02B", "#DF8E1D", "#1E66F5", "#EA76CB", "#179299", "#4C4F69");

            yield return CreateTheme(
                "06ed31c1-b9d2-4e45-a149-c7154e2587de", "Nord Dark",
                "#2E3440", "#D8DEE9", "#D8DEE9", "#2E3440",
                "#3B4252", "#BF616A", "#A3BE8C", "#EBCB8B", "#81A1C1", "#B48EAD", "#88C0D0", "#E5E9F0",
                "#4C566A", "#BF616A", "#A3BE8C", "#EBCB8B", "#5E81AC", "#B48EAD", "#8FBCBB", "#ECEFF4");

            yield return CreateTheme(
                "e6b8c0d3-4496-4760-98f9-50ad5d452b2e", "Nord Snow Light",
                "#ECEFF4", "#2E3440", "#2E3440", "#ECEFF4",
                "#3B4252", "#BF616A", "#A3BE8C", "#EBCB8B", "#5E81AC", "#B48EAD", "#8FBCBB", "#D8DEE9",
                "#4C566A", "#BF616A", "#A3BE8C", "#EBCB8B", "#81A1C1", "#B48EAD", "#88C0D0", "#2E3440");

            yield return CreateTheme(
                "fa2adf51-e244-41d8-9df5-8790ea320974", "Vintage Dark",
                "#000000", "#C0C0C0", "#FFFFFF", "#000000",
                "#000000", "#800000", "#008000", "#808000", "#000080", "#800080", "#008080", "#C0C0C0",
                "#808080", "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#FF00FF", "#00FFFF", "#FFFFFF");

            yield return CreateTheme(
                "b2508e31-e15c-4eaf-8fdf-384deee7a2c4", "Vintage Light",
                "#FFFFFF", "#000000", "#000000", "#FFFFFF",
                "#000000", "#800000", "#008000", "#808000", "#000080", "#800080", "#008080", "#C0C0C0",
                "#808080", "#FF0000", "#00FF00", "#FFFF00", "#0000FF", "#FF00FF", "#00FFFF", "#FFFFFF");
        }

        private static TerminalTheme CreateTheme(
            string id,
            string name,
            string background,
            string foreground,
            string cursor,
            string cursorAccent,
            string black,
            string red,
            string green,
            string yellow,
            string blue,
            string magenta,
            string cyan,
            string white,
            string brightBlack,
            string brightRed,
            string brightGreen,
            string brightYellow,
            string brightBlue,
            string brightMagenta,
            string brightCyan,
            string brightWhite)
        {
            return new TerminalTheme
            {
                Id = Guid.Parse(id),
                Author = "Windows Terminal inspired / FluentTerminalPlus",
                Name = name,
                PreInstalled = true,
                Colors = new TerminalColors
                {
                    Background = background,
                    Foreground = foreground,
                    Cursor = cursor,
                    CursorAccent = cursorAccent,
                    Selection = name.EndsWith("Light", StringComparison.OrdinalIgnoreCase)
                        ? "rgba(0, 0, 0, 0.25)"
                        : "rgba(255, 255, 255, 0.30)",
                    Black = black,
                    Red = red,
                    Green = green,
                    Yellow = yellow,
                    Blue = blue,
                    Magenta = magenta,
                    Cyan = cyan,
                    White = white,
                    BrightBlack = brightBlack,
                    BrightRed = brightRed,
                    BrightGreen = brightGreen,
                    BrightYellow = brightYellow,
                    BrightBlue = brightBlue,
                    BrightMagenta = brightMagenta,
                    BrightCyan = brightCyan,
                    BrightWhite = brightWhite
                }
            };
        }

        public ICommand CreateThemeCommand { get; }
        public ICommand ImportThemeCommand { get; }
        public ICommand CloneCommand { get; set; }

        public double BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => SetProperty(ref _backgroundOpacity, value);
        }

        public ThemeViewModel SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (_selectedTheme != null)
                {
                    _selectedTheme.BackgroundChanged -= OnSelectedThemeBackgroundChanged;
                    _selectedTheme.BackgroundImageChanged -= OnSelectedThemeBackgroundImageChanged;
                }

                SetProperty(ref _selectedTheme, value);
                SelectedThemeChanged?.Invoke(this, _selectedTheme);

                if (value != null)
                {
                    _selectedTheme.BackgroundOpacity = BackgroundOpacity;
                    value.BackgroundChanged += OnSelectedThemeBackgroundChanged;
                    value.BackgroundImageChanged += OnSelectedThemeBackgroundImageChanged;
                }
            }
        }

        public ObservableCollection<ThemeViewModel> Themes { get; } = new ObservableCollection<ThemeViewModel>();

        private void CloneTheme(ThemeViewModel theme)
        {
            var cloned = new TerminalTheme(theme.Model)
            {
                Id = Guid.NewGuid(),
                PreInstalled = false,
                Name = $"Copy of {theme.Name}"
            };

            AddTheme(cloned);
        }

        private void CreateTheme()
        {
            var defaultTheme = _settingsService.GetTheme(_defaultValueProvider.GetDefaultThemeId());
            var theme = new TerminalTheme
            {
                Id = Guid.NewGuid(),
                PreInstalled = false,
                Name = "New Theme",
                Colors = new TerminalColors(defaultTheme.Colors)
            };

            AddTheme(theme);
        }

        // Requires UI thread
        private async Task ImportThemeAsync()
        {
            // ConfigureAwait(true) because we need to execute AddTheme method in the calling (UI) thread.
            var file = await _fileSystemService.OpenFileAsync(_themeParserFactory.SupportedFileTypes)
                .ConfigureAwait(true);

            if (file != null)
            {
                var parser = _themeParserFactory.GetParser(file.FileType);

                if (parser == null)
                {
                    await _dialogService.ShowMessageDialogAsync(I18N.Translate("ImportThemeFailed"),
                        I18N.Translate("NoSuitableParserFound"), DialogButton.OK).ConfigureAwait(false);

                    return;
                }

                try
                {
                    // ConfigureAwait(true) because we need to execute AddTheme method in the calling (UI) thread.
                    var exportedTheme = await parser.Import(file.Name, file.Content).ConfigureAwait(true);

                    if (!string.IsNullOrWhiteSpace(exportedTheme.EncodedImage))
                    {
                        // ConfigureAwait(true) because we need to execute AddTheme method in the calling (UI) thread.
                        var importedImage = await _imageFileSystemService
                            .ImportThemeImageAsync(exportedTheme.BackgroundImage, exportedTheme.EncodedImage)
                            .ConfigureAwait(true);

                        exportedTheme.BackgroundImage = importedImage;
                    }

                    var terminalTheme = new TerminalTheme(exportedTheme);

                    AddTheme(terminalTheme);
                }
                catch (Exception exception)
                {
                    await _dialogService
                        .ShowMessageDialogAsync(I18N.Translate("ImportThemeFailed"), exception.Message, DialogButton.OK)
                        .ConfigureAwait(false);
                }
            }
        }

        private void AddTheme(TerminalTheme theme)
        {
            _settingsService.SaveTheme(theme, true);

            var viewModel = new ThemeViewModel(theme, _settingsService, _dialogService, _fileSystemService, _imageFileSystemService, true);
            viewModel.EditCommand.Execute(null);
            viewModel.Activated += OnThemeActivated;
            viewModel.Deleted += OnThemeDeleted;
            Themes.Add(viewModel);
            SelectedTheme = viewModel;
        }

        private void OnThemeActivated(object sender, EventArgs e)
        {
            if (sender is ThemeViewModel activatedTheme)
            {
                _settingsService.SaveCurrentThemeId(activatedTheme.Id);

                foreach (var theme in Themes)
                {
                    theme.IsActive = theme.Id == activatedTheme.Id;
                }
            }
        }

        private void OnThemeDeleted(object sender, EventArgs e)
        {
            if (sender is ThemeViewModel theme)
            {
                if (SelectedTheme == theme)
                {
                    SelectedTheme = Themes.First();
                }
                Themes.Remove(theme);

                if (theme.IsActive)
                {
                    Themes.First().IsActive = true;
                    _settingsService.SaveCurrentThemeId(Themes.First().Id);
                }
                _settingsService.DeleteTheme(theme.Id);
            }
        }

        public void Receive(TerminalOptionsChangedMessage message)
        {
            BackgroundOpacity = message.TerminalOptions.BackgroundOpacity;
        }

        private void OnSelectedThemeBackgroundChanged(object sender, string e)
        {
            SelectedThemeBackgroundColorChanged?.Invoke(this, e);
        }

        private void OnSelectedThemeBackgroundImageChanged(object sender, ImageFile e)
        {
            SelectedThemeBackgroundImageChanged?.Invoke(this, e);
        }
    }
}