using System.ComponentModel;

namespace Finly.ViewModels
{
 public sealed class CategoryFilterItem : INotifyPropertyChanged
 {
 private bool _sel;
 public string Name { get; set; } = "";
 public bool IsSelected
 {
 get => _sel;
 set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
 }
 public event PropertyChangedEventHandler? PropertyChanged;
 }
}
