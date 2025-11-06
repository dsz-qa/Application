using System;
using System.Windows.Input;

namespace Finly.Helpers
{
    /// <summary>
    /// Uniwersalny RelayCommand (z obs³ug¹ wersji bez- i z-parametrem).
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        // Wersja bez parametru: RelayCommand(() => ..., () => bool)
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute is null ? null : new Predicate<object?>(_ => canExecute()))
        { }

        // Wersja z parametrem: RelayCommand(obj => ..., obj => bool)
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        // Podpinamy pod RequerySuggested, ¿eby automatycznie odœwie¿a³o stan przy zmianach fokusu/klawiatury itd.
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary> Rêczne wymuszenie odœwie¿enia CanExecute. </summary>
        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}
