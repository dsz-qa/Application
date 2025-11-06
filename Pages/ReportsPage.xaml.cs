using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;

        public ReportsPage() { InitializeComponent(); }
        public ReportsPage(int userId) : this() => _userId = userId;
    }
}
