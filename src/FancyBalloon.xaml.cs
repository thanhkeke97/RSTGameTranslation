using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;

namespace RSTGameTranslation
{
    public partial class FancyBalloon : System.Windows.Controls.UserControl
    {
        private TaskbarIcon _taskbarIcon;

        public FancyBalloon(string title, string message, TaskbarIcon taskbarIcon)
        {
            InitializeComponent();
            txtTitle.Text = title;
            txtMessage.Text = message;
            _taskbarIcon = taskbarIcon;

            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1.5); 
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _taskbarIcon.CloseBalloon(); 
            };
            timer.Start();
        }
    }
}