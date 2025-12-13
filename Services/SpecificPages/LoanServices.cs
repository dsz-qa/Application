// Finly/Services/LoansService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Finly.Models;

namespace Finly.Services
{
    /// <summary>
    /// Serwis kredytów – agreguje logikę obliczeń oraz parser harmonogramu CSV,
    /// aby nie trzymać tego w osobnych plikach.
    /// </summary>
    public static class LoansService
    {
        /// <summary>
        /// Standardowa rata annuitetowa.
        /// </summary>
        public static decimal CalculateMonthlyPayment(
            decimal principal,
            decimal annualRatePercent,
            int termMonths)
        {
            if (principal <= 0m || termMonths <= 0)
                return 0m;

            var r = annualRatePercent / 100m / 12m; // miesięczna stopa

            if (r == 0m)
                return Math.Round(principal / termMonths, 2);

            // A = P * r / (1 - (1+r)^-n)
            var denom = 1m - (decimal)Math.Pow((double)(1m + r), -termMonths);
            if (denom == 0m)
                return Math.Round(principal / termMonths, 2);

            var payment = principal * r / denom;
            return Math.Round(payment, 2);
        }

        /// <summary>
        /// Rozbija pierwszą ratę na część odsetkową i kapitałową.
        /// </summary>
        public static (decimal InterestPart, decimal PrincipalPart)
            CalculateFirstInstallmentBreakdown(
                decimal principal,
                decimal annualRatePercent,
                int termMonths)
        {
            var payment = CalculateMonthlyPayment(principal, annualRatePercent, termMonths);
            if (payment <= 0m)
                return (0m, 0m);

            var r = annualRatePercent / 100m / 12m;
            var interest = Math.Round(principal * r, 2);
            var capital = Math.Round(payment - interest, 2);
            if (capital < 0m) capital = 0m;

            return (interest, capital);
        }

        /// <summary>
        /// Poprzedni „umowny” termin raty względem podanej daty.
        /// Uwzględnia PaymentDay oraz start kredytu (nie cofamy się przed start).
        /// </summary>
        public static DateTime GetPreviousDueDate(DateTime today, int paymentDay, DateTime startDate)
        {
            if (paymentDay <= 0)
            {
                var d = today.Date.AddMonths(-1);
                return d < startDate.Date ? startDate.Date : d;
            }

            int dim = DateTime.DaysInMonth(today.Year, today.Month);
            int day = Math.Min(paymentDay, dim);
            var thisDue = new DateTime(today.Year, today.Month, day);

            if (today.Date >= thisDue.Date)
                return thisDue.Date < startDate.Date ? startDate.Date : thisDue.Date;

            var prevMonthFirst = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
            int dimPrev = DateTime.DaysInMonth(prevMonthFirst.Year, prevMonthFirst.Month);
            day = Math.Min(paymentDay, dimPrev);
            var prevDue = new DateTime(prevMonthFirst.Year, prevMonthFirst.Month, day);

            return prevDue.Date < startDate.Date ? startDate.Date : prevDue.Date;
        }

        /// <summary>
        /// Kolejny termin raty – płatności danego dnia miesiąca z korektą weekendową.
        /// </summary>
        public static DateTime GetNextDueDate(DateTime previousDueDate, int paymentDay)
        {
            if (paymentDay <= 0)
                return previousDueDate.AddMonths(1);

            var nextMonthFirst = new DateTime(previousDueDate.Year, previousDueDate.Month, 1).AddMonths(1);
            var daysInNextMonth = DateTime.DaysInMonth(nextMonthFirst.Year, nextMonthFirst.Month);
            var day = Math.Min(paymentDay, daysInNextMonth);

            var due = new DateTime(nextMonthFirst.Year, nextMonthFirst.Month, day);

            // weekend → na najbliższy dzień roboczy
            if (due.DayOfWeek == DayOfWeek.Saturday)
                due = due.AddDays(2);
            else if (due.DayOfWeek == DayOfWeek.Sunday)
                due = due.AddDays(1);

            return due;
        }
    }

