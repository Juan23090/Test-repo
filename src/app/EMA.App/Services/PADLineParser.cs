using System.Globalization;
using System.Text.RegularExpressions;

namespace EMA.App.Services;

public class ParsedPADLine
{
    public DateTime? Date { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransactionType { get; set; } = "PAD";
    public bool IsValid { get; set; }
}

public static class PADLineParser
{
    private static readonly string[] NegativeOrFeeKeywords =
    [
        "e-Transfer sent",
        "transfer sent",
        "online transfer sent",
        "Online transfer sent",
        "ONLINE TRANSFER SENT",
        "Monthly fee",
        "MonthlyAgentFee",
        "PAY-FILE FEES",
        "CREDIT CARD",
        "Interac purchase",
        "Reversed debit",
        "Tax Refund",
        "Investment",
        "Square Canada",
        "GST,CANADA",
        "GST, CANADA",
        ",GST,"
    ];

    private static readonly string[] NoiseKeywords =
    [
        "EFT Settlement",
        "e-Transfer - Autodeposit",
        "e-Transfer - Auto Deposit",
        "e-Transfer - Received",
        "E-TRANSFER - AUTODEPOSIT",
        "E-TRANSFER RECEIVED",
        "E-Transfer - Autodeposit",
        "E-Transfer",
        "e-Transfer",
        "ETransfer",
        "etransfer",
        "Autodeposit",
        "AUTODEPOSIT",
        "Inbound Transfer",
        "INBOUND TRANSFER",
        "Interac e-Transfer",
        "INTERAC",
        "Online transfer received -",
        "Online transfer received",
        "ONLINE TRANSFER RECEIVED",
        "EFT",
        "Settlement",
        "Pre-Authorized Debit",
        "Pre Authorized Debit",
        "Direct Debit",
        "PAD DONATION -",
        "PAD DONATION",
        "PAD",
        "DONATION",
        "SUCCESS",
        "PAYMENT",
        "TRANSFER",
        "AUTOMATIC",
        "SUMMARY",
        "REPORT",
        "CREDIT",
        "DEBIT",
        "PRE-AUTH",
        "PRE AUTH",
        "COMPLETED",
        "Chequing",
        "CHEQUING",
        "Savings",
        "SAVINGS"
    ];

    public static ParsedPADLine ParseLine(string unalteredText, IEnumerable<BankKeyPhraseDto>? keyphrases = null)
    {
        var result = new ParsedPADLine();
        if (string.IsNullOrWhiteSpace(unalteredText)) return result;

        string workingLine = unalteredText.Trim();

        if (IsCsvHeaderOrComment(workingLine))
        {
            return result;
        }

        if (workingLine.Count(c => c == ',') >= 3 && !workingLine.Contains("$"))
        {
            var csvParsed = TryParseCsvRow(workingLine, keyphrases);
            if (csvParsed != null && csvParsed.IsValid)
            {
                return csvParsed;
            }
        }

        result.TransactionType = DetermineTransactionType(workingLine, keyphrases);

        workingLine = Regex.Replace(workingLine, @"\b([A-Za-z]{3})\s+0\s*,?\s*(20\d{2})\b", "$1 01, $2");

        var (date, dateText) = ExtractDate(workingLine);
        result.Date = date;
        if (!string.IsNullOrEmpty(dateText))
        {
            workingLine = ReplaceFirst(workingLine, dateText, " ");
        }

        var (amount, amountText) = ExtractAmount(workingLine);
        result.Amount = amount;
        if (!string.IsNullOrEmpty(amountText))
        {
            workingLine = ReplaceFirst(workingLine, amountText, " ");
        }

        foreach (var keyword in NoiseKeywords)
        {
            workingLine = Regex.Replace(workingLine, $@"\b{Regex.Escape(keyword)}\b", " ", RegexOptions.IgnoreCase);
            workingLine = workingLine.Replace(keyword, " ", StringComparison.OrdinalIgnoreCase);
        }

        string rawName = ExtractName(workingLine);
        result.DonorName = ToProperCase(rawName);

        result.IsValid = result.Date.HasValue && result.Amount > 0 && !string.IsNullOrWhiteSpace(result.DonorName);
        return result;
    }

