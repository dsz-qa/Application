using System.Windows.Controls;
using Finly.ViewModels;

namespace Finly.Pages
{
    public partial class ReportsPage : UserControl
    {
        private readonly int _userId;

        public ReportsPage()
        {
            InitializeComponent();
            // DataContext is set in XAML, but ensure there's a fallback
            if (this.DataContext == null)
                this.DataContext = new ReportsViewModel();
        }

        public ReportsPage(int userId) : this()
        {
            _userId = userId;
            // Optionally pass userId to ViewModel in future
        }
    }
}
