using System;
using System.Windows;
using System.Windows.Input;
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
        
        // Set up application-wide keyboard handling
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        
        // We'll hook keyboard events in the main window and other windows instead
        // of at the application level (which isn't supported in this context)
        
        // Show splash screen first
        SplashManager.Instance.ShowSplash();
        
        // Initialize ChatBoxWindow instance without showing it
        // This ensures ChatBoxWindow.Instance is available immediately
        new ChatBoxWindow();
        
        // Create main window but don't show it yet
        _mainWindow = new MainWindow();
        
        // We'll attach the keyboard handlers when the windows are loaded
        // Each window now has its own Application_KeyDown method attached to PreviewKeyDown
        
        // Add event handler to show main window after splash closes
        SplashManager.Instance.SplashClosed += (sender, args) =>
        {
            _mainWindow?.Show();
            
            // Attach key handler to other windows once main window is shown
            AttachKeyHandlersToAllWindows();
        };
    }
    
    // Ensure all windows are initialized and loaded
    private void AttachKeyHandlersToAllWindows()
    {
        // Each window now automatically attaches its own keyboard handler
        // when it's loaded, using PreviewKeyDown and its own Application_KeyDown method.
        // We don't need to do anything here anymore.
    }
    
    // Handle application-level keyboard events
    private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // No need to check window focus since this is only called when a window has focus
        KeyboardShortcuts.HandleKeyDown(e);
    }
    
    // Handle any unhandled exceptions to prevent app crashes
    private void App_DispatcherUnhandledException(object sender, 
                                               System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception
        System.Console.WriteLine($"Unhandled application exception: {e.Exception.Message}");
        System.Console.WriteLine($"Stack trace: {e.Exception.StackTrace}");
        
        // Mark as handled to prevent app from crashing
        e.Handled = true;
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}