using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace RSTGameTranslation
{
    /// <summary>
    /// First-run welcome screen. Only asks for what a new user must decide (the language pair);
    /// everything else runs on defaults and stays configurable in Settings.
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        // Common game → reader language pairs offered as one-click presets
        private static readonly (string Source, string Target)[] CommonPairs =
        {
            ("ja", "en"), ("ja", "vi"), ("ko", "en"), ("ch_sim", "en"), ("ch_sim", "vi"), ("en", "vi")
        };

        private bool _isLoading = true;

        /// <summary>
        /// True when the user closed the window with "Open Settings" and the caller should open the Settings window.
        /// </summary>
        public bool OpenSettingsRequested { get; private set; }

        public WelcomeWindow()
        {
            InitializeComponent();

            // Language lists are copied from the main window so this screen offers exactly the codes OCR/translation use
            LoadInterfaceLanguages();
            PopulateLanguageComboBox(SourceLanguageComboBox, MainWindow.Instance.sourceLanguageComboBox, ConfigManager.Instance.GetSourceLanguage());
            PopulateLanguageComboBox(TargetLanguageComboBox, MainWindow.Instance.targetLanguageComboBox, ConfigManager.Instance.GetTargetLanguage());
            BuildCommonPairButtons();
            UpdateFormattedTexts();

            LocalizationManager.Instance.PropertyChanged += LocalizationManager_PropertyChanged;
            Closed += (s, e) =>
            {
                LocalizationManager.Instance.PropertyChanged -= LocalizationManager_PropertyChanged;
                // Shown once automatically; it stays reachable from the Quick Start button
                ConfigManager.Instance.SetNeedShowQuickStart(false);
            };

            _isLoading = false;
        }

        private void LoadInterfaceLanguages()
        {
            string currentLanguage = ConfigManager.Instance.GetLanguageInterface();
            foreach (ComboBoxItem mainItem in MainWindow.Instance.languageSelector.Items)
            {
                var item = new ComboBoxItem { Content = mainItem.Content, Tag = mainItem.Tag };
                InterfaceLanguageComboBox.Items.Add(item);
                if (string.Equals(mainItem.Tag as string, currentLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    InterfaceLanguageComboBox.SelectedItem = item;
                }
            }
        }

        private static void PopulateLanguageComboBox(ComboBox target, ComboBox source, string selectedCode)
        {
            var codes = source.Items.OfType<ComboBoxItem>()
                .Select(item => item.Content?.ToString())
                .Where(code => !string.IsNullOrEmpty(code))
                .Select(code => code!);

            foreach (var (code, name) in codes.Select(code => (code, GetLanguageDisplayName(code))).OrderBy(x => x.Item2))
            {
                var item = new ComboBoxItem { Content = name, Tag = code };
                target.Items.Add(item);
                if (string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    target.SelectedItem = item;
                }
            }
        }

        private void BuildCommonPairButtons()
        {
            foreach (var (source, target) in CommonPairs)
            {
                // Skip presets whose languages are not offered (keeps this list valid if the main lists change)
                if (FindItem(SourceLanguageComboBox, source) == null || FindItem(TargetLanguageComboBox, target) == null)
                    continue;

                var button = new Button
                {
                    Content = $"{GetLanguageShortName(source)} → {GetLanguageShortName(target)}",
                    Tag = (source, target),
                    Style = (Style)FindResource("PairButton")
                };
                button.Click += CommonPairButton_Click;
                CommonPairsPanel.Children.Add(button);
            }
        }

        private void CommonPairButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: ValueTuple<string, string> pair })
            {
                SourceLanguageComboBox.SelectedItem = FindItem(SourceLanguageComboBox, pair.Item1);
                TargetLanguageComboBox.SelectedItem = FindItem(TargetLanguageComboBox, pair.Item2);
            }
        }

        private static ComboBoxItem? FindItem(ComboBox comboBox, string code) =>
            comboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Readable name for an app language code, e.g. "Japanese — 日本語 (ja)".
        /// </summary>
        private static string GetLanguageDisplayName(string code)
        {
            CultureInfo? culture = GetCulture(code);
            if (culture == null)
                return code;

            return culture.NativeName == culture.EnglishName
                ? $"{culture.EnglishName} ({code})"
                : $"{culture.EnglishName} — {culture.NativeName} ({code})";
        }

        private static string GetLanguageShortName(string code) => GetCulture(code)?.EnglishName ?? code;

        private static CultureInfo? GetCulture(string code)
        {
            // The app uses its own codes for Chinese
            string cultureName = code.ToLowerInvariant() switch
            {
                "ch_sim" => "zh-Hans",
                "ch_tra" => "zh-Hant",
                _ => code
            };

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
                // Show "Bulgarian" rather than "Bulgarian (Bulgaria)" for region-specific codes like bg-BG
                if (!culture.IsNeutralCulture && !Equals(culture.Parent, CultureInfo.InvariantCulture))
                {
                    culture = culture.Parent;
                }
                return culture;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        private void LocalizationManager_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateFormattedTexts();
        }

        /// <summary>
        /// Texts that embed other values (current services, button labels) cannot use plain bindings.
        /// </summary>
        private void UpdateFormattedTexts()
        {
            var strings = LocalizationManager.Instance.Strings;

            ReadyText.Text = string.Format(strings["Welcome_ReadyDesc"],
                ConfigManager.Instance.GetOcrMethod(),
                ConfigManager.Instance.GetCurrentTranslationService(),
                strings["Btn_Settings"]);

            NextStepsText.Text = string.Format(strings["Welcome_NextDesc"],
                strings["Btn_SelectArea"], strings["Btn_SelectWindow"], strings["Btn_Start"]);

            ReopenHintText.Text = string.Format(strings["Welcome_ReopenHint"], strings["Lbl_QuickStart"]);
        }

        private void InterfaceLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || InterfaceLanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string languageCode })
                return;

            // Select it on the main window so its handler applies and saves the change exactly as usual
            var mainItem = MainWindow.Instance.languageSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag as string == languageCode);
            if (mainItem != null)
            {
                MainWindow.Instance.languageSelector.SelectedItem = mainItem;
            }
        }

        private void SaveLanguages()
        {
            if (SourceLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string source })
            {
                ConfigManager.Instance.SetSourceLanguage(source);
            }
            if (TargetLanguageComboBox.SelectedItem is ComboBoxItem { Tag: string target })
            {
                ConfigManager.Instance.SetTargetLanguage(target);
            }
        }

        private void GetStartedButton_Click(object sender, RoutedEventArgs e)
        {
            SaveLanguages();
            DialogResult = true;
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveLanguages();
            OpenSettingsRequested = true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}
