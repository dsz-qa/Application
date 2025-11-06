using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class BudgetsPage : UserControl
    {
        private readonly int _userId;

        public BudgetsPage()
        {
            InitializeComponent();
        }

        public BudgetsPage(int userId) : this()
        {
            _userId = userId;
        }
    }
}


