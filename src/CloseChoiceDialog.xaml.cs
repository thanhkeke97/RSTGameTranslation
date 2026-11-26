using System.Windows;

namespace RSTGameTranslation
{
    public enum CloseButtonAction
    {
        Ask,
        MinimizeToTray,
        Exit,
    }

    /// <summary>
    /// Asked every time the main window's close button is pressed (unless Settings fixes the choice):
    /// exit RST, or keep it running in the tray.
    /// </summary>
    public partial class CloseChoiceDialog : Window
    {
        private CloseButtonAction _choice = CloseButtonAction.Ask; // Ask = cancelled

        public CloseChoiceDialog()
        {
            InitializeComponent();
        }

        /// <summary>Returns MinimizeToTray, Exit, or Ask when the dialog was cancelled.</summary>
        public static CloseButtonAction Ask(Window owner)
        {
            var dialog = new CloseChoiceDialog();
            // A hidden or minimized owner would leave the dialog without a place on screen
            if (owner.IsVisible && owner.WindowState != WindowState.Minimized)
                dialog.Owner = owner;
            else
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dialog.ShowDialog();
            return dialog._choice;
        }

        private void TrayButton_Click(object sender, RoutedEventArgs e)
        {
            _choice = CloseButtonAction.MinimizeToTray;
            DialogResult = true;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            _choice = CloseButtonAction.Exit;
            DialogResult = true;
        }
    }
}
