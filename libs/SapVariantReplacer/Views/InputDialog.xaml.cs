using System.Windows;

namespace SapVariantReplacer.Views;

public partial class InputDialog : Window
{
    public string ResponseText => ResponseBox.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ResponseBox.Text = defaultValue;
        ResponseBox.SelectAll();
        Loaded += (_, _) => ResponseBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
