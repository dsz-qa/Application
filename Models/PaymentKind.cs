namespace Finly.Models
{
    /// <summary>
    /// Źródło prawdy dla typu źródła/portfela płatności.
    /// Przechowywane w DB jako INTEGER.
    /// </summary>
    public enum PaymentKind
    {
        FreeCash = 0,
        SavedCash = 1,
        BankAccount = 2,
        Envelope = 3
    }
}

