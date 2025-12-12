using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Finly.Models;

namespace Finly.Services
{
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

            // 1) Czytanie z BOM + fallback na bankowe kodowania
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
            // BOM detection (UTF8/UTF16) – standard w .NET. :contentReference[oaicite:1]{index=1}
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

            // Uproszczona heurystyka: dużo znaków replacement char '�'
            int bad = text.Count(c => c == '\uFFFD');
            return bad >= 5; // próg praktyczny
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

                    // premiuj więcej kolumn + stabilność
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

            // nawiasy jako minus, czasem bank tak zapisuje
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
