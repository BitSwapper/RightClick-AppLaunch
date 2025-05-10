// File: UI/InputDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RightClickAppLauncher.UI
{
    public partial class InputDialog : Window
    {
        public string ResponseText => ResponseTextBox.Text;

        public InputDialog(string prompt, string defaultResponse = "")
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;
            ResponseTextBox.Text = defaultResponse;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; // IsCancel=True in XAML handles this, but good for clarity
        }
        
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            ResponseTextBox.SelectAll();
            ResponseTextBox.Focus();
        }

        // Handle Enter key in TextBox to submit, and Escape key for the window
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            ResponseTextBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    // Check if default button is available and simulate its click
                    //if (IsDefault && OkButton_Click != null) // OkButton_Click is just a method name here
                    //{
                    //     OkButton_Click(sender, new RoutedEventArgs(Button.ClickEvent, this));
                    //}
                    System.Windows.Forms.MessageBox.Show("WTF");
                    args.Handled = true;
                }
            };
        }
        
        // Handle Escape key at window level to close as Cancel
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
            }
        }
    }
}