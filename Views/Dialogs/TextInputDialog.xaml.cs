using System.Windows;

namespace Finly.Views.Dialogs
{
    public partial class TextInputDialog : Window
    {
        public string Value { get; private set; } = string.Empty;

        public TextInputDialog(string title, string label, string? initial = null)
        {
            InitializeComponent();
            Title = title ?? "Finly";
            HeaderText.Text = label ?? string.Empty;
            InputBox.Text = initial ?? string.Empty;

            Loaded += (_, __) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Value = InputBox.Text ?? string.Empty;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