    /// <summary>
    /// Pomocniczy serwis matematyczny do obliczeń kredytowych.
    /// Jedno źródło prawdy dla odsetek dziennych.
    /// </summary>
    public static class LoanMathService
    {
        /// <summary>
        /// Odsetki: kapitał * (oprocentowanie/365) * liczba dni.
        /// annualRate podajesz w % (np. 6.28).
        /// </summary>
        public static decimal CalculateInterest(
            decimal principal,
            decimal annualRate,
            DateTime from,
            DateTime to)
        {
            if (to <= from || principal <= 0m || annualRate <= 0m)
                return 0m;

            int days = (to.Date - from.Date).Days;
            if (days <= 0) return 0m;

            decimal dailyRate = annualRate / 100m / 365m;
            return Math.Round(principal * dailyRate * days, 2);
        }
    }

    /// <summary>
    /// Parser harmonogramu rat kredytu z CSV (bankowy, tolerancyjny).
    /// </summary>
    public sealed class LoanScheduleCsvParser
    {
        private static readonly CultureInfo Pl = new("pl-PL");

        private static readonly string[] DateHeaders = new[]
        {
            "data", "data splaty", "termin", "due date", "payment date", "date"
        }.Select(NormalizeHeader).ToArray();

        private static readonly string[] TotalHeaders = new[]
        {
            "rata", "kwota raty", "laczna rata", "do zaplaty", "amount", "payment", "installment", "total"
        }.Select(NormalizeHeader).ToArray();

        private static readonly string[] PrincipalHeaders = new[]
        {
            "kapital", "czesc kapitalowa", "principal", "capital"
        }.Select(NormalizeHeader).ToArray();

        private static readonly string[] InterestHeaders = new[]
        {
            "odsetki", "czesc odsetkowa", "interest"
        }.Select(NormalizeHeader).ToArray();

        public IReadOnlyList<LoanInstallmentRow> Parse(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Nie znaleziono pliku harmonogramu.", filePath);

            var text = ReadAllTextRobust(filePath);

            var lines = SplitLines(text)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
                throw new InvalidDataException("Plik CSV jest pusty.");

            var delimiter = DetectDelimiter(lines.Take(10).ToList());

            var rows = lines
                .Select(l => SplitCsvLine(l, delimiter).ToList())
                .Where(cells => cells.Count > 0 && cells.Any(x => !string.IsNullOrWhiteSpace(x)))
                .ToList();

            if (rows.Count == 0)
                throw new InvalidDataException("Nie znaleziono danych w pliku CSV (po rozbiciu na kolumny).");

            int headerIndex = DetectHeaderRowIndex(rows);
            Dictionary<int, string>? headerMap = null;

            if (headerIndex >= 0)
            {
                headerMap = rows[headerIndex]
                    .Select((name, idx) => new { idx, name = NormalizeHeader(name) })
                    .ToDictionary(x => x.idx, x => x.name);

                rows = rows.Skip(headerIndex + 1).ToList();
            }

            var mapping = headerMap is not null
                ? MapColumnsByHeader(headerMap)
                : MapColumnsByHeuristics(rows);

            if (mapping.DateCol < 0 || mapping.TotalCol < 0)
            {
                var msg =
                    "Nie udało się wykryć kolumn DATY i KWOTY.\n" +
                    $"Separator: '{delimiter}'\n" +
                    $"DateCol={mapping.DateCol}, TotalCol={mapping.TotalCol}\n" +
                    "Podpowiedź: sprawdź, czy w pliku jest kolumna z datą (np. 15.12.2025) oraz kolumna z kwotą raty.";
                throw new InvalidDataException(msg);
            }

            var result = new List<LoanInstallmentRow>();

            foreach (var r in rows)
            {
                if (mapping.DateCol >= r.Count || mapping.TotalCol >= r.Count)
                    continue;

                var dateRaw = r[mapping.DateCol];
                if (!TryParseDate(dateRaw, out var date))
                    continue;

                var totalRaw = r[mapping.TotalCol];
                if (!TryParseMoney(totalRaw, out var total))
                    continue;

                decimal? principal = null;
                decimal? interest = null;

                if (mapping.PrincipalCol >= 0 && mapping.PrincipalCol < r.Count
                    && TryParseMoney(r[mapping.PrincipalCol], out var p))
                    principal = p;

                if (mapping.InterestCol >= 0 && mapping.InterestCol < r.Count
                    && TryParseMoney(r[mapping.InterestCol], out var i))
                    interest = i;

                result.Add(new LoanInstallmentRow
                {
                    Date = date,
                    Total = total,
                    Principal = principal,
                    Interest = interest
                });
            }

            result = result.OrderBy(x => x.Date).ToList();

            if (result.Count == 0)
                throw new InvalidDataException(
                    "Nie udało się odczytać żadnej raty. CSV ma nietypowy format (daty/kwoty nie pasują do oczekiwanych wzorców).");

            return result;
        }

