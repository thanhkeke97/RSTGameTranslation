using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace RSTGameTranslation;

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
    
    /// <summary>
    /// Restarts the application, ensuring it happens on the UI thread
    /// </summary>
    public static void RestartApplication()
    {
        // Make sure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            // If not on UI thread, invoke on UI thread
            Application.Current.Dispatcher.Invoke(RestartApplication);
            return;
        }
        
        try
        {
            // Get current process path
            string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            
            if (string.IsNullOrEmpty(appPath))
            {
                System.Windows.MessageBox.Show("Unable to restart application: Could not determine application path.", 
                    "Restart Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Create process start info
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true
            };
            
            // Start new instance
            Process.Start(startInfo);
            
            // Schedule application shutdown after a brief delay to ensure new process starts
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                // Exit current instance
                Application.Current.Shutdown();
            }));
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to restart application: {ex.Message}", 
                "Restart Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    /// <summary>
    /// Shows a confirmation dialog and restarts the application if confirmed
    /// </summary>
    /// <param name="message">Custom message to show in the confirmation dialog</param>
    /// <returns>True if restart was initiated, false if user cancelled</returns>
    public static bool ConfirmAndRestartApplication(string message = "Application needs to restart to apply changes. Restart now?")
    {
        // Make sure we're on the UI thread
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            // If not on UI thread, invoke on UI thread and return result
            return (bool)Application.Current.Dispatcher.Invoke(
                new Func<string, bool>(ConfirmAndRestartApplication), message);
        }
        
        var result = System.Windows.MessageBox.Show(message, "Restart Application", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
            
        if (result == MessageBoxResult.Yes)
        {
            RestartApplication();
            return true;
        }
        
        return false;
    }
}