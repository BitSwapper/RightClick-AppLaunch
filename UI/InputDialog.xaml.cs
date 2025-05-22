using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RightClickAppLauncher.UI;

public partial class InputDialog : Window
{
    public string ResponseText => ResponseTextBox.Text;

    public InputDialog(string prompt, string defaultValue = "")
    {
        InitializeComponent();
        PromptTextBlock.Text = prompt;
        ResponseTextBox.Text = defaultValue;
    }

    void Window_SourceInitialized(object sender, System.EventArgs e)
    {
    }

    void Window_ContentRendered(object sender, System.EventArgs e)
    {
        ResponseTextBox.SelectAll();
        ResponseTextBox.Focus();
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if(e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}