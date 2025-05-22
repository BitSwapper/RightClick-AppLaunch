using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs; // For KeyEventArgs and Keyboard

namespace RightClickAppLauncher.UI
{
    public partial class InputDialog : Window
    {
        public string ResponseText => ResponseTextBox.Text;

        public InputDialog(string prompt, string defaultValue = "")
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;
            ResponseTextBox.Text = defaultValue;
        }

        private void Window_SourceInitialized(object sender, System.EventArgs e)
        {
            // Optional: Remove minimize/maximize buttons if WindowStyle allows them
        }

        private void Window_ContentRendered(object sender, System.EventArgs e)
        {
            ResponseTextBox.SelectAll();
            ResponseTextBox.Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            // ResponseText is accessed by the caller after ShowDialog returns true
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}