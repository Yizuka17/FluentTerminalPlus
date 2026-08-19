using Microsoft.Toolkit.Uwp.Helpers;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace FluentTerminal.App.Utilities
{
    public static class AppThemeManager
    {
        public const string AppThemeModeKey = "AppThemeMode";

        // 0 = follow system, 1 = light, 2 = dark.
        public static int GetModeIndex()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            if (values.TryGetValue(AppThemeModeKey, out var value) && value is int mode && mode >= 0 && mode <= 2)
            {
                return mode;
            }

            return 0;
        }

        public static void SetModeIndex(int mode)
        {
            if (mode < 0 || mode > 2)
            {
                mode = 0;
            }

            ApplicationData.Current.LocalSettings.Values[AppThemeModeKey] = mode;
        }

        public static ElementTheme GetRequestedTheme()
        {
            switch (GetModeIndex())
            {
                case 1:
                    return ElementTheme.Light;
                case 2:
                    return ElementTheme.Dark;
                default:
                    return ElementTheme.Default;
            }
        }
    }

    public static class ContrastHelper
    {
        public static ElementTheme GetIdealThemeForBackgroundColor(string color)
        {
            return GetIdealThemeForBackgroundColor(color.ToColor());
        }

        public static ElementTheme GetIdealThemeForBackgroundColor(Color color)
        {
            if (((color.R * 0.299) + (color.G * 0.587) + (color.B * 0.114)) > 186)
            {
                return ElementTheme.Light;
            }
            else
            {
                return ElementTheme.Dark;
            }
        }

        public static ElementTheme ResolveTheme(ElementTheme theme)
        {
            if (theme != ElementTheme.Default)
            {
                return theme;
            }

            // ElementTheme.Default follows Windows. Resolve it only for APIs such as the
            // native title-bar button colors that require an explicit light/dark choice.
            var background = new UISettings().GetColorValue(UIColorType.Background);
            return GetIdealThemeForBackgroundColor(background);
        }

        public static void SetTitleBarButtonsForTheme(ElementTheme theme)
        {
            theme = ResolveTheme(theme);
            var titleBar = ApplicationView.GetForCurrentView().TitleBar;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            titleBar.ButtonForegroundColor = GetColor("SystemBaseHighColor", theme);
            titleBar.ButtonInactiveForegroundColor = GetColor("SystemBaseHighColor", theme);
            titleBar.ButtonHoverForegroundColor = GetColor("SystemBaseHighColor", theme);
            titleBar.ButtonPressedForegroundColor = GetColor("SystemBaseHighColor", theme);

            titleBar.ButtonHoverBackgroundColor = GetColor("SystemListLowColor", theme);
            titleBar.ButtonPressedBackgroundColor = GetColor("SystemListMediumColor", theme);
        }

        public static Color? GetColor(string name, ElementTheme theme)
        {
            theme = ResolveTheme(theme);
            if (theme == ElementTheme.Light)
            {
                switch (name)
                {
                    case "SystemBaseHighColor":
                        return new Color { A = 0xFF, R = 0x00, G = 0x00, B = 0x00 };

                    case "SystemListLowColor":
                        return new Color { A = 0x19, R = 0x00, G = 0x00, B = 0x00 };

                    case "SystemListMediumColor":
                        return new Color { A = 0x33, R = 0x00, G = 0x00, B = 0x00 };

                    default:
                        return null;
                }
            }
            else
            {
                switch (name)
                {
                    case "SystemBaseHighColor":
                        return new Color { A = 0xFF, R = 0xFF, G = 0xFF, B = 0xFF };

                    case "SystemListLowColor":
                        return new Color { A = 0x19, R = 0xFF, G = 0xFF, B = 0xFF };

                    case "SystemListMediumColor":
                        return new Color { A = 0x33, R = 0xFF, G = 0xFF, B = 0xFF };

                    default:
                        return null;
                }
            }
        }
    }
}