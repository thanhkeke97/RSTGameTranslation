using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Windows;

namespace RSTGameTranslation
{
    public class LocalizationManager : INotifyPropertyChanged
    {
        // Singleton instance
        private static LocalizationManager _instance;
        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        private Dictionary<string, string> _strings;
        
        private Dictionary<string, string> _defaultStrings;

        private string _currentLanguage = "en";

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LoadLanguage(_currentLanguage);
                    OnPropertyChanged();
                    // Notify that all properties "Strings" have changed
                    OnPropertyChanged(nameof(Strings)); 
                }
            }
        }

        public string this[string key]
        {
            get
            {
                if (_strings != null && _strings.TryGetValue(key, out string value))
                {
                    return value;
                }
                
                // Fallback to English if not found in the current language
                if (_defaultStrings != null && _defaultStrings.TryGetValue(key, out string defaultValue))
                {
                    return defaultValue;
                }

                return $"[{key}]"; // Return key if not found in any language
            }
        }
        
        public LocalizationManager Strings => this;

        private LocalizationManager()
        {
            _strings = new Dictionary<string, string>();
            _defaultStrings = new Dictionary<string, string>();
            
            LoadDefaultLanguage();
            LoadLanguage("en"); 
        }

        private void LoadDefaultLanguage()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", "en.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _defaultStrings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading default language: {ex.Message}");
            }
        }

        public void LoadLanguage(string langCode)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"{langCode}.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var newStrings = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    
                    // Merge with default strings to ensure no missing keys
                    if (_defaultStrings != null)
                    {
                        foreach (var kvp in _defaultStrings)
                        {
                            if (!newStrings.ContainsKey(kvp.Key))
                            {
                                newStrings[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    
                    _strings = newStrings;
                }
                else
                {
                    // If language file is not found, use default
                    _strings = new Dictionary<string, string>(_defaultStrings);
                }
                
                // Update UI
                OnPropertyChanged(nameof(Strings));
                // Call PropertyChanged for null or empty string to refresh all bindings
                OnPropertyChanged(string.Empty); 
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading language {langCode}: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}