        private static string ReadAllTextRobust(string path)
        {
            // BOM detection (UTF8/UTF16) – standard w .NET
            string text = ReadWithEncoding(path, Encoding.UTF8, detectBom: true);

            // Jeżeli widać krzaki, spróbuj bankowych kodowań.
            if (LooksLikeMojibake(text))
            {
                var win1250 = Encoding.GetEncoding(1250);
                var iso88592 = Encoding.GetEncoding("ISO-8859-2");

                var t2 = ReadWithEncoding(path, win1250, detectBom: false);
                if (!LooksLikeMojibake(t2)) return t2;

                var t3 = ReadWithEncoding(path, iso88592, detectBom: false);
                return t3;
            }

            return text;
        }

        private static string ReadWithEncoding(string path, Encoding encoding, bool detectBom)
        {
            using var sr = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: detectBom);
            return sr.ReadToEnd();
        }

        private static bool LooksLikeMojibake(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            int bad = text.Count(c => c == '\uFFFD');
            return bad >= 5;
        }

        private static List<string> SplitLines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n')
                       .Split('\n')
                       .ToList();
        }

        private static char DetectDelimiter(List<string> sampleLines)
        {
            var candidates = new[] { ';', ',', '\t' };

            char best = ';';
            int bestScore = int.MinValue;

            foreach (var c in candidates)
            {
                int score = 0;
                foreach (var line in sampleLines)
                {
                    var parts = SplitCsvLine(line, c);

                    if (parts.Count >= 6) score += 4;
                    else if (parts.Count >= 3) score += 2;
                    else if (parts.Count == 2) score += 1;
                    else score -= 1;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best;
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];

                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                    continue;
                }

                if (!inQuotes && ch == delimiter)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString().Trim());
            return result;
        }

        private static int DetectHeaderRowIndex(List<List<string>> rows)
        {
            int limit = Math.Min(10, rows.Count);

            for (int i = 0; i < limit; i++)
            {
                var cells = rows[i];
                var normalized = cells.Select(NormalizeHeader).ToList();

                bool hasDate = normalized.Any(h => DateHeaders.Contains(h));
                bool hasTotal = normalized.Any(h => TotalHeaders.Contains(h));

                if (hasDate && hasTotal)
                    return i;
            }

            return -1;
        }

        private static string NormalizeHeader(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            s = s.Replace("ł", "l").Replace("ą", "a").Replace("ę", "e")
                 .Replace("ć", "c").Replace("ń", "n").Replace("ó", "o")
                 .Replace("ś", "s").Replace("ż", "z").Replace("ź", "z");
            return s;
        }

        private static (int DateCol, int TotalCol, int PrincipalCol, int InterestCol) MapColumnsByHeader(Dictionary<int, string> headerMap)
        {
            int dateCol = FindByHeaders(headerMap, DateHeaders);
            int totalCol = FindByHeaders(headerMap, TotalHeaders);
            int principalCol = FindByHeaders(headerMap, PrincipalHeaders);
            int interestCol = FindByHeaders(headerMap, InterestHeaders);

            return (dateCol, totalCol, principalCol, interestCol);
        }

        private static int FindByHeaders(Dictionary<int, string> headerMap, string[] headers)
        {
            foreach (var kv in headerMap)
            {
                if (headers.Contains(kv.Value))
                    return kv.Key;
            }
            return -1;
        }

        private static (int DateCol, int TotalCol, int PrincipalCol, int InterestCol) MapColumnsByHeuristics(List<List<string>> rows)
        {
            int maxCols = rows.Max(r => r.Count);
            int dateCol = -1;
            int totalCol = -1;

            for (int col = 0; col < maxCols; col++)
            {
                int okDates = 0;
                int checkedRows = 0;

                foreach (var r in rows.Take(30))
                {
                    if (col >= r.Count) continue;
                    checkedRows++;
                    if (TryParseDate(r[col], out _)) okDates++;
                }

                if (checkedRows >= 5 && okDates >= checkedRows * 0.6)
                {
                    dateCol = col;
                    break;
                }
            }

            for (int col = 0; col < maxCols; col++)
            {
                int okMoney = 0;
                int checkedRows = 0;

                foreach (var r in rows.Take(30))
                {
                    if (col >= r.Count) continue;
                    checkedRows++;
                    if (TryParseMoney(r[col], out var m) && m >= 0) okMoney++;
                }

                if (checkedRows >= 5 && okMoney >= checkedRows * 0.6)
                {
                    totalCol = col;
                    break;
                }
            }

            int principalCol = -1;
            int interestCol = -1;

            if (totalCol >= 0)
            {
                var moneyCols = new List<int>();
                for (int col = 0; col < maxCols; col++)
                {
                    if (col == totalCol) continue;

                    int ok = 0;
                    int ch = 0;
                    foreach (var r in rows.Take(30))
                    {
                        if (col >= r.Count) continue;
                        ch++;
                        if (TryParseMoney(r[col], out _)) ok++;
                    }
                    if (ch >= 5 && ok >= ch * 0.6)
                        moneyCols.Add(col);
                }

                if (moneyCols.Count >= 1) principalCol = moneyCols[0];
                if (moneyCols.Count >= 2) interestCol = moneyCols[1];
            }

            return (dateCol, totalCol, principalCol, interestCol);
        }

        private static bool TryParseDate(string raw, out DateTime date)
        {
            date = default;
            raw = (raw ?? "").Trim();

            if (string.IsNullOrWhiteSpace(raw)) return false;

            string[] formats = new[]
            {
                "dd.MM.yyyy", "d.M.yyyy",
                "dd-MM-yyyy", "d-M-yyyy",
                "yyyy-MM-dd",
                "yyyy.MM.dd",
                "dd/MM/yyyy", "d/M/yyyy",
            };

            if (DateTime.TryParseExact(raw, formats, Pl, DateTimeStyles.None, out date))
                return true;

            return DateTime.TryParse(raw, Pl, DateTimeStyles.AllowWhiteSpaces, out date);
        }

        private static bool TryParseMoney(string raw, out decimal value)
        {
            value = default;
            raw = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Replace("PLN", "", StringComparison.OrdinalIgnoreCase)
                     .Replace("zł", "", StringComparison.OrdinalIgnoreCase)
                     .Replace("zl", "", StringComparison.OrdinalIgnoreCase);

            raw = raw.Replace("\u00A0", " ").Replace("\u202F", " ");
            raw = raw.Replace(" ", "");

            if (raw.StartsWith("(") && raw.EndsWith(")"))
                raw = "-" + raw.Trim('(', ')');

            int comma = raw.LastIndexOf(',');
            int dot = raw.LastIndexOf('.');

            if (comma >= 0 && dot >= 0)
            {
                if (dot < comma)
                {
                    raw = raw.Replace(".", "");
                    raw = raw.Replace(',', '.');
                }
                else
                {
                    raw = raw.Replace(",", "");
                }
            }
            else if (comma >= 0)
            {
                raw = raw.Replace(',', '.');
            }

            return decimal.TryParse(
                raw,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
