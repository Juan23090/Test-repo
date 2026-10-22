using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace EMA.App.Services;

public class PADProcessingService : IPADProcessingService
{
    private readonly HttpClient _http;
    private const string ApiBase = "/api";

    public PADProcessingService(HttpClient http)
    {
        _http = http;
    }

    public static string FormatDateRangeSummary(IEnumerable<DateTime> dates)
    {
        var dateList = dates?.ToList() ?? new List<DateTime>();
        if (dateList.Count == 0) return string.Empty;

        var minDate = dateList.Min();
        var maxDate = dateList.Max();
        return minDate.Date == maxDate.Date 
            ? minDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture) 
            : $"{minDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture)} To {maxDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public async Task<PADProcessResult> ProcessStatementFileAsync(Stream fileStream, string fileName, int organizationID, int userID, int? methodOfGivingID = null, string? bankId = null)
    {
        if (fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return await ProcessCsvStatementAsync(fileStream, organizationID, userID, methodOfGivingID, bankId);
        }

        return await ProcessPdfStatementAsync(fileStream, organizationID, userID, methodOfGivingID, bankId);
    }

    public async Task<PADProcessResult> ProcessCsvStatementAsync(Stream csvStream, int organizationID, int userID, int? methodOfGivingID = null, string? bankId = null)
    {
        var result = new PADProcessResult();

        try
        {
            var rawLines = await ReadCsvLinesAsync(csvStream);

            if (rawLines.Count == 0)
            {
                result.Success = false;
                result.Message = "The uploaded CSV file contains no readable data.";
                return result;
            }

            var keyphrases = await FetchKeyphrasesFromDatabaseAsync(bankId);
            var validParsed = new List<(string RawLine, ParsedPADLine Parsed)>();

            foreach (var line in rawLines)
            {
                var parsed = PADLineParser.TryParseCsvRow(line, keyphrases);
                if (parsed != null && parsed.IsValid)
                {
                    validParsed.Add((line, parsed));
                }
            }

            result.TotalTransactionsRead = rawLines.Count(r => !r.StartsWith("#") && !r.StartsWith("Account Type") && !r.StartsWith("Date,"));
            result.TotalExtractedLines = validParsed.Count;
            result.FilteredDonationLinesCount = validParsed.Count;
            result.ExtractedDonationLines = validParsed.Select(v => v.RawLine).ToList();
            result.ETransfersCount = validParsed.Count(p => p.Parsed.TransactionType == "e-Transfer" || p.Parsed.TransactionType == "eTransfer");
            result.PadsCount = validParsed.Count(p => p.Parsed.TransactionType == "PAD");
            
            if (validParsed.Count == 0)
            {
                result.Success = false;
                result.Message = "No valid donation or transfer transactions could be identified in the CSV statement.";
                return result;
            }

            var parsedDates = validParsed.Select(v => v.Parsed.Date)
                                         .Where(d => d.HasValue)
                                         .Select(d => d!.Value)
                                         .ToList();

            string dateRangeSummary = FormatDateRangeSummary(parsedDates);
            result.DateRangeSummary = dateRangeSummary;

            var batchCreationResult = await CreateBatchInDatabaseAsync(organizationID, userID, methodOfGivingID, result.ExtractedDonationLines);
            result.PADBatchID = batchCreationResult.BatchID;
            result.PADBatchStatusID = batchCreationResult.StatusID;
            result.StatusDescription = batchCreationResult.StatusID == 40 ? "Batch Aborted" : "PAD Details Imported";
            result.Success = true;
            result.Message = batchCreationResult.StatusID == 40
                ? $"Transaction Already Processed - Must be ReImported. Batch #{batchCreationResult.BatchID} marked as Aborted."
                : $"Successfully created Batch #{batchCreationResult.BatchID} in database with {validParsed.Count} donation lines. ({dateRangeSummary})";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error processing CSV statement: {ex.Message}";
        }

        return result;
    }

    public async Task<PADProcessResult> ProcessPdfStatementAsync(Stream pdfStream, int organizationID, int userID, int? methodOfGivingID = null, string? bankId = null)
    {
        var result = new PADProcessResult();

        try
        {
            var rawLines = await ExtractRawTextLinesFromPdfAsync(pdfStream);
            result.TotalTransactionsRead = rawLines.Count;
            result.TotalExtractedLines = rawLines.Count;

            if (rawLines.Count == 0)
            {
                result.Success = false;
                result.Message = "No text could be extracted from the uploaded PDF statement.";
                return result;
            }

            var keyphrases = await FetchKeyphrasesFromDatabaseAsync(bankId);
            if (keyphrases.Count == 0)
            {
                result.Success = false;
                result.Message = "No active statement keyphrases configured in the database for this bank. Please configure keywords in the Banks setup.";
                return result;
            }

            var filteredLines = FilterPdfLinesByKeywords(rawLines, keyphrases);

            if (filteredLines.Count == 0)
            {
                filteredLines = rawLines.Where(l => StartsWithDatePrefix(l) && !IsTableOrPageHeader(l)).ToList();
            }

            if (filteredLines.Count == 0)
            {
                result.Success = false;
                result.Message = "No donation line items matched the configured bank keyphrases in the PDF statement.";
                return result;
            }

            result.FilteredDonationLinesCount = filteredLines.Count;
            result.TotalTransactionsRead = filteredLines.Count;
            result.TotalExtractedLines = filteredLines.Count;
            result.ExtractedDonationLines = filteredLines;

            var parsedList = filteredLines.Select(l => PADLineParser.ParseLine(l)).ToList();
            result.ETransfersCount = parsedList.Count(p => p.TransactionType == "e-Transfer");
            result.PadsCount = parsedList.Count(p => p.TransactionType == "PAD");

            var parsedDates = parsedList.Select(p => p.Date)
                                        .Where(d => d.HasValue)
                                        .Select(d => d!.Value)
                                        .ToList();

            string dateRangeSummary = FormatDateRangeSummary(parsedDates);
            result.DateRangeSummary = dateRangeSummary;

            var batchCreationResult = await CreateBatchInDatabaseAsync(organizationID, userID, methodOfGivingID, filteredLines);
            result.PADBatchID = batchCreationResult.BatchID;
            result.PADBatchStatusID = batchCreationResult.StatusID;
            result.StatusDescription = batchCreationResult.StatusID == 40 ? "Batch Aborted" : "PAD Details Imported";
            result.Success = true;
            result.Message = batchCreationResult.StatusID == 40
                ? $"Transaction Already Processed - Must be ReImported. Batch #{batchCreationResult.BatchID} marked as Aborted."
                : $"Successfully created Batch #{batchCreationResult.BatchID} in database with {filteredLines.Count} donation lines stored in dbo.PADBatchLine. ({dateRangeSummary})";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error processing PDF: {ex.Message}";
        }

        return result;
    }

    public async Task<List<string>> ExtractRawTextLinesFromPdfAsync(Stream pdfStream)
    {
        var extractedLines = new List<string>();

        using var memoryStream = new MemoryStream();
        await pdfStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var content = new StreamContent(memoryStream);
        var response = await _http.PostAsync($"{ApiBase}/donations/pad-extract-pdf", content);
        if (response.IsSuccessStatusCode)
        {
            var ocrLines = await response.Content.ReadFromJsonAsync<List<string>>();
            if (ocrLines != null)
            {
                extractedLines = ocrLines;
            }
        }

        return extractedLines;
    }

    private async Task<List<BankKeyPhraseDto>> FetchKeyphrasesFromDatabaseAsync(string? bankId = null)
    {
        try
        {
            string url = string.IsNullOrWhiteSpace(bankId)
                ? $"{ApiBase}/donations/pad-keywords"
                : $"{ApiBase}/donations/pad-keywords?bankId={Uri.EscapeDataString(bankId.Trim())}";

            var response = await _http.GetFromJsonAsync<List<BankKeyPhraseDto>>(url);
            if (response != null && response.Count > 0)
            {
                return response;
            }
        }
        catch
        {
        }

        return new List<BankKeyPhraseDto>();
    }

    private static bool IsTableOrPageHeader(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string lower = text.ToLowerInvariant();
        return lower.StartsWith("date") ||
               lower.StartsWith("filters") ||
               lower.StartsWith("transaction history") ||
               lower.Contains("running balance") ||
               lower.Contains("debit") ||
               lower.Contains("credit") ||
               lower.Contains("accou") ||
               lower.Contains("page") ||
               lower.Contains("file generated") ||
               lower.Contains("online banking") ||
               lower.Contains("disclaimer") ||
               lower.Contains("newest - oldest") ||
               lower.Contains("newest-oldest") ||
               lower.Contains("default");
    }

    private List<string> FilterPdfLinesByKeywords(List<string> lines, List<BankKeyPhraseDto> keyphrases)
    {
        var matchedLines = new List<string>();
        var phraseStrings = keyphrases.Select(k => k.KeyPhrase).ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (IsTableOrPageHeader(line)) continue;

            bool containsKeyword = phraseStrings.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (!containsKeyword && !StartsWithDatePrefix(line)) continue;

            var parsed = PADLineParser.ParseLine(line);
            bool isCurrentValid = StartsWithDatePrefix(line) || containsKeyword || parsed.IsValid;

            if (isCurrentValid)
            {
                while (i + 1 < lines.Count)
                {
                    string nextLine = lines[i + 1].Trim();
                    if (string.IsNullOrWhiteSpace(nextLine)) { i++; continue; }

                    if (IsTableOrPageHeader(nextLine)) break;

                    var nextParsed = PADLineParser.ParseLine(nextLine);
                    bool isNextValidRow = StartsWithDatePrefix(nextLine) || (nextParsed.Date.HasValue && nextParsed.Amount > 0);

                    if (!isNextValidRow)
                    {
                        line = $"{line} {nextLine}";
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                matchedLines.Add(line);
            }
        }

        return matchedLines;
    }

    private bool StartsWithDatePrefix(string line)
    {
        string trimmed = line.TrimStart();
        if (Regex.IsMatch(trimmed, @"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\b\s*\d{1,2}", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}") || Regex.IsMatch(trimmed, @"^\d{1,2}/\d{1,2}/\d{2,4}"))
        {
            return true;
        }

        return false;
    }

    private async Task<(int BatchID, int StatusID)> CreateBatchInDatabaseAsync(int organizationID, int userID, int? methodOfGivingID, List<string> lines)
    {
        var payload = new
        {
            EMAOrganizationID = organizationID,
            CreatedByUserID = userID,
            EMAMethodOfGivingID = methodOfGivingID,
            UnalteredLines = lines,
            ParsedLines = lines.Select(l =>
            {
                var p = PADLineParser.ParseLine(l);
                return new
                {
                    UnalteredText = l,
                    PADDate = p.Date,
                    PADDonorName = p.DonorName,
                    PADDonationAmount = p.Amount
                };
            }).ToList()
        };

        var response = await _http.PostAsJsonAsync($"{ApiBase}/donations/pad-batch", payload);
        if (!response.IsSuccessStatusCode)
        {
            var errText = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(errText) ? "Failed to save batch in database." : errText);
        }

        var resultObj = await response.Content.ReadFromJsonAsync<PADBatchCreationResponse>();
        if (resultObj == null || resultObj.PADBatchID <= 0)
        {
            throw new Exception("Invalid response received from batch creation API.");
        }

        return (resultObj.PADBatchID, resultObj.PADBatchStatusID);
    }

    public async Task<List<PADMatchResultDto>> MatchDonorsForBatchAsync(int organizationId, int batchId)
    {
        var keyphrases = await FetchKeyphrasesFromDatabaseAsync();
        var payload = new { EMAOrganizationID = organizationId, PADBatchID = batchId };
        var response = await _http.PostAsJsonAsync($"{ApiBase}/donations/pad-match-donors", payload);
        if (response.IsSuccessStatusCode)
        {
            var results = await response.Content.ReadFromJsonAsync<List<PADMatchResultDto>>();
            if (results != null)
            {
                foreach (var item in results)
                {
                    item.TransactionType = PADLineParser.DetermineTransactionType(item.UnalteredText, keyphrases);
                }
                return results;
            }
        }

        return new List<PADMatchResultDto>();
    }

    public async Task<bool> UpdateBatchLineDonorAsync(int padBatchLineId, string officialDonorName, int padBatchId = 0)
    {
        var payload = new { PADBatchLineID = padBatchLineId, PADBatchID = padBatchId, OfficialDonorName = officialDonorName };
        var response = await _http.PostAsJsonAsync($"{ApiBase}/donations/pad-update-line-donor", payload);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<PADBatchItemDto>> GetPADBatchesAsync(int organizationId)
    {
        try
        {
            var results = await _http.GetFromJsonAsync<List<PADBatchItemDto>>($"{ApiBase}/donations/pad-batches?EMAOrganizationID={organizationId}");
            return results ?? new List<PADBatchItemDto>();
        }
        catch
        {
            return new List<PADBatchItemDto>();
        }
    }

    public async Task<List<BankItemDto>> GetBanksAsync()
    {
        try
        {
            var results = await _http.GetFromJsonAsync<List<BankItemDto>>($"{ApiBase}/donations/banks");
            return results ?? new List<BankItemDto>();
        }
        catch
        {
            return new List<BankItemDto>();
        }
    }

    public async Task<PADCreateDonationsResultDto> CreateDonationRecordsForBatchAsync(int organizationId, int batchId, int userId, Dictionary<long, int>? lineDonorAssignments = null, List<int>? deletedLineIds = null, Dictionary<long, int>? lineFundOverrides = null)
    {
        var payload = new
        {
            EMAOrganizationID = organizationId,
            PADBatchID = batchId,
            UserID = userId,
            LineDonorAssignments = lineDonorAssignments,
            DeletedLineIDs = deletedLineIds,
            LineFundOverrides = lineFundOverrides
        };

        var response = await _http.PostAsJsonAsync($"{ApiBase}/donations/pad-create-donations", payload);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<PADCreateDonationsResultDto>();
            return result ?? new PADCreateDonationsResultDto { Success = true, Message = "Donation records created successfully." };
        }

        var errorText = await response.Content.ReadAsStringAsync();
        return new PADCreateDonationsResultDto
        {
            Success = false,
            Message = string.IsNullOrWhiteSpace(errorText) ? "Failed to create donation records." : errorText
        };
    }

    private static async Task<List<string>> ReadCsvLinesAsync(Stream csvStream)
    {
        var rawLines = new List<string>();
        using var reader = new StreamReader(csvStream);
        string? currentLine;
        var sb = new System.Text.StringBuilder();

        while ((currentLine = await reader.ReadLineAsync()) != null)
        {
            string line = currentLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string clean = line.TrimStart('\"', '#', ' ');
            bool isNewRow = clean.StartsWith("Account Type", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("Date,", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("Transaction Date", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("Chequing", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("Savings", StringComparison.OrdinalIgnoreCase) ||
                            Regex.IsMatch(clean, @"^\d{1,2}/\d{1,2}/\d{2,4}") ||
                            Regex.IsMatch(clean, @"^\d{4}-\d{2}-\d{2}");

            if (isNewRow && sb.Length > 0)
            {
                rawLines.Add(sb.ToString().Trim());
                sb.Clear();
            }

            if (sb.Length > 0) sb.Append(" ");
            sb.Append(line);
        }

        if (sb.Length > 0)
        {
            rawLines.Add(sb.ToString().Trim());
        }

        return rawLines;
    }

    private class PADBatchCreationResponse
    {
        public int PADBatchID { get; set; }
        public int PADBatchStatusID { get; set; }
        public int LinesInserted { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
