using System.Windows.Controls;
using Finly.ViewModels;
using Finly.Services;
using System;
using Finly.Models;
using Finly.Views.Controls;
using System.Windows;

namespace Finly.Pages
{
    public partial class TransactionsPage : UserControl
    {
        private TransactionsViewModel _vm;
        private PeriodBarControl? _periodBar;
        public TransactionsPage()
        {
            InitializeComponent();
            _vm = new TransactionsViewModel();
            this.DataContext = _vm;
            Loaded += TransactionsPage_Loaded;
        }

        private void TransactionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            int uid = UserService.GetCurrentUserId();
            _vm.Initialize(uid);
            // odszukaj kontrolkę po nazwie nadanej w XAML
            _periodBar = this.FindName("PeriodBar") as PeriodBarControl;
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
                _periodBar.RangeChanged += PeriodBar_RangeChanged;
            }
        }

        private void PeriodBar_RangeChanged(object? sender, EventArgs e)
        {
            if (_periodBar != null)
            {
                _vm.SetPeriod(_periodBar.Mode, _periodBar.StartDate, _periodBar.EndDate);
            }
        }
    }
}











