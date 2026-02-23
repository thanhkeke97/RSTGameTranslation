using System;
using System.Windows;
using System.Windows.Media;

namespace RSTGameTranslation
{
    public static class ThemeManager
    {
        public static void ApplyTheme(bool isDark)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            if (isDark)
            {
                app.Resources["BgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E");
                app.Resources["SurfaceColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252526");
                app.Resources["Surface2Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D30");
                app.Resources["Surface3Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E3E42");
                app.Resources["AccentColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#007ACC");
                app.Resources["BorderColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3F3F46");
                app.Resources["NeutralBgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333337");
                
                app.Resources["BgBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["BgColor"]);
                app.Resources["SurfaceBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["SurfaceColor"]);
                app.Resources["Surface2Brush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["Surface2Color"]);
                app.Resources["Surface3Brush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["Surface3Color"]);
                app.Resources["NeutralBgBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["NeutralBgColor"]);
                app.Resources["AccentBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["AccentColor"]);
                app.Resources["BorderBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["BorderColor"]);
                app.Resources["TextBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F1F1F1"));
                app.Resources["MutedBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A0A0A0"));

                app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["SurfaceBrush"];
                app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["Surface2Brush"];
                app.Resources[System.Windows.SystemColors.ControlLightBrushKey] = app.Resources["Surface3Brush"];
                app.Resources[System.Windows.SystemColors.ControlDarkBrushKey] = app.Resources["BorderBrush"];
                app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
                app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
            else
            {
                app.Resources["BgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F7FAFC");
                app.Resources["SurfaceColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFF");
                app.Resources["Surface2Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F8FD");
                app.Resources["Surface3Color"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EBF4FB");
                app.Resources["AccentColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0066CC");
                app.Resources["BorderColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E0ECF8");
                app.Resources["NeutralBgColor"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E6EEF3");
                
                app.Resources["BgBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["BgColor"]);
                app.Resources["SurfaceBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["SurfaceColor"]);
                app.Resources["Surface2Brush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["Surface2Color"]);
                app.Resources["Surface3Brush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["Surface3Color"]);
                app.Resources["NeutralBgBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["NeutralBgColor"]);
                app.Resources["AccentBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["AccentColor"]);
                app.Resources["BorderBrush"] = new SolidColorBrush((System.Windows.Media.Color)app.Resources["BorderColor"]);
                app.Resources["TextBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0B2545"));
                app.Resources["MutedBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5B6B7A"));

                app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["SurfaceBrush"];
                app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["Surface2Brush"];
                app.Resources[System.Windows.SystemColors.ControlLightBrushKey] = app.Resources["Surface3Brush"];
                app.Resources[System.Windows.SystemColors.ControlDarkBrushKey] = app.Resources["BorderBrush"];
                app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextBrush"];
                app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
                app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
        }
    }
}
