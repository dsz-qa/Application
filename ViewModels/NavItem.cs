using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace Finly.ViewModels
{
    /// <summary>Pozycja w nawigacji powłoki.</summary>
    public class NavItem : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Title { get; }
        public Func<UserControl> Factory { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public NavItem(string key, string title, Func<UserControl> factory)
        {
            Key = key;
            Title = title;
            Factory = factory;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

