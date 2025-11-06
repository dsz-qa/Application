using System.Windows.Controls;

namespace Finly.Pages
{
    public partial class SubscriptionsPage : UserControl
    {
        private readonly int _userId;

        public SubscriptionsPage()
        {
            InitializeComponent();
        }

        public SubscriptionsPage(int userId) : this()
        {
            _userId = userId;
        }
    }
}
