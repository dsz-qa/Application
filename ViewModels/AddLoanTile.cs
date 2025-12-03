using System;

namespace Finly.ViewModels
{
    // Specjalny „kafelek” dodawania pożyczki.
    // Nie trzymamy tu żadnej logiki – ważne, że dziedziczy po LoanCardVm,
    // żeby dało się go dodać do ObservableCollection<LoanCardVm>.
    public sealed class AddLoanTile : LoanCardVm
    {
    }
}

