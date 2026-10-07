using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace RSTGameTranslation
{
    public partial class FancyBalloon : System.Windows.Controls.UserControl
    {
        private readonly TaskbarIcon _taskbarIcon;
        private readonly DispatcherTimer _closeTimer;

        public FancyBalloon(string title, string message, TaskbarIcon taskbarIcon, int durationMs)
        {
            InitializeComponent();
            txtTitle.Text = title;
            txtMessage.Text = message;
            _taskbarIcon = taskbarIcon;

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                _taskbarIcon.CloseBalloon();
            };
            _closeTimer.Start();

            // Keep the balloon open while the user is reading it, restart the countdown when the mouse leaves
            MouseEnter += (s, e) => _closeTimer.Stop();
            MouseLeave += (s, e) => _closeTimer.Start();
        }
    }
}
