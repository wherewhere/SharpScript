using Windows.UI.Xaml;
using WinUIEditor;

namespace SharpScript.Helpers
{
    public static class CodeEditorHelper
    {
        #region Text

        /// <summary>
        /// Identifies the Text dependency property.
        /// </summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(CodeEditorHelper),
                new PropertyMetadata(null, OnTextChanged));

        public static string GetText(CodeEditorControl control)
        {
            return (string)control.GetValue(TextProperty);
        }

        public static void SetText(CodeEditorControl control, string value)
        {
            control.SetValue(TextProperty, value);
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeEditorControl control)
            {
                control.Editor.SetText(e.NewValue?.ToString());
            }
        }

        #endregion

        #region Readonly

        /// <summary>
        /// Identifies the Readonly dependency property.
        /// </summary>
        public static readonly DependencyProperty ReadonlyProperty =
            DependencyProperty.RegisterAttached(
                "Readonly",
                typeof(bool),
                typeof(CodeEditorHelper),
                new PropertyMetadata(null, OnReadonlyChanged));

        public static bool GetReadonly(CodeEditorControl control)
        {
            return (bool)control.GetValue(ReadonlyProperty);
        }

        public static void SetReadonly(CodeEditorControl control, bool value)
        {
            control.SetValue(ReadonlyProperty, value);
        }

        private static void OnReadonlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeEditorControl control)
            {
                control.Editor.ReadOnly = e.NewValue is true;
            }
        }

        #endregion
    }
}
