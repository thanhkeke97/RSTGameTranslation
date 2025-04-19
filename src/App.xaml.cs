using System.Windows;
using Application = System.Windows.Application;

namespace WPFScreenCapture;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Initialize ChatBoxWindow instance without showing it
        // This ensures ChatBoxWindow.Instance is available immediately
        new ChatBoxWindow();
        
        // Create and show the main window
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}