    private static bool IsCsvHeaderOrComment(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return true;
        string clean = line.TrimStart('\"', '#', '-', ' ');
        if (clean.StartsWith("Account Type", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("Date,", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("Transaction Date", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("1,Description", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("CSV created", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    public static string DetermineTransactionType(string text, IEnumerable<BankKeyPhraseDto>? keyphrases = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        if (keyphrases != null)
        {
            foreach (var kp in keyphrases)
            {
                if (!string.IsNullOrWhiteSpace(kp.KeyPhrase) && text.Contains(kp.KeyPhrase.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return kp.TransactionTypeID.Trim();
                }
            }
        }

        return string.Empty;
    }

    public static ParsedPADLine? TryParseCsvRow(string csvLine, IEnumerable<BankKeyPhraseDto>? keyphrases = null)
    {
        string rawTrim = csvLine.Trim();
        if (IsCsvHeaderOrComment(rawTrim)) return null;

        var cells = SplitCsvRow(rawTrim);
        if (cells.Count < 2) return null;

        BankKeyPhraseDto? matchedKp = null;
        if (keyphrases != null && keyphrases.Any())
        {
            matchedKp = keyphrases.FirstOrDefault(kp =>
                !string.IsNullOrWhiteSpace(kp.KeyPhrase) &&
                rawTrim.IndexOf(kp.KeyPhrase.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        bool isFeeOrNeg = matchedKp == null && NegativeOrFeeKeywords.Any(negKw => rawTrim.Contains(negKw, StringComparison.OrdinalIgnoreCase));

        DateTime? extractedDate = null;
        decimal extractedAmount = 0m;
        string rawDescription = string.Empty;

        int startIndex = 0;
        if (cells.Count >= 5 && (cells[0].Equals("Chequing", StringComparison.OrdinalIgnoreCase) || cells[0].Equals("Savings", StringComparison.OrdinalIgnoreCase)))
        {
            startIndex = 2;
        }

        for (int i = startIndex; i < cells.Count; i++)
        {
            string cell = cells[i].Trim('\"', ' ');
            if (string.IsNullOrWhiteSpace(cell)) continue;

            if (!extractedDate.HasValue)
            {
                var (d, _) = ExtractDate(cell);
                if (d.HasValue)
                {
                    extractedDate = d;
                    continue;
                }
            }

            if (cell.Length > 1 && cell.StartsWith("0") && cell.All(char.IsDigit) && !cell.Contains("."))
            {
                continue;
            }

            if ((i == 1 || i == 2) && cell.All(char.IsDigit) && !cell.Contains("."))
            {
                continue;
            }

            string cleanAmountStr = cell.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleanAmountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVal))
            {
                bool isExplicitDecimalOrCurrency = cleanAmountStr.Contains(".") || cell.Contains("$");
                if (isExplicitDecimalOrCurrency)
                {
                    if (extractedAmount == 0m || !extractedAmount.ToString().Contains("."))
                    {
                        extractedAmount = parsedVal;
                        continue;
                    }
                }
                else if (extractedAmount == 0m && parsedVal > 0 && parsedVal <= 10000000m)
                {
                    extractedAmount = parsedVal;
                    continue;
                }
            }

            if (!cell.All(char.IsDigit))
            {
                rawDescription += " " + cell;
            }
        }

        string txType = string.Empty;
        if (matchedKp != null && !string.IsNullOrWhiteSpace(matchedKp.TransactionTypeID))
        {
            txType = matchedKp.TransactionTypeID.Trim();
        }
        else if (!isFeeOrNeg)
        {
            txType = DetermineTransactionType(csvLine, keyphrases);
        }

        string workingName = rawDescription;
        foreach (var keyword in NoiseKeywords)
        {
            workingName = Regex.Replace(workingName, $@"\b{Regex.Escape(keyword)}\b", " ", RegexOptions.IgnoreCase);
            workingName = workingName.Replace(keyword, " ", StringComparison.OrdinalIgnoreCase);
        }

        string cleanName = ExtractName(workingName);
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = string.IsNullOrWhiteSpace(workingName) ? rawDescription.Trim() : workingName.Trim();
        }

        if (string.IsNullOrWhiteSpace(cleanName))
        {
            cleanName = isFeeOrNeg ? "Bank Fee / Charge" : "Unknown";
        }

        return new ParsedPADLine
        {
            Date = extractedDate,
            Amount = extractedAmount,
            DonorName = ToProperCase(cleanName),
            TransactionType = txType,
            IsValid = true
        };
    }

    public static List<string> SplitCsvRow(string row)
    {
        var list = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                list.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        list.Add(current.ToString().Trim());
        return list;
    }

    private static (DateTime? Date, string DateText) ExtractDate(string input)
    {
        string normalizedInput = Regex.Replace(input, @"\b([A-Za-z]{3})\s+0\s*,", "$1 01,");

        var m1 = Regex.Match(normalizedInput, @"\b[A-Za-z]{3}\s+\d{1,2}\s*,?\s*20\d{2}\b");
        if (m1.Success && DateTime.TryParse(m1.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1))
        {
            return (d1, m1.Value);
        }

        var m2 = Regex.Match(input, @"\b20\d{2}-\d{2}-\d{2}\b");
        if (m2.Success && DateTime.TryParseExact(m2.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
        {
            return (d2, m2.Value);
        }

        var m3 = Regex.Match(input, @"\b\d{1,2}/\d{1,2}/20\d{2}\b");
        if (m3.Success && DateTime.TryParse(m3.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d3))
        {
            return (d3, m3.Value);
        }

        var m4 = Regex.Match(input, @"\b\d{1,2}/\d{1,2}/\d{2}\b");
        if (m4.Success && DateTime.TryParse(m4.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d4))
        {
            return (d4, m4.Value);
        }

        return (null, string.Empty);
    }

    private static (decimal Amount, string AmountText) ExtractAmount(string input)
    {
        var matches = Regex.Matches(input, @"\$?\b\d{1,3}(?:,\d{3})*(?:\.\d{2})?\b");
        if (matches.Count == 0) return (0m, string.Empty);

        decimal smallestAmount = decimal.MaxValue;
        string smallestText = string.Empty;

        foreach (Match match in matches)
        {
            string cleanNum = match.Value.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(cleanNum, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                if (val > 0 && val < smallestAmount)
                {
                    smallestAmount = val;
                    smallestText = match.Value;
                }
            }
        }

        if (smallestAmount == decimal.MaxValue) return (0m, string.Empty);
        return (smallestAmount, smallestText);
    }

    private static string ExtractName(string input)
    {
        string withoutHashes = Regex.Replace(input, @"\b(?=[A-Za-z0-9]*\d)(?=[A-Za-z0-9]*[A-Za-z])[A-Za-z0-9]{6,}\b", " ");
        withoutHashes = Regex.Replace(withoutHashes, @"\b[a-fA-F0-9]{16,}\b", " ");
        string cleaned = Regex.Replace(withoutHashes, @"[^A-Za-z&\s]", " ");
        var segments = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Where(s => s.Length > 1 || s == "&")
                              .ToList();

        if (segments.Count == 0) return string.Empty;
        return string.Join(" ", segments);
    }

    private static string ToProperCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var textInfo = CultureInfo.CurrentCulture.TextInfo;
        string lower = input.ToLower(CultureInfo.CurrentCulture);
        return textInfo.ToTitleCase(lower);
    }

    private static string ReplaceFirst(string text, string search, string replace)
    {
        int pos = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (pos < 0) return text;
        return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
    }
}
