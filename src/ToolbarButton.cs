using System.Windows;

namespace RSTGameTranslation
{
    public enum ToolbarState
    {
        Off,   // feature off / window hidden / nothing selected
        On,    // feature on / window shown / area or window selected
        Busy   // waiting for the user (an area or position is being selected)
    }

    /// <summary>
    /// State of a main-window toolbar button. The ToolbarToggleButton style draws it (dot, border and
    /// tint from the theme), so code only says what the state is instead of picking colors.
    /// </summary>
    public static class ToolbarButton
    {
        public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(ToolbarState), typeof(ToolbarButton),
            new FrameworkPropertyMetadata(ToolbarState.Off));

        public static ToolbarState GetState(DependencyObject element) => (ToolbarState)element.GetValue(StateProperty);

        public static void SetState(DependencyObject element, ToolbarState value) => element.SetValue(StateProperty, value);

        public static void Set(DependencyObject element, bool on) => SetState(element, on ? ToolbarState.On : ToolbarState.Off);
    }
}
