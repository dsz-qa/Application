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
    /// Parser harmonogramu rat kredytu z CSV (bankowy, tolerancyjny, zgodny z cudzysłowami).
    /// Cel: wczytać harmonogram z możliwie "dowolnego" CSV eksportowanego przez bank.
    /// </summary>
    public sealed class LoanScheduleCsvParser
    {
        private static readonly CultureInfo Pl = new("pl-PL");

        // Normalizowane tokeny nagłówków (bez spacji/znaków, PL->latin)
        private static readonly string[] DateHeaders =
        {
            "data", "datasplaty", "termin", "terminsplaty", "paymentdate", "duedate"
        };

        private static readonly string[] TotalHeaders =
        {
            "kwotaraty", "rata", "razem", "total", "kwotadozaplaty", "kwotazaplaty", "kwota"
        };

        private static readonly string[] PrincipalHeaders =
        {
            "kapital", "kapitalowa", "principal", "splatakapitalu", "splata kapitalu"
        };

        private static readonly string[] InterestHeaders =
        {
            "odsetki", "odsetkowa", "interest"
        };

        private static readonly string[] BalanceHeaders =
        {
            "saldo", "pozostalo", "pozostalokapitalu", "balance", "remaining"
        };

        public IEnumerable<LoanInstallmentRow> Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            if (!File.Exists(path)) throw new FileNotFoundException(path);

            var text = ReadAllTextRobust(path);
            var records = ReadCsvRecords(text).ToList();
            if (records.Count == 0) return Enumerable.Empty<LoanInstallmentRow>();

            var delimiter = DetectDelimiter(records.Take(20).ToList());

            var rows = records
                .Select(r => SplitCsvRecord(r, delimiter))
                .Where(r => r.Count > 1)
                .ToList();

            if (rows.Count == 0) return Enumerable.Empty<LoanInstallmentRow>();

            int headerIdx = DetectHeaderRowIndex(rows);
            if (headerIdx < 0) headerIdx = 0;

            var header = rows[headerIdx].Select(NormalizeHeader).ToList();
            var headerMap = header
                .Select((h, i) => new { h, i })
                .ToDictionary(x => x.i, x => x.h);

            int dateCol = FindByHeaders(headerMap, DateHeaders);
            int totalCol = FindByHeaders(headerMap, TotalHeaders);
            int principalCol = FindByHeaders(headerMap, PrincipalHeaders);
            int interestCol = FindByHeaders(headerMap, InterestHeaders);
            int balanceCol = FindByHeaders(headerMap, BalanceHeaders);

            // Heurystyki, jeśli nagłówek jest “nietypowy”
            if (dateCol < 0 || totalCol < 0)
            {
                var dataRows = rows.Skip(headerIdx + 1).Take(80).ToList();
                var (d, t, p, it) = MapColumnsByHeuristics(dataRows);
                if (dateCol < 0) dateCol = d;
                if (totalCol < 0) totalCol = t;
                if (principalCol < 0) principalCol = p;
                if (interestCol < 0) interestCol = it;
            }

            if (dateCol < 0 || totalCol < 0)
                throw new InvalidOperationException("CSV nie ma wymaganych kolumn: Data + Kwota raty/Total.");

            var result = new List<LoanInstallmentRow>();

            foreach (var r in rows.Skip(headerIdx + 1))
            {
                if (r.Count <= Math.Max(dateCol, totalCol)) continue;

                // pomijaj podsumowania
                if (r.Any(c =>
                {
                    var n = NormalizeHeader(c);
                    return n.Contains("suma") || n.Contains("razem") || n.Contains("podsum");
                }))
                    continue;

                if (!TryParseDate(r[dateCol], out var dt)) continue;
                if (!TryParseMoney(r[totalCol], out var total) || total <= 0m) continue;

                decimal? cap = null, intr = null, bal = null;

                if (principalCol >= 0 && principalCol < r.Count && TryParseMoney(r[principalCol], out var c1)) cap = c1;
                if (interestCol >= 0 && interestCol < r.Count && TryParseMoney(r[interestCol], out var i1)) intr = i1;
                if (balanceCol >= 0 && balanceCol < r.Count && TryParseMoney(r[balanceCol], out var b1)) bal = b1;

                result.Add(new LoanInstallmentRow
                {
                    Date = dt.Date,
                    Total = total,
                    Principal = cap,
                    Interest = intr,
                    Remaining = bal
                });
            }

            return result.OrderBy(x => x.Date).ToList();
        }

        // ===================== RECORDS / CSV CORE =====================

        private static IEnumerable<string> ReadCsvRecords(string text)
        {
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '"')
                {
                    // "" => literal "
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue; // omit wrapping quotes
                }

                if (!inQuotes && (ch == '\n' || ch == '\r'))
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;

                    var rec = sb.ToString().Trim();
                    sb.Clear();

                    if (!string.IsNullOrWhiteSpace(rec))
                        yield return rec;

                    continue;
                }

                sb.Append(ch);
            }

            var last = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(last))
                yield return last;
        }

        private static char DetectDelimiter(List<string> sampleRecords)
        {
            var candidates = new[] { ';', ',', '\t', '|' };

            char best = ';';
            double bestScore = double.NegativeInfinity;

            foreach (var c in candidates)
            {
                var counts = new List<int>();

                foreach (var rec in sampleRecords)
                {
                    var parts = SplitCsvRecord(rec, c);
                    if (parts.Count > 1) counts.Add(parts.Count);
                }

                if (counts.Count < 3) continue;

                int mode = counts.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
                int modeFreq = counts.Count(x => x == mode);
                double avg = counts.Average();

                double score = modeFreq * 1000.0 + avg;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best;
        }

        private static List<string> SplitCsvRecord(string record, char delimiter)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < record.Length; i++)
            {
                char ch = record[i];

                if (ch == '"')
                {
                    if (inQuotes && i + 1 < record.Length && record[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue; // omit wrapping quotes
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

        // ===================== HEADER DETECTION / MAPPING =====================

        private static int DetectHeaderRowIndex(List<List<string>> rows)
        {
            int limit = Math.Min(25, rows.Count);

            for (int i = 0; i < limit; i++)
            {
                var cells = rows[i];
                if (cells.Count < 2) continue;

                var normalized = cells.Select(NormalizeHeader).ToList();

                bool hasDate = normalized.Any(h => DateHeaders.Any(x => h == x || h.Contains(x)));
                bool hasTotal = normalized.Any(h => TotalHeaders.Any(x => h == x || h.Contains(x)));

                if (hasDate && hasTotal)
                    return i;

                // heurystyka nagłówka: dużo liter, mało cyfr
                int letterish = normalized.Count(h => h.Any(char.IsLetter));
                int digitish = normalized.Count(h => h.Any(char.IsDigit));
                if (cells.Count >= 3 && letterish >= 2 && digitish == 0)
                {
                    if (i + 1 < rows.Count)
                    {
                        bool nextLooksLikeData = rows[i + 1].Any(x => TryParseDate(x, out _)) ||
                                                rows[i + 1].Any(x => TryParseMoney(x, out _));
                        if (nextLooksLikeData)
                            return i;
                    }
                }
            }

            return -1;
        }

        private static int FindByHeaders(Dictionary<int, string> headerMap, string[] headers)
        {
            // exact
            foreach (var kv in headerMap)
                if (headers.Contains(kv.Value))
                    return kv.Key;

            // contains
            foreach (var kv in headerMap)
            {
                var h = kv.Value ?? "";
                foreach (var token in headers)
                {
                    if (!string.IsNullOrWhiteSpace(token) && h.Contains(token, StringComparison.OrdinalIgnoreCase))
                        return kv.Key;
                }
            }

            return -1;
        }

        private static (int DateCol, int TotalCol, int PrincipalCol, int InterestCol) MapColumnsByHeuristics(List<List<string>> rows)
        {
            if (rows.Count == 0) return (-1, -1, -1, -1);

            int maxCols = rows.Max(r => r.Count);
            int dateCol = -1;
            int totalCol = -1;

            for (int col = 0; col < maxCols; col++)
            {
                int ok = 0, ch = 0;
                foreach (var r in rows.Take(60))
                {
                    if (col >= r.Count) continue;
                    ch++;
                    if (TryParseDate(r[col], out _)) ok++;
                }

                if (ch >= 6 && ok >= ch * 0.3)
                {
                    dateCol = col;
                    break;
                }
            }

            for (int col = 0; col < maxCols; col++)
            {
                int ok = 0, ch = 0;
                foreach (var r in rows.Take(60))
                {
                    if (col >= r.Count) continue;
                    ch++;
                    if (TryParseMoney(r[col], out var m) && m > 0m) ok++;
                }

                if (ch >= 6 && ok >= ch * 0.3)
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

                    int ok = 0, ch = 0;
                    foreach (var r in rows.Take(60))
                    {
                        if (col >= r.Count) continue;
                        ch++;
                        if (TryParseMoney(r[col], out var m) && m >= 0m) ok++;
                    }

                    if (ch >= 6 && ok >= ch * 0.6)
                        moneyCols.Add(col);
                }

                if (moneyCols.Count >= 1) principalCol = moneyCols[0];
                if (moneyCols.Count >= 2) interestCol = moneyCols[1];
            }

            return (dateCol, totalCol, principalCol, interestCol);
        }

        private static string NormalizeHeader(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();

            s = s.Replace("ł", "l").Replace("ą", "a").Replace("ę", "e")
                 .Replace("ć", "c").Replace("ń", "n").Replace("ó", "o")
                 .Replace("ś", "s").Replace("ż", "z").Replace("ź", "z");

            s = s.Replace(" ", "")
                 .Replace("\t", "")
                 .Replace("-", "")
                 .Replace("_", "")
                 .Replace(".", "")
                 .Replace(":", "");

            return s;
        }

        // ===================== DATE / MONEY =====================

        private static bool TryParseDate(string raw, out DateTime date)
        {
            date = default;
            raw = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Replace("\u00A0", " ").Replace("\u202F", " ").Trim();
            raw = raw.Replace("r.", "", StringComparison.OrdinalIgnoreCase)
                     .Replace("r", "", StringComparison.OrdinalIgnoreCase)
                     .Trim();
            raw = raw.Replace("T", " ");

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && (parts[0].Contains('.') || parts[0].Contains('-') || parts[0].Contains('/')))
                raw = parts[0];

            string[] formats =
            {
                "dd.MM.yyyy","d.M.yyyy",
                "dd-MM-yyyy","d-M-yyyy",
                "yyyy-MM-dd",
                "yyyy.MM.dd",
                "dd/MM/yyyy","d/M/yyyy",
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

        // ===================== ROBUST FILE READING =====================

        private static void EnsureCodePages()
        {
            // potrzebne dla Windows-1250 / ISO-8859-2 na niektórych środowiskach
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static string ReadAllTextRobust(string path)
        {
            EnsureCodePages();

            // 1) BOM/UTF8/UTF16 auto-detection
            var text = ReadWithEncoding(path, Encoding.UTF8, detectBom: true);

            // 2) jeśli widać krzaki, próbujemy bankowych kodowań
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
            int bad = text.Count(c => c == '\uFFFD'); // replacement character
            return bad >= 5;
        }
    }
}
