using System.Windows;
using Application = System.Windows.Application;

namespace UGTLive;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private MainWindow? _mainWindow;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Show splash screen first
        SplashManager.Instance.ShowSplash();
        
        // Initialize ChatBoxWindow instance without showing it
        // This ensures ChatBoxWindow.Instance is available immediately
        new ChatBoxWindow();
        
        // Create main window but don't show it yet
        _mainWindow = new MainWindow();
        
        // Add event handler to show main window after splash closes
        SplashManager.Instance.SplashClosed += (sender, args) =>
        {
            _mainWindow?.Show();
        };
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}