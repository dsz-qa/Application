using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class GoalsPage : UserControl
    {
        private readonly int _userId;

        public GoalsPage() { InitializeComponent(); }
        public GoalsPage(int userId) : this() => _userId = userId;
    }
}
