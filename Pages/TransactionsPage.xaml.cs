using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private readonly int _userId;

        public TransactionsPage()
        {
            InitializeComponent();
        }

        public TransactionsPage(int userId) : this()
        {
            _userId = userId;
        }
    }
}
