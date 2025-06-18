using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RSTGameTranslation
{
    public partial class ApiKeysWindow : Window
    {
        private ObservableCollection<string> _apiKeys = new ObservableCollection<string>();
        private string _serviceType;
        
        public ApiKeysWindow(string serviceType, List<string> apiKeys)
        {
            InitializeComponent();
            
            _serviceType = serviceType;
            Title = $"{_serviceType} API Keys";
            
            // Populate the list with existing keys
            foreach (var key in apiKeys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    // Mask API keys for display
                    string maskedKey = MaskApiKey(key);
                    _apiKeys.Add(maskedKey);
                }
            }
            
            // Set the ListView's ItemsSource
            apiKeysListView.ItemsSource = _apiKeys;
        }
        
        // Mask API key for display (show only first 4 and last 4 characters)
        private string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length <= 8)
            {
                return apiKey;
            }
            
            return apiKey.Substring(0, 4) + "..." + apiKey.Substring(apiKey.Length - 4);
        }
        
        // Clear selected API key
        private void ClearSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (apiKeysListView.SelectedItem != null)
            {
                int selectedIndex = apiKeysListView.SelectedIndex;
                
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove this API key?", 
                    "Confirm Removal", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Remove the key from the list
                    _apiKeys.RemoveAt(selectedIndex);
                    
                    // Save the updated list
                    List<string> actualKeys = ConfigManager.Instance.GetApiKeysList(_serviceType);
                    if (selectedIndex < actualKeys.Count)
                    {
                        actualKeys.RemoveAt(selectedIndex);
                        ConfigManager.Instance.SaveApiKeysList(_serviceType, actualKeys);
                    }
                }
            }
        }
        
        // Clear all API keys
        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiKeys.Count > 0)
            {
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    $"Are you sure you want to remove ALL API keys for {_serviceType}?", 
                    "Confirm Removal", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Clear the list
                    _apiKeys.Clear();
                    
                    // Save the empty list
                    ConfigManager.Instance.SaveApiKeysList(_serviceType, new List<string>());
                }
            }
        }
        
        // Close button
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}