using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace RSTGameTranslation
{
    public partial class LogWindow : Window
    {
        private static LogWindow? _instance;
        private StringWriter? _logWriter;
        private TextWriter? _originalOutput;
        private readonly StringBuilder _logBuffer;
        private readonly List<string> _logLines;
        private int _lineCount = 0;
        
        // Maximum number of log lines to keep
        private const int MAX_LOG_LINES = 500;
        // Trim threshold - trim when we exceed this to reduce rebuild frequency
        private const int TRIM_THRESHOLD = MAX_LOG_LINES + 100;
        
        // Singleton pattern
        public static LogWindow Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LogWindow();
                }
                return _instance;
            }
        }
        
        private LogWindow()
        {
            InitializeComponent();
            _logBuffer = new StringBuilder();
            _logLines = new List<string>(MAX_LOG_LINES + 100);
            
            // Set window position to the right of screen
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.PrimaryScreenWidth - Width - 50;
            Top = 50;
            
            // Handle window closing to hide instead of dispose
            Closing += LogWindow_Closing;
            
            // Initialize log capture
            initializeLogCapture();
        }
        
        // Initialize console output capture
        private void initializeLogCapture()
        {
            // Save original output (this may be a StreamWriter from MainWindow's InitializeConsole)
            _originalOutput = Console.Out;
            
            // Create a custom StringWriter that writes to both the buffer and the TextBox
            _logWriter = new StringWriter(_logBuffer);
            
            // Create a multi-writer that writes to both original console and our buffer
            var multiWriter = new MultiTextWriter(_originalOutput, _logWriter, this);
            Console.SetOut(multiWriter);
            
            // Write initial message
            appendLog("=== Log Viewer Started ===");
            appendLog("All application messages will appear here.");
            appendLog("You can select and copy text freely.");
            appendLog("==========================\n");
        }
        
        // Append log message (thread-safe, non-blocking)
        public void appendLog(string message)
        {
            // Use InvokeAsync instead of Invoke to prevent deadlocks
            // This is fire-and-forget to avoid blocking background threads
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    string lineToAdd = message + Environment.NewLine;
                    _logLines.Add(message);
                    _lineCount++;
                    
                    // Trim old lines if we exceed the threshold (efficient O(k) where k is lines to remove)
                    // Using a threshold reduces rebuild frequency
                    if (_lineCount > TRIM_THRESHOLD)
                    {
                        trimOldLines();
                    }
                    else
                    {
                        // Fast path: just append to TextBox when not trimming
                        logTextBox.AppendText(lineToAdd);
                    }
                    
                    updateLineCount();
                    
                    // Auto-scroll if enabled
                    if (autoScrollCheckBox.IsChecked == true)
                    {
                        logScrollViewer.ScrollToEnd();
                    }
                }
                catch (Exception ex)
                {
                    // Don't log errors here to avoid recursion - just write to original console
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in appendLog: {ex.Message}");
                    }
                    catch
                    {
                        // Ignore if even debug output fails
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
        
        // Trim old log lines to keep within the limit (O(k) where k is lines to remove)
        private void trimOldLines()
        {
            int linesToRemove = _lineCount - MAX_LOG_LINES;
            if (linesToRemove > 0)
            {
                // Remove old lines from the front of the list (O(k) operation)
                _logLines.RemoveRange(0, linesToRemove);
                _lineCount = _logLines.Count;
                
                // Rebuild TextBox text efficiently using StringBuilder (O(n) where n is remaining lines)
                // Pre-allocate with estimated capacity (80 chars per line)
                var sb = new StringBuilder(_lineCount * 80);
                for (int i = 0; i < _logLines.Count; i++)
                {
                    sb.Append(_logLines[i]);
                    sb.Append(Environment.NewLine); // Match AppendText behavior
                }
                
                // Set text once (triggers single re-render instead of multiple)
                logTextBox.Text = sb.ToString();
                
                // Also trim the buffer
                _logBuffer.Clear();
                _logBuffer.Append(logTextBox.Text);
            }
        }
        
        // Update line count display
        private void updateLineCount()
        {
            lineCountText.Text = $"Lines: {_lineCount}";
        }
        
        // Clear button click
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            logTextBox.Clear();
            _logBuffer.Clear();
            _logLines.Clear();
            _lineCount = 0;
            updateLineCount();
            statusText.Text = "Log cleared";
        }
        
        // Copy all button click
        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(logTextBox.Text))
                {
                    System.Windows.Clipboard.SetText(logTextBox.Text);
                    statusText.Text = "All log text copied to clipboard";
                }
                else
                {
                    statusText.Text = "No log text to copy";
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error copying to clipboard: {ex.Message}";
            }
        }
        
        // Text changed event for auto-scroll
        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-scroll if enabled
            if (autoScrollCheckBox.IsChecked == true)
            {
                logScrollViewer.ScrollToEnd();
            }
        }
        
        // Handle window closing - hide instead of close
        private void LogWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            
            // Notify main window that log is hidden
            MainWindow.Instance?.updateLogButtonState(false);
        }
        
        // Cleanup when application exits
        public void cleanup()
        {
            // Restore original console output
            if (_originalOutput != null)
            {
                Console.SetOut(_originalOutput);
            }
            
            _logWriter?.Dispose();
        }
    }
    
    // Custom TextWriter that writes to multiple outputs
    public class MultiTextWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly TextWriter _buffer;
        private readonly LogWindow _logWindow;
        
        public override Encoding Encoding => Encoding.UTF8;
        
        public MultiTextWriter(TextWriter original, TextWriter buffer, LogWindow logWindow)
        {
            _original = original;
            _buffer = buffer;
            _logWindow = logWindow;
        }
        
        public override void Write(char value)
        {
            _original.Write(value);
            _buffer.Write(value);
        }
        
        public override void Write(string? value)
        {
            if (value == null) return;
            
            _original.Write(value);
            _buffer.Write(value);
        }
        
        public override void WriteLine(string? value)
        {
            if (value == null) return;
            
            _original.WriteLine(value);
            _buffer.WriteLine(value);
            
            // Update the log window
            _logWindow.appendLog(value);
        }
        
        public override void WriteLine()
        {
            _original.WriteLine();
            _buffer.WriteLine();
            _logWindow.appendLog("");
        }
        
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _original?.Dispose();
                _buffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}


