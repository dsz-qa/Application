using System.Windows;
using System.Windows.Input;

namespace Finly.Views.Dialogs
{
    public partial class ConfirmDialog : Window
    {
        public bool Result { get; private set; }

        public ConfirmDialog(string message)
        {
            InitializeComponent();
            MessageText.Text = message ?? string.Empty;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;   // pozwala użyć: if (dlg.ShowDialog() == true) ...
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) Cancel_Click(this, new RoutedEventArgs());
            if (e.Key == Key.Enter) Ok_Click(this, new RoutedEventArgs());
        }
    }
}

