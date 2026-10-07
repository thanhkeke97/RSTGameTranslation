using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace RSTGameTranslation
{
    /// <summary>
    /// First-run walkthrough: points at "Select Area" and then "Start" with popups anchored to the real buttons,
    /// advancing as the user actually performs each step.
    /// </summary>
    public sealed class CoachMarkTour
    {
        private const string TourTipId = "coach_mark_tour";

        private enum Step { SelectArea, Start }

        private readonly MainWindow _window;
        private readonly Popup _popup;
        private readonly TextBlock _message;
        private Step _step;

        /// <summary>
        /// The running tour, or null when none is active.
        /// </summary>
        public static CoachMarkTour? Current { get; private set; }

        private CoachMarkTour(MainWindow window)
        {
            _window = window;
            _message = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var gotItButton = new Button
            {
                Content = LocalizationManager.Instance.Strings["Coach_GotIt"],
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(10, 3, 10, 3),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            gotItButton.Click += (s, e) => Finish();

            var panel = new StackPanel();
            panel.Children.Add(_message);
            panel.Children.Add(gotItButton);

            _popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 6,
                Child = new Border
                {
                    Background = (Brush)window.FindResource("AccentBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12),
                    MaxWidth = 300,
                    Child = panel
                }
            };

            // A popup stays where it was opened, so follow the main window when it moves
            _window.LocationChanged += Window_LocationChanged;
            // Popups are topmost; hide while the user is in the game or the area selector
            _window.Deactivated += Window_Deactivated;
            _window.Activated += Window_Activated;
        }

        /// <summary>
        /// Start the tour unless the user has already completed or dismissed it.
        /// </summary>
        public static void StartIfNeeded(MainWindow window)
        {
            if (Current != null || ConfigManager.Instance.IsOnboardingTipShown(TourTipId))
                return;

            Current = new CoachMarkTour(window);
            Current.ShowStep(Step.SelectArea);
        }

        /// <summary>
        /// Call when a translation area has been selected (selecting the game window alone does not count).
        /// </summary>
        public void OnAreaChosen()
        {
            if (_step == Step.SelectArea)
            {
                ShowStep(Step.Start);
            }
        }

        /// <summary>
        /// Call when translation is started.
        /// </summary>
        public void OnTranslationStarted()
        {
            Finish();
        }

        private void ShowStep(Step step)
        {
            _step = step;
            var strings = LocalizationManager.Instance.Strings;

            if (step == Step.SelectArea)
            {
                _message.Text = string.Format(strings["Coach_SelectArea"], strings["Btn_SelectArea"], strings["Btn_SelectWindow"]);
                _popup.PlacementTarget = _window.selectAreaButton;
            }
            else
            {
                _message.Text = string.Format(strings["Coach_Start"], strings["Btn_Start"]);
                _popup.PlacementTarget = _window.toggleButton;
            }

            _popup.IsOpen = false;
            _popup.IsOpen = true;
        }

        private void Window_LocationChanged(object? sender, EventArgs e)
        {
            // Nudging the offset forces the popup to recompute its position from the placement target
            _popup.HorizontalOffset += 0.01;
            _popup.HorizontalOffset -= 0.01;
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            _popup.IsOpen = false;
        }

        private void Window_Activated(object? sender, EventArgs e)
        {
            _popup.IsOpen = true;
        }

        private void Finish()
        {
            _popup.IsOpen = false;
            _window.LocationChanged -= Window_LocationChanged;
            _window.Deactivated -= Window_Deactivated;
            _window.Activated -= Window_Activated;
            ConfigManager.Instance.MarkOnboardingTipShown(TourTipId);
            Current = null;
        }
    }
}
