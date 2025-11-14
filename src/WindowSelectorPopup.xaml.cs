using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RSTGameTranslation
{
    public partial class WindowSelectorPopup : Window
    {
        public delegate void WindowSelectedHandler(IntPtr windowHandle, string windowTitle);
        public event WindowSelectedHandler? WindowSelected;

        private List<WindowInfo> windows = new List<WindowInfo>();

        public WindowSelectorPopup()
        {
            InitializeComponent();

            this.Title = "Select Window to Capture";
            this.Width = 500;
            this.Height = 400;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.ResizeMode = ResizeMode.CanResize;
            
            LoadWindowList();
        }

        private void LoadWindowList()
        {
          
            windows.Clear();
            windowListView.Items.Clear();

           
            EnumWindows(EnumWindowsProcCallback, IntPtr.Zero);

         
            foreach (var window in windows)
            {
                var item = new System.Windows.Controls.ListViewItem
                {
                    Content = window.Title,
                    Tag = window
                };

              
                if (window.Icon != null)
                {
                    var stackPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    var image = new System.Windows.Controls.Image
                    {
                        Source = window.Icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    var textBlock = new TextBlock { Text = window.Title };
                    
                    stackPanel.Children.Add(image);
                    stackPanel.Children.Add(textBlock);
                    
                    item.Content = stackPanel;
                }

                windowListView.Items.Add(item);
            }
        }

        private bool EnumWindowsProcCallback(IntPtr hWnd, IntPtr lParam)
        {
            
            if (!IsWindowVisible(hWnd))
                return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            StringBuilder title = new StringBuilder(length + 1);
            GetWindowText(hWnd, title, title.Capacity);

           
            if (string.IsNullOrEmpty(title.ToString()) || hWnd == new WindowInteropHelper(this).Handle)
                return true;

           
            ImageSource? icon = ExtractIcon(hWnd);

            
            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title.ToString(),
                Icon = icon
            });

            return true;
        }

        private ImageSource? ExtractIcon(IntPtr hwnd)
        {
            try
            {
                
                GetWindowThreadProcessId(hwnd, out uint processId);
                
                if (processId == 0)
                    return null;

               
                try
                {
                   
                    IntPtr hIcon = SendMessage(hwnd, WM_GETICON, ICON_SMALL2, 0);
                    if (hIcon == IntPtr.Zero)
                        hIcon = SendMessage(hwnd, WM_GETICON, ICON_SMALL, 0);
                    if (hIcon == IntPtr.Zero)
                        hIcon = SendMessage(hwnd, WM_GETICON, ICON_BIG, 0);
                    if (hIcon == IntPtr.Zero)
                        hIcon = GetClassLongPtr(hwnd, GCL_HICON);
                    if (hIcon == IntPtr.Zero)
                        hIcon = GetClassLongPtr(hwnd, GCL_HICONSM);

                    if (hIcon != IntPtr.Zero)
                    {
                        
                        return Imaging.CreateBitmapSourceFromHIcon(
                            hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                }
                catch
                {
                    
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

       
        private const int WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const int GCL_HICON = -14;
        private const int GCL_HICONSM = -34;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadWindowList();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectWindow();
        }

        private void windowListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectWindow();
        }

        private void SelectWindow()
        {
            if (windowListView.SelectedItem is System.Windows.Controls.ListViewItem item && item.Tag is WindowInfo window)
            {
                if (IsWindow(window.Handle))
                {
                    WindowSelected?.Invoke(window.Handle, window.Title);
                    this.Close();
                }
                else
                {
                    System.Windows.MessageBox.Show("The selected window is no longer available. Please refresh the list.",
                        "Window Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadWindowList();
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    
        private class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = string.Empty;
            public ImageSource? Icon { get; set; }
        }

        // P/Invoke
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);
    }
}