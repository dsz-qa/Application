using System.Windows.Controls;
using Finly.ViewModels;
using Finly.Services;
using System;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private TransactionsViewModel _vm;
        public TransactionsPage()
        {
            InitializeComponent();
            _vm = new TransactionsViewModel();
            this.DataContext = _vm;
            Loaded += TransactionsPage_Loaded;
        }

        private void TransactionsPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            int uid = UserService.GetCurrentUserId();
            _vm.Initialize(uid);
        }
    }
}











