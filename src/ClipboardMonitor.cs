using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace RST
{
    /// <summary>
    /// Monitors clipboard for text changes and raises an event when text is copied.
    /// Uses Win32 AddClipboardFormatListener for efficient monitoring.
    /// </summary>
    public class ClipboardMonitor : IDisposable
    {
        #region Win32 Interop
        
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        
        #endregion

        #region Singleton
        
        private static ClipboardMonitor? _instance;
        private static readonly object _lock = new object();
        
        public static ClipboardMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ClipboardMonitor();
                    }
                }
                return _instance;
            }
        }
        
        #endregion

        #region Events
        
        /// <summary>
        /// Raised when clipboard text changes (with debouncing applied).
        /// </summary>
        public event EventHandler<ClipboardTextChangedEventArgs>? ClipboardTextChanged;
        
        #endregion

        #region Fields
        
        private IntPtr _hwnd = IntPtr.Zero;
        private HwndSource? _hwndSource;
        private bool _isListening;
        private bool _disposed;
        
        // Debounce support
        private DispatcherTimer? _debounceTimer;
        private string? _pendingText;
        private int _debounceMs = 300;
        
        // Track programmatic clipboard writes to avoid re-triggering
        private bool _isProgrammaticWrite;
        private string? _lastProcessedText;
        
        #endregion

        #region Properties
        
        /// <summary>
        /// Gets or sets debounce time in milliseconds. Default is 300ms.
        /// </summary>
        public int DebounceMs
        {
            get => _debounceMs;
            set => _debounceMs = Math.Clamp(value, 100, 2000);
        }
        
        /// <summary>
        /// Gets or sets maximum characters to process. Default is 5000.
        /// </summary>
        public int MaxCharacters { get; set; } = 5000;
        
        /// <summary>
        /// Returns true if monitoring is active.
        /// </summary>
        public bool IsListening => _isListening;
        
        #endregion

        #region Constructor
        
        private ClipboardMonitor()
        {
            // Private constructor for singleton
        }
        
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Starts listening for clipboard changes. Must be called from UI thread.
        /// </summary>
        /// <param name="window">The WPF window to use for Win32 message handling.</param>
        public void StartListening(Window window)
        {
            if (_isListening) return;
            
            try
            {
                var helper = new WindowInteropHelper(window);
                _hwnd = helper.Handle;
                
                if (_hwnd == IntPtr.Zero)
                {
                    // Window not yet loaded, wait for it
                    window.Loaded += (s, e) => StartListening(window);
                    return;
                }
                
                _hwndSource = HwndSource.FromHwnd(_hwnd);
                _hwndSource?.AddHook(WndProc);
                
                if (AddClipboardFormatListener(_hwnd))
                {
                    _isListening = true;
                    Console.WriteLine("[ClipboardMonitor] Started listening for clipboard changes.");
                }
                else
                {
                    Console.WriteLine("[ClipboardMonitor] Failed to add clipboard listener.");
                }
                
                // Initialize debounce timer
                _debounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(_debounceMs)
                };
                _debounceTimer.Tick += OnDebounceTimerTick;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardMonitor] Error starting: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stops listening for clipboard changes.
        /// </summary>
        public void StopListening()
        {
            if (!_isListening) return;
            
            try
            {
                _debounceTimer?.Stop();
                
                if (_hwnd != IntPtr.Zero)
                {
                    RemoveClipboardFormatListener(_hwnd);
                }
                
                _hwndSource?.RemoveHook(WndProc);
                _hwndSource = null;
                _hwnd = IntPtr.Zero;
                _isListening = false;
                
                Console.WriteLine("[ClipboardMonitor] Stopped listening.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardMonitor] Error stopping: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Sets the clipboard text programmatically without triggering the ClipboardTextChanged event.
        /// </summary>
        /// <param name="text">Text to set on clipboard.</param>
        public void SetClipboardTextSilently(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            try
            {
                _isProgrammaticWrite = true;
                _lastProcessedText = text;
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Clipboard.SetText(text);
                });
                
                Console.WriteLine("[ClipboardMonitor] Set clipboard text silently.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardMonitor] Error setting clipboard: {ex.Message}");
            }
            finally
            {
                // Reset flag after a short delay to handle any delayed WM_CLIPBOARDUPDATE
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isProgrammaticWrite = false;
                }), DispatcherPriority.Background);
            }
        }
        
        #endregion

        #region Private Methods
        
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardUpdate();
                handled = true;
            }
            return IntPtr.Zero;
        }
        
        private void OnClipboardUpdate()
        {
            // Skip if this is our own programmatic write
            if (_isProgrammaticWrite)
            {
                Console.WriteLine("[ClipboardMonitor] Skipping programmatic write.");
                return;
            }
            
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) return;
                
                string text = System.Windows.Clipboard.GetText();
                
                if (string.IsNullOrWhiteSpace(text)) return;
                
                // Skip if same as last processed text (duplicate detection)
                if (text == _lastProcessedText)
                {
                    Console.WriteLine("[ClipboardMonitor] Skipping duplicate text.");
                    return;
                }
                
                // Store pending text and restart debounce timer
                _pendingText = text;
                _debounceTimer?.Stop();
                _debounceTimer!.Interval = TimeSpan.FromMilliseconds(_debounceMs);
                _debounceTimer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClipboardMonitor] Error reading clipboard: {ex.Message}");
            }
        }
        
        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer?.Stop();
            
            if (string.IsNullOrWhiteSpace(_pendingText)) return;
            
            string textToProcess = _pendingText;
            _pendingText = null;
            
            // Update last processed to avoid re-triggering
            _lastProcessedText = textToProcess;
            
            // Raise event with the text
            var args = new ClipboardTextChangedEventArgs(textToProcess);
            ClipboardTextChanged?.Invoke(this, args);
        }
        
        #endregion

        #region IDisposable
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                StopListening();
                _debounceTimer?.Stop();
            }
            
            _disposed = true;
        }
        
        ~ClipboardMonitor()
        {
            Dispose(false);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Event args for clipboard text changed event.
    /// </summary>
    public class ClipboardTextChangedEventArgs : EventArgs
    {
        public string Text { get; }
        public int Length => Text?.Length ?? 0;
        
        public ClipboardTextChangedEventArgs(string text)
        {
            Text = text;
        }
    }
}
