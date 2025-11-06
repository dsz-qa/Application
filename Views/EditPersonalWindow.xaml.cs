using System.Windows;
using Finly.ViewModels;

namespace Finly.Views
{
    public partial class EditPersonalWindow : Window
    {
        private readonly EditPersonalViewModel _vm;

        public EditPersonalWindow(int userId)
        {
            InitializeComponent();
            _vm = new EditPersonalViewModel(userId);
            DataContext = _vm;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // zapis do DB
            if (_vm.Save())
            {
                // dialog OK -> zamknij, a rodzic odświeży VM
                DialogResult = true;  // ważne: powoduje zwrot true w ShowDialog()
                Close();
            }
            else
            {
                // ewentualnie pokaż komunikat o błędzie
                MessageBox.Show("Uzupełnij poprawnie wymagane pola.", "Błąd", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
