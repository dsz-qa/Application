using System;

namespace Finly.Models
{
 public class LoanModel
 {
 public int Id { get; set; }
 public int UserId { get; set; }
 public string Name { get; set; } = "";
 public decimal Principal { get; set; }
 public decimal InterestRate { get; set; } =0m; // roczna np.5.5
 public DateTime StartDate { get; set; } = DateTime.Today;
 public int TermMonths { get; set; } =0;
 public int PaymentDay { get; set; } = 0; // 0 = unspecified, otherwise day of month (1-31)
 public string? Note { get; set; }
 }
}