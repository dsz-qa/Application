using System.Windows;

namespace Finly.Views
{
    public partial class EnvelopesIntroWindow : Window
    {
        public EnvelopesIntroWindow()
        {
            InitializeComponent();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}