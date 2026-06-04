using System.Text;
using System.Text.RegularExpressions;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ike.Api.Data;
using Ike.Api.DTOs.Quotes;
using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Ike.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QuotesController : ControllerBase
{
    private const string DiscountModeNone = "None";
    private const string DiscountModeGlobal = "Global";
    private const string DiscountModePerItem = "PerItem";
    private const string DiscountModePerItemAndGlobal = "PerItemAndGlobal";
    private const string QuoteUploadFolder = "uploads/quotes";
    private const long MaxUploadedQuoteFileSizeBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedUploadedQuoteExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".txt", ".csv"
    };

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _emailService;
    private readonly ICurrentUserService _currentUser;
    private readonly IStatusTransitionService _statusTransitions;
    private readonly IScopeGuardService _scopeGuard;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IPermissionService _permissionService;

    public QuotesController(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IEmailService emailService,
        ICurrentUserService currentUser,
        IStatusTransitionService statusTransitions,
        IScopeGuardService scopeGuard,
        UserManager<ApplicationUser> userManager,
        IPermissionService permissionService)
    {
        _db = db;
        _env = env;
        _emailService = emailService;
        _currentUser = currentUser;
        _statusTransitions = statusTransitions;
        _scopeGuard = scopeGuard;
        _userManager = userManager;
        _permissionService = permissionService;
    }

    private static decimal ClampDiscountPercent(decimal value)
    {
        if (value < 0m) return 0m;
        if (value > 100m) return 100m;
        return value;
    }

    private static string NormalizeDiscountMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim();
        if (m.Equals(DiscountModeGlobal, StringComparison.OrdinalIgnoreCase)) return DiscountModeGlobal;
        if (m.Equals(DiscountModePerItem, StringComparison.OrdinalIgnoreCase)) return DiscountModePerItem;
        if (m.Equals(DiscountModePerItemAndGlobal, StringComparison.OrdinalIgnoreCase)) return DiscountModePerItemAndGlobal;
        return DiscountModeNone;
    }

    private static bool UsesPerItemDiscount(string discountMode) =>
        discountMode.Equals(DiscountModePerItem, StringComparison.OrdinalIgnoreCase)
        || discountMode.Equals(DiscountModePerItemAndGlobal, StringComparison.OrdinalIgnoreCase);

    private static bool UsesGlobalDiscount(string discountMode) =>
        discountMode.Equals(DiscountModeGlobal, StringComparison.OrdinalIgnoreCase)
        || discountMode.Equals(DiscountModePerItemAndGlobal, StringComparison.OrdinalIgnoreCase);

    private static decimal ComputeSubtotal(IEnumerable<QuoteLineItemDto> lineItems) =>
        lineItems.Where(li => li.Quantity > 0 && li.UnitPrice >= 0).Sum(li => li.Quantity * li.UnitPrice);

    private static decimal ComputePerItemDiscount(IEnumerable<QuoteLineItemDto> lineItems, string discountMode)
    {
        if (!UsesPerItemDiscount(discountMode))
            return 0m;
        return lineItems
            .Where(li => li.Quantity > 0 && li.UnitPrice >= 0)
            .Sum(li =>
            {
                var sub = li.Quantity * li.UnitPrice;
                var pct = ClampDiscountPercent(li.DiscountPercent);
                return Math.Round(sub * (pct / 100m), 2, MidpointRounding.AwayFromZero);
            });
    }

    private static decimal ComputeQuoteAmountFromLines(IEnumerable<QuoteLineItemDto> lineItems, string discountMode, decimal globalDiscountPercent)
    {
        var subtotal = ComputeSubtotal(lineItems);
        var perItemDiscount = ComputePerItemDiscount(lineItems, discountMode);
        var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
        if (!UsesGlobalDiscount(discountMode))
            return Math.Round(afterPerItem, 2, MidpointRounding.AwayFromZero);
        var pct = ClampDiscountPercent(globalDiscountPercent);
        var globalDiscount = Math.Round(afterPerItem * (pct / 100m), 2, MidpointRounding.AwayFromZero);
        return Math.Round(Math.Max(0m, afterPerItem - globalDiscount), 2, MidpointRounding.AwayFromZero);
    }

    private static (decimal subtotal, decimal discount) ComputeQuoteSummary(IEnumerable<QuoteLineItemResponseDto> lineItems, string discountMode, decimal globalDiscountPercent, decimal fallbackAmount)
    {
        var list = lineItems.ToList();
        if (list.Count == 0)
            return (fallbackAmount, 0m);

        var subtotal = list.Sum(li => li.LineSubtotal);
        var perItemDiscount = UsesPerItemDiscount(discountMode) ? list.Sum(li => li.LineDiscountAmount) : 0m;
        var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
        var globalDiscount = UsesGlobalDiscount(discountMode)
            ? Math.Round(afterPerItem * (ClampDiscountPercent(globalDiscountPercent) / 100m), 2, MidpointRounding.AwayFromZero)
            : 0m;
        return (Math.Round(subtotal, 2, MidpointRounding.AwayFromZero), Math.Round(perItemDiscount + globalDiscount, 2, MidpointRounding.AwayFromZero));
    }

    private static bool IsPartLine(QuoteLineItemDto li) =>
        string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizeFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safe))
            return null;
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length > 256 ? safe[^256..] : safe;
    }

    private static bool IsTextUpload(string ext) =>
        ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ValidateTextUploadAsync(IFormFile file, CancellationToken ct)
    {
        var peekLength = (int)Math.Min(2048, file.Length);
        var buffer = new byte[peekLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, peekLength), ct);
        if (read == 0)
            return "Empty file.";
        var printable = 0;
        for (var i = 0; i < read; i++)
        {
            var b = buffer[i];
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126))
                printable++;
        }
        return printable >= Math.Max(1, read * 0.85) ? null : "File content does not look like readable text.";
    }

    private static async Task<string?> ExtractReadableTextAsync(string fullPath, string ext, CancellationToken ct)
    {
        try
        {
            if (IsTextUpload(ext))
            {
                var text = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8, ct);
                return NormalizeExtractedText(text);
            }

            if (!ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                using var document = PdfDocument.Open(fullPath);
                var text = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    text.AppendLine(ContentOrderTextExtractor.GetText(page));
                }
                var extracted = NormalizeExtractedText(text.ToString());
                if (!string.IsNullOrWhiteSpace(extracted))
                    return extracted;
            }
            catch
            {
                // Fall back to the raw PDF scan below for PDFs PdfPig cannot parse.
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath, ct);
            var raw = Encoding.Latin1.GetString(bytes);
            var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)]){2,})\)");
            var parts = matches
                .Select(m => Regex.Unescape(m.Groups["text"].Value))
                .Where(t => t.Any(char.IsLetterOrDigit))
                .Take(400);
            return NormalizeExtractedText(string.Join(" ", parts));
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeExtractedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var lines = text
            .Replace('\0', ' ')
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t]+", " ").Trim())
            .Where(line => line.Length > 0);
        var normalized = string.Join("\n", lines).Trim();
        return normalized.Length == 0 ? null : normalized[..Math.Min(normalized.Length, 4000)];
    }

    private static decimal? ExtractAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var preferredMatch = Regex.Match(text, @"(?ix)\b(?:balance\s+due|grand\s+total|quote\s+total|sub\s+total)\s*:?\s*(?:\n|\s)*R?\s*(?<amount>\d{1,3}(?:[,\s]\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))");
        if (preferredMatch.Success)
            return ParseMoney(preferredMatch.Groups["amount"].Value);
        var matches = Regex.Matches(text, @"(?ix)(?:total|amount\s*due|grand\s*total|quote\s*total)?\s*(?:R|ZAR)?\s*(?<amount>\d{1,3}(?:[,\s]\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))");
        var values = matches
            .Select(m => decimal.TryParse(m.Groups["amount"].Value.Replace(",", "").Replace(" ", ""), out var value) ? value : (decimal?)null)
            .Where(v => v.HasValue && v.Value >= 0)
            .Select(v => v!.Value)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    private static string? ExtractQuoteNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?i)\b(?:quote|quotation)\s*(?:no\.?|number|\#)?\s*[:#-]?\s*(?<number>[A-Z0-9][A-Z0-9\/\-_.]{2,})");
        return match.Success ? match.Groups["number"].Value.Trim()[..Math.Min(match.Groups["number"].Value.Trim().Length, 128)] : null;
    }

    private static string? ExtractSupplierName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parties = ExtractDocumentParties(text, null, null);
        if (!string.IsNullOrWhiteSpace(parties.sourceCompanyName))
            return parties.sourceCompanyName;
        var match = Regex.Match(text, @"(?im)^\s*(?<name>[A-Z][A-Za-z0-9&.,'()\- ]{2,80})\s*$");
        return match.Success ? match.Groups["name"].Value.Trim()[..Math.Min(match.Groups["name"].Value.Trim().Length, 256)] : null;
    }

    private static string NormalizeNameForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]+", "");
    }

    private static (string? sourceCompanyName, string? clientName, bool clientMatchesSelected) SplitPartyNames(string names, string? selectedClientName, string? selectedSourceCompanyName)
    {
        names = Regex.Replace(names, @"\s+", " ").Trim();
        var selectedName = selectedClientName?.Trim();
        var sourceName = selectedSourceCompanyName?.Trim();
        if (names.Length == 0)
            return (null, null, false);

        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            var sourceIdx = names.IndexOf(sourceName, StringComparison.OrdinalIgnoreCase);
            if (sourceIdx >= 0)
            {
                var source = names.Substring(sourceIdx, sourceName.Length).Trim();
                var clientCandidate = names[(sourceIdx + sourceName.Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(clientCandidate))
                {
                    var clientSplit = SplitPartyNames(clientCandidate, selectedClientName, null);
                    return (
                        source.Length > 0 ? source[..Math.Min(source.Length, 256)] : sourceName,
                        clientSplit.clientName ?? clientCandidate[..Math.Min(clientCandidate.Length, 256)],
                        clientSplit.clientMatchesSelected);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            var exactIdx = names.IndexOf(selectedName, StringComparison.OrdinalIgnoreCase);
            if (exactIdx >= 0)
            {
                var source = names[..exactIdx].Trim();
                var client = names.Substring(exactIdx, selectedName.Length).Trim();
                return (
                    source.Length > 0 ? source[..Math.Min(source.Length, 256)] : null,
                    client.Length > 0 ? client[..Math.Min(client.Length, 256)] : selectedName,
                    true);
            }

            var nameTokens = Regex.Matches(names.ToUpperInvariant(), @"[A-Z0-9]+").Cast<Match>().ToList();
            var selectedTokens = Regex.Matches(selectedName.ToUpperInvariant(), @"[A-Z0-9]+").Cast<Match>().Select(m => m.Value).ToList();
            if (selectedTokens.Count > 0 && nameTokens.Count >= selectedTokens.Count)
            {
                for (var i = 0; i <= nameTokens.Count - selectedTokens.Count; i++)
                {
                    var matched = true;
                    for (var j = 0; j < selectedTokens.Count; j++)
                    {
                        if (!nameTokens[i + j].Value.Equals(selectedTokens[j], StringComparison.OrdinalIgnoreCase))
                        {
                            matched = false;
                            break;
                        }
                    }
                    if (!matched)
                        continue;

                    var source = names[..nameTokens[i].Index].Trim();
                    return (
                        source.Length > 0 ? source[..Math.Min(source.Length, 256)] : null,
                        selectedName[..Math.Min(selectedName.Length, 256)],
                        true);
                }
            }
        }

        return (names[..Math.Min(names.Length, 256)], null, false);
    }

    private static (string? sourceCompanyName, string? clientName, bool clientMatchesSelected) ExtractDocumentParties(string? text, string? selectedClientName, string? selectedSourceCompanyName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null, false);

        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t]+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!Regex.IsMatch(lines[i], @"(?i)\bFROM\b.*\bTO\b"))
                continue;

            for (var j = i + 1; j < lines.Count; j++)
            {
                if (Regex.IsMatch(lines[j], @"(?i)\b(?:VAT\s+NO|CUSTOMER\s+VAT\s+NO|POSTAL\s+ADDRESS|PHYSICAL\s+ADDRESS)\b"))
                    break;
                var split = SplitPartyNames(lines[j], selectedClientName, selectedSourceCompanyName);
                if (!string.IsNullOrWhiteSpace(split.sourceCompanyName) || !string.IsNullOrWhiteSpace(split.clientName))
                    return split;
            }
        }

        var fromToMatch = Regex.Match(text, @"(?is)\bFROM\s+TO\s+(?<names>.+?)\s+(?:VAT\s+NO|CUSTOMER\s+VAT\s+NO)\s*:");
        return fromToMatch.Success
            ? SplitPartyNames(fromToMatch.Groups["names"].Value, selectedClientName, selectedSourceCompanyName)
            : (null, null, false);
    }

    private static DateTime? ExtractDueDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?ix)\b(?:due\s*date|valid\s*until)\s*:\s*(?<date>\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})");
        if (!match.Success)
            return null;
        var raw = match.Groups["date"].Value.Trim();
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "MM/dd/yyyy", "M/d/yyyy" };
        return DateTime.TryParseExact(raw, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value)
            ? value.Date
            : null;
    }

    private static string? ExtractReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?ix)\bREFERENCE\s*:\s*(?<reference>.+?)(?:\s+DATE\s*:|\s+DUE\s+DATE\s*:|\s+SALES\s+REP\s*:|$)");
        if (!match.Success)
            return null;
        var reference = match.Groups["reference"].Value.Trim();
        return reference.Length == 0 ? null : reference[..Math.Min(reference.Length, 256)];
    }

    private static decimal? ExtractOverallDiscountPercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?ix)\bOVERALL\s+DISCOUNT\s*%\s*:\s*(?<discount>\d+(?:\.\d+)?)\s*%");
        return match.Success ? ClampDiscountPercent(ParsePercent(match.Groups["discount"].Value)) : null;
    }

    private static decimal ParseMoney(string value) =>
        decimal.TryParse(value.Replace(",", "").Replace(" ", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static decimal ParsePercent(string value) =>
        decimal.TryParse(value.Replace("%", "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static List<QuoteUploadPreviewLineDto> ExtractQuoteLineItems(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<QuoteUploadPreviewLineDto>();

        var body = text;
        var headerIdx = body.IndexOf("Description Quantity", StringComparison.OrdinalIgnoreCase);
        if (headerIdx >= 0)
        {
            body = body[(headerIdx + "Description Quantity".Length)..];
            body = Regex.Replace(body, @"(?is)^\s*Excl\.\s*Price\s+Disc\s*%\s+VAT\s*%\s+Excl\.\s*Total\s+Incl\.\s*Total\s*", "");
        }
        var footerMatch = Regex.Match(body, @"(?ix)\b(?:Total\s+Discount|Total\s+Exclusive|Grand\s+Total|BALANCE\s+DUE|Nedbank|Branch\s+Code)\b");
        if (footerMatch.Success)
            body = body[..footerMatch.Index];

        var pattern = @"(?is)(?<description>.+?)\s+(?<quantity>\d+(?:\.\d+)?)\s+R(?<unitPrice>\d{1,3}(?:,\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))\s+(?<discount>\d+(?:\.\d+)?)%\s+(?<vat>\d+(?:\.\d+)?)%\s+R(?<excl>\d{1,3}(?:,\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))\s+R(?<incl>\d{1,3}(?:,\d{3})*(?:\.\d{2})|\d+(?:\.\d{2}))";
        return Regex.Matches(body, pattern)
            .Select(m =>
            {
                var fullDescription = Regex.Replace(m.Groups["description"].Value, @"\s+", " ").Trim();
                var code = "";
                var description = fullDescription;
                var codeMatch = Regex.Match(fullDescription, @"^(?<code>[A-Z0-9][A-Z0-9\/_.\-]{2,})\s*-\s*(?<desc>.+)$", RegexOptions.IgnoreCase);
                if (codeMatch.Success)
                {
                    code = codeMatch.Groups["code"].Value.Trim();
                    description = codeMatch.Groups["desc"].Value.Trim();
                }
                var lineType = fullDescription.Contains("labour", StringComparison.OrdinalIgnoreCase)
                    || fullDescription.Contains("transport", StringComparison.OrdinalIgnoreCase)
                    || fullDescription.Contains("transportation", StringComparison.OrdinalIgnoreCase)
                        ? "Labour"
                        : "Part";
                return new QuoteUploadPreviewLineDto
                {
                    LineType = lineType,
                    Code = code,
                    Description = description,
                    Quantity = ParseMoney(m.Groups["quantity"].Value),
                    UnitPrice = ParseMoney(m.Groups["unitPrice"].Value),
                    DiscountPercent = ParsePercent(m.Groups["discount"].Value),
                    VatPercent = ParsePercent(m.Groups["vat"].Value),
                    ExclTotal = ParseMoney(m.Groups["excl"].Value),
                    InclTotal = ParseMoney(m.Groups["incl"].Value),
                    MatchStatus = lineType == "Labour" ? "Manual" : "Unmatched"
                };
            })
            .Where(li => li.Quantity > 0)
            .ToList();
    }

    private static List<QuoteLineItemDto>? ParseUploadedQuoteLineItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<QuoteLineItemDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })?
            .Where(li => li.Quantity > 0 && li.UnitPrice >= 0)
            .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ValidateMissingUploadedQuoteItems(IEnumerable<QuoteLineItemDto>? lineItems)
    {
        var addMissingLines = (lineItems ?? Enumerable.Empty<QuoteLineItemDto>())
            .Where(li => li.AddMissingItemToSystem && !li.PartId.HasValue)
            .ToList();
        foreach (var line in addMissingLines)
        {
            if (string.IsNullOrWhiteSpace(line.Code))
                return "Each item selected to be added to the system needs a code.";
            if (string.IsNullOrWhiteSpace(line.Description))
                return "Each item selected to be added to the system needs a description/name.";
        }

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in addMissingLines.Select(li => li.Code?.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            if (!seenCodes.Add(code!))
                return $"Duplicate item code \"{code}\" cannot be added more than once.";
        }

        var seenDescriptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var description in addMissingLines.Select(li => NormalizeMissingItemKey(li.Description)).Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            if (!seenDescriptions.Add(description!))
                return $"Duplicate item description \"{description}\" cannot be added more than once.";
        }

        return null;
    }

    private static string? NormalizeMissingItemKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private async Task ApplyPartMatchesAsync(Guid clientId, List<QuoteUploadPreviewLineDto> lineItems, CancellationToken ct)
    {
        var client = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        var allowedCompanyIds = new List<Guid> { clientId };
        if (client?.ParentCompanyId is Guid parentCompanyId)
            allowedCompanyIds.Add(parentCompanyId);

        var parts = await _db.Parts.AsNoTracking()
            .Where(p => p.CompanyId.HasValue && allowedCompanyIds.Contains(p.CompanyId.Value))
            .Select(p => new { p.Id, p.Name, p.PartNumber, p.IsLabour })
            .ToListAsync(ct);

        foreach (var line in lineItems)
        {
            var shouldMatchLabour = !string.Equals(line.LineType, "Part", StringComparison.OrdinalIgnoreCase);
            var code = line.Code.Trim();
            var match = !string.IsNullOrWhiteSpace(code)
                ? parts.FirstOrDefault(p => p.IsLabour == shouldMatchLabour && !string.IsNullOrWhiteSpace(p.PartNumber) && p.PartNumber.Equals(code, StringComparison.OrdinalIgnoreCase))
                : null;
            match ??= parts.FirstOrDefault(p => p.IsLabour == shouldMatchLabour && p.Name.Equals(line.Description, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                continue;
            line.SuggestedPartId = match.Id;
            line.SuggestedPartName = match.Name;
            line.MatchStatus = "Matched";
        }
    }

    private async Task<HashSet<Guid>> CreateMissingUploadedQuotePartsAsync(Guid partCompanyId, IEnumerable<QuoteLineItemDto> lineItems, CancellationToken ct)
    {
        var linkedPartIds = new HashSet<Guid>();
        foreach (var line in lineItems.Where(li => li.AddMissingItemToSystem && !li.PartId.HasValue))
        {
            var name = (line.Description ?? string.Empty).Trim();
            var code = (line.Code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(code))
                continue;

            var isLabour = !IsPartLine(line);
            var existing = !string.IsNullOrWhiteSpace(code)
                ? await _db.Parts.FirstOrDefaultAsync(p =>
                    p.CompanyId == partCompanyId
                    && p.IsLabour == isLabour
                    && p.PartNumber != null
                    && p.PartNumber == code, ct)
                : null;
            existing ??= await _db.Parts.FirstOrDefaultAsync(p =>
                p.CompanyId == partCompanyId
                && p.IsLabour == isLabour
                && p.Name == (string.IsNullOrWhiteSpace(name) ? code : name), ct);

            if (existing != null)
            {
                line.PartId = existing.Id;
                linkedPartIds.Add(existing.Id);
                continue;
            }

            var part = new Part
            {
                Id = Guid.NewGuid(),
                CompanyId = partCompanyId,
                Name = string.IsNullOrWhiteSpace(name) ? code : name,
                Description = string.IsNullOrWhiteSpace(name) ? null : name,
                PartNumber = string.IsNullOrWhiteSpace(code) ? null : code,
                Quantity = 0,
                ReorderLevel = 0,
                UnitPrice = Math.Max(0, line.UnitPrice),
                IsLabour = isLabour,
                SupplierId = null,
                Unit = null,
                CreatedAt = DateTime.UtcNow
            };
            _db.Parts.Add(part);
            line.PartId = part.Id;
            linkedPartIds.Add(part.Id);
        }
        return linkedPartIds;
    }

    private async Task<HashSet<Guid>> GetAllowedPartIdsAsync(Guid quoteCompanyId, Guid? quoteCompanyParentId, IEnumerable<Guid> requestedPartIds, CancellationToken ct)
    {
        var partIds = requestedPartIds.Distinct().ToList();
        if (partIds.Count == 0) return new HashSet<Guid>();

        var allowedCompanyIds = new List<Guid> { quoteCompanyId };
        if (quoteCompanyParentId.HasValue)
            allowedCompanyIds.Add(quoteCompanyParentId.Value);

        var allowedIds = await _db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id)
                        && p.CompanyId.HasValue
                        && allowedCompanyIds.Contains(p.CompanyId.Value))
            .Select(p => p.Id)
            .ToListAsync(ct);

        return allowedIds.ToHashSet();
    }

    [HttpGet]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(List<QuoteDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<QuoteDto>>> List([FromQuery] Guid? clientId, [FromQuery] Guid? siteId, [FromQuery] string? status, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        IQueryable<Quote> query = _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .Include(q => q.Site);
        if (companyId.HasValue)
        {
            if (isClient)
                query = query.Where(q => q.CompanyId == companyId);
            else
                query = query.Where(q => q.Company != null && q.Company.ParentCompanyId == companyId);
        }
        if (clientId.HasValue)
            query = query.Where(q => q.CompanyId == clientId);
        if (siteId.HasValue)
            query = query.Where(q => q.SiteId == siteId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!QuoteStatus.IsValid(status))
                return BadRequest(ApiResponseBodies.Message($"Status must be one of: {string.Join(", ", QuoteStatus.All)}."));
            var normalized = QuoteStatus.Normalize(status);
            query = query.Where(q => q.Status == normalized);
        }

        var list = await query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new QuoteDto
            {
                Id = q.Id,
                QuoteNumber = q.QuoteNumber,
                ClientId = q.CompanyId,
                ClientName = q.Company != null ? q.Company.Name : null,
                SiteId = q.SiteId,
                SiteName = q.Site != null ? q.Site.Name : null,
                JobCardId = q.JobCardId,
                ServiceRequestId = q.ServiceRequestId,
                Amount = q.Amount,
                Currency = q.Currency,
                Description = q.Description,
                DeferPricing = q.DeferPricing,
                IsUploaded = q.IsUploaded,
                UploadedFileName = q.UploadedFileName,
                UploadedContentType = q.UploadedContentType,
                UploadedAt = q.UploadedAt,
                ExtractedQuoteNumber = q.ExtractedQuoteNumber,
                ExtractedSupplierName = q.ExtractedSupplierName,
                ExtractedText = q.ExtractedText,
                DiscountMode = q.DiscountMode,
                GlobalDiscountPercent = q.GlobalDiscountPercent,
                SubtotalAmount = q.Amount,
                DiscountAmount = 0,
                Notes = q.Notes,
                Status = q.Status,
                ValidUntil = q.ValidUntil,
                SentAt = q.SentAt,
                CreatedAt = q.CreatedAt,
                LinkedPurchaseOrderId = null,
                LinkedPurchaseOrderNumber = null
            }).ToListAsync();
        var quoteIds = list.Select(q => q.Id).ToList();
        var lineStats = await _db.QuoteLineItems.AsNoTracking()
            .Where(li => quoteIds.Contains(li.QuoteId))
            .GroupBy(li => li.QuoteId)
            .Select(g => new
            {
                QuoteId = g.Key,
                Subtotal = g.Sum(li => li.Quantity * li.UnitPrice),
                PerItemDiscount = g.Sum(li => (li.Quantity * li.UnitPrice) * (li.DiscountPercent / 100m))
            })
            .ToListAsync(ct);
        var lineStatsMap = lineStats.ToDictionary(x => x.QuoteId);
        var linkedPos = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.QuoteId != null && quoteIds.Contains(po.QuoteId.Value))
            .Select(po => new { po.QuoteId, po.Id, po.PONumber })
            .ToListAsync();
        foreach (var dto in list)
        {
            var mode = NormalizeDiscountMode(dto.DiscountMode);
            if (lineStatsMap.TryGetValue(dto.Id, out var stat))
            {
                var subtotal = Math.Round(stat.Subtotal, 2, MidpointRounding.AwayFromZero);
                var perItemDiscount = UsesPerItemDiscount(mode)
                    ? Math.Round(stat.PerItemDiscount, 2, MidpointRounding.AwayFromZero)
                    : 0m;
                var afterPerItem = Math.Max(0m, subtotal - perItemDiscount);
                var globalDiscount = UsesGlobalDiscount(mode)
                    ? Math.Round(afterPerItem * (ClampDiscountPercent(dto.GlobalDiscountPercent) / 100m), 2, MidpointRounding.AwayFromZero)
                    : 0m;
                dto.SubtotalAmount = subtotal;
                dto.DiscountAmount = Math.Round(perItemDiscount + globalDiscount, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                dto.SubtotalAmount = dto.Amount;
                dto.DiscountAmount = 0m;
            }

            var po = linkedPos.FirstOrDefault(p => p.QuoteId == dto.Id);
            if (po != null)
            {
                dto.LinkedPurchaseOrderId = po.Id;
                dto.LinkedPurchaseOrderNumber = po.PONumber;
            }
        }
        return Ok(list);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> Get(Guid id, CancellationToken ct = default)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var q = await _db.Quotes.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Site)
            .Include(x => x.LineItems)
            .ThenInclude(li => li.Part)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null)
            return NotFound();
        if (companyId.HasValue && q.Company != null &&
                (isClient ? q.CompanyId != companyId : q.Company.ParentCompanyId != companyId))
            return NotFound();
        var linkedPo = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.QuoteId == id)
            .Select(po => new { po.Id, po.PONumber })
            .FirstOrDefaultAsync();
        return Ok(MapToDto(q, linkedPo?.Id, linkedPo?.PONumber));
    }

    [HttpGet("{id:guid}/uploaded-file")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadedFile(Guid id, CancellationToken ct = default)
    {
        return await GetUploadedFileResult(id, inlinePreview: false, ct);
    }

    [HttpGet("{id:guid}/uploaded-file/preview")]
    [Authorize(Policy = "RequireViewPurchaseOrders")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewUploadedFile(Guid id, CancellationToken ct = default)
    {
        return await GetUploadedFileResult(id, inlinePreview: true, ct);
    }

    private async Task<IActionResult> GetUploadedFileResult(Guid id, bool inlinePreview, CancellationToken ct)
    {
        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var quote = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null || !quote.IsUploaded)
            return NotFound();
        if (companyId.HasValue && quote.Company != null &&
            (isClient ? quote.CompanyId != companyId : quote.Company.ParentCompanyId != companyId))
            return NotFound();

        var safePath = FilePathHelper.ValidateAndNormalize(quote.UploadedFilePath);
        if (safePath == null)
            return NotFound();
        var fullPath = Path.Combine(_env.ContentRootPath, safePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        var provider = new FileExtensionContentTypeProvider();
        var contentType = quote.UploadedContentType;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.Contains('/'))
            provider.TryGetContentType(fullPath, out contentType);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (inlinePreview)
            return File(stream, contentType ?? "application/octet-stream");
        return File(stream, contentType ?? "application/octet-stream", quote.UploadedFileName ?? Path.GetFileName(fullPath));
    }

    private static QuoteDto MapToDto(Quote q, Guid? linkedPoId, string? linkedPoNumber)
    {
        var lineItems = q.LineItems?.OrderBy(li => li.SortOrder).Select(li => new QuoteLineItemResponseDto
        {
            Id = li.Id,
            LineType = li.LineType,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            DiscountPercent = li.DiscountPercent,
            PartId = li.PartId,
            PartName = li.Part?.Name
        }).ToList() ?? new List<QuoteLineItemResponseDto>();
        var mode = NormalizeDiscountMode(q.DiscountMode);
        var (subtotal, discount) = ComputeQuoteSummary(lineItems, mode, q.GlobalDiscountPercent, q.Amount);
        return new QuoteDto
        {
            Id = q.Id,
            QuoteNumber = q.QuoteNumber,
            ClientId = q.CompanyId,
            ClientName = q.Company?.Name,
            SiteId = q.SiteId,
            SiteName = q.Site?.Name,
            JobCardId = q.JobCardId,
            ServiceRequestId = q.ServiceRequestId,
            Amount = q.Amount,
            Currency = q.Currency,
            Description = q.Description,
            DeferPricing = q.DeferPricing,
            IsUploaded = q.IsUploaded,
            UploadedFileName = q.UploadedFileName,
            UploadedContentType = q.UploadedContentType,
            UploadedAt = q.UploadedAt,
            ExtractedQuoteNumber = q.ExtractedQuoteNumber,
            ExtractedSupplierName = q.ExtractedSupplierName,
            ExtractedText = q.ExtractedText,
            DiscountMode = mode,
            GlobalDiscountPercent = ClampDiscountPercent(q.GlobalDiscountPercent),
            SubtotalAmount = subtotal,
            DiscountAmount = discount,
            Notes = q.Notes,
            Status = q.Status,
            ValidUntil = q.ValidUntil,
            SentAt = q.SentAt,
            CreatedAt = q.CreatedAt,
            LinkedPurchaseOrderId = linkedPoId,
            LinkedPurchaseOrderNumber = linkedPoNumber,
            LineItems = lineItems
        };
    }

    [HttpPost]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] CreateQuoteRequest request, CancellationToken ct)
    {
        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (company == null || site == null)
            return BadRequest(ApiResponseBodies.Message("Client or site not found."));
        if (site.CompanyId != request.ClientId)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : company.ParentCompanyId == scopeCompanyId;
            if (!inClientScope)
                return NotFound();
        }
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking()
                .Include(s => s.Site)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != request.SiteId || sr.Site.CompanyId != request.ClientId)
                return BadRequest(ApiResponseBodies.Message("Service request must belong to the selected client and site."));
        }
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job == null)
                return BadRequest(ApiResponseBodies.Message("Job card not found."));
            if (job.SiteId != request.SiteId)
                return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        }
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        var amount = request.Amount;
        var lineItems = request.LineItems ?? new List<QuoteLineItemDto>();
        var deferPricing = request.DeferPricing;
        var discountMode = NormalizeDiscountMode(request.DiscountMode);
        var globalDiscountPercent = ClampDiscountPercent(request.GlobalDiscountPercent);
        if (lineItems.Count > 0 && !deferPricing)
            amount = ComputeQuoteAmountFromLines(lineItems, discountMode, globalDiscountPercent);
        if (deferPricing && amount < 0) amount = 0;

        var requestedPartIds = lineItems
            .Where(li => IsPartLine(li) && li.PartId.HasValue && li.Quantity > 0)
            .Select(li => li.PartId!.Value)
            .ToList();
        var allowedPartIds = await GetAllowedPartIdsAsync(request.ClientId, company.ParentCompanyId, requestedPartIds, ct);
        var invalidPart = requestedPartIds.FirstOrDefault(id => !allowedPartIds.Contains(id));
        if (invalidPart != Guid.Empty)
            return BadRequest(ApiResponseBodies.Message("One or more selected parts are outside your tenant scope or invalid for this quote."));

        var quoteNumber = NumberGenerator.NextQuoteNumber(_db.Quotes);
        var q = new Quote
        {
            Id = Guid.NewGuid(),
            QuoteNumber = quoteNumber,
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            JobCardId = request.JobCardId,
            ServiceRequestId = request.ServiceRequestId,
            Amount = amount,
            Currency = request.Currency?.Trim() ?? "ZAR",
            Description = request.Description.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            DeferPricing = deferPricing,
            DiscountMode = discountMode,
            GlobalDiscountPercent = globalDiscountPercent,
            Status = QuoteStatus.Draft,
            ValidUntil = request.ValidUntil,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Quotes.Add(q);
        var sortOrder = 0;
        foreach (var li in lineItems)
        {
            if (li.Quantity <= 0) continue;
            var partId = IsPartLine(li) && li.PartId.HasValue && allowedPartIds.Contains(li.PartId.Value) ? li.PartId : null;
            _db.QuoteLineItems.Add(new QuoteLineItem
            {
                Id = Guid.NewGuid(),
                QuoteId = q.Id,
                LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                Description = li.Description?.Trim() ?? "",
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = ClampDiscountPercent(li.DiscountPercent),
                SortOrder = sortOrder++,
                PartId = partId
            });
            if (partId.HasValue)
            {
                var part = await _db.Parts.FindAsync([partId.Value], ct);
                if (part != null)
                {
                    part.UnitPrice = li.UnitPrice;
                    part.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        await _db.SaveChangesAsync(ct);

        if (q.JobCardId.HasValue)
        {
            await SyncPlannedPartsFromQuoteLineItemsAsync(q.JobCardId.Value, lineItems, ct);
            await _db.SaveChangesAsync(ct);
        }
        else if (q.ServiceRequestId.HasValue)
        {
            var relatedJobIds = await _db.JobCards.AsNoTracking()
                .Where(j => j.ServiceRequestId == q.ServiceRequestId.Value)
                .Select(j => j.Id)
                .ToListAsync(ct);
            foreach (var jobId in relatedJobIds)
                await SyncPlannedPartsFromQuoteLineItemsAsync(jobId, lineItems, ct);
            if (relatedJobIds.Count > 0)
                await _db.SaveChangesAsync(ct);
        }

        var loaded = await _db.Quotes.AsNoTracking()
            .Include(x => x.Company)
            .Include(x => x.Site)
            .Include(x => x.LineItems)
            .ThenInclude(li => li.Part)
            .FirstAsync(x => x.Id == q.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = q.Id }, MapToDto(loaded, null, null));
    }

    [HttpPost("upload")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> Upload([FromForm] UploadQuoteRequest request, CancellationToken ct)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponseBodies.Message("No quote file provided."));
        if (request.File.Length > MaxUploadedQuoteFileSizeBytes)
            return BadRequest(ApiResponseBodies.Message("File too large (max 10 MB)."));

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedUploadedQuoteExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Allowed quote file types: PDF, PNG, JPG, WEBP, TXT, CSV."));

        var sigErr = IsTextUpload(ext)
            ? await ValidateTextUploadAsync(request.File, ct)
            : await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(request.File, ext, ct);
        if (sigErr != null)
            return BadRequest(ApiResponseBodies.Message(sigErr));

        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (company == null || site == null)
            return BadRequest(ApiResponseBodies.Message("Client or site not found."));
        if (site.CompanyId != request.ClientId)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : company.ParentCompanyId == scopeCompanyId;
            if (!inClientScope)
                return NotFound();
        }
        if (request.ServiceRequestId.HasValue)
        {
            var sr = await _db.ServiceRequests.AsNoTracking()
                .Include(s => s.Site)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceRequestId.Value, ct);
            if (sr == null)
                return BadRequest(ApiResponseBodies.Message("Service request not found."));
            if (sr.SiteId != request.SiteId || sr.Site.CompanyId != request.ClientId)
                return BadRequest(ApiResponseBodies.Message("Service request must belong to the selected client and site."));
        }
        if (request.JobCardId.HasValue)
        {
            var job = await _db.JobCards.AsNoTracking().FirstOrDefaultAsync(j => j.Id == request.JobCardId.Value, ct);
            if (job == null)
                return BadRequest(ApiResponseBodies.Message("Job card not found."));
            if (job.SiteId != request.SiteId)
                return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        }

        var reviewedLineItems = ParseUploadedQuoteLineItems(request.LineItemsJson);
        var uploadedQuotePartIdsCreatedOrMatched = new HashSet<Guid>();
        if (reviewedLineItems?.Count > 0)
        {
            var missingItemValidation = ValidateMissingUploadedQuoteItems(reviewedLineItems);
            if (missingItemValidation != null)
                return BadRequest(ApiResponseBodies.Message(missingItemValidation));

            if (reviewedLineItems.Any(li => li.AddMissingItemToSystem && !li.PartId.HasValue))
            {
                if (isClientScope || !scopeCompanyId.HasValue)
                    return BadRequest(ApiResponseBodies.Message("Only admin users can add missing stock or non-stock items from an uploaded quote."));
                uploadedQuotePartIdsCreatedOrMatched = await CreateMissingUploadedQuotePartsAsync(scopeCompanyId.Value, reviewedLineItems, ct);
            }

            var requestedPartIds = reviewedLineItems
                .Select(li => li.PartId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
            var allowedPartIds = await GetAllowedPartIdsAsync(request.ClientId, company.ParentCompanyId, requestedPartIds, ct);
            allowedPartIds.UnionWith(uploadedQuotePartIdsCreatedOrMatched);
            foreach (var li in reviewedLineItems)
            {
                if (li.PartId.HasValue && !allowedPartIds.Contains(li.PartId.Value))
                    li.PartId = null;
            }
        }
        var quoteId = Guid.NewGuid();
        var safeOriginalName = SanitizeFileName(request.File.FileName) ?? $"uploaded-quote{ext}";
        var dir = Path.Combine(_env.ContentRootPath, QuoteUploadFolder);
        Directory.CreateDirectory(dir);
        var storedFileName = $"{quoteId:N}{ext}";
        var fullPath = Path.Combine(dir, storedFileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await request.File.CopyToAsync(stream, ct);
        }

        var extractedText = await ExtractReadableTextAsync(fullPath, ext, ct);
        var extractedAmount = ExtractAmount(extractedText);
        var extractedQuoteNumber = ExtractQuoteNumber(extractedText);
        var overallDiscountPercent = ClampDiscountPercent(request.GlobalDiscountPercent ?? ExtractOverallDiscountPercent(extractedText) ?? 0m);
        var hasOverallDiscount = overallDiscountPercent > 0m;
        var hasLineDiscounts = reviewedLineItems?.Any(li => ClampDiscountPercent(li.DiscountPercent) > 0m) == true;
        var uploadedDiscountMode = hasLineDiscounts && hasOverallDiscount
            ? DiscountModePerItemAndGlobal
            : hasLineDiscounts
                ? DiscountModePerItem
                : hasOverallDiscount
                    ? DiscountModeGlobal
                    : DiscountModeNone;
        var systemSourceCompanyName = !isClientScope && scopeCompanyId.HasValue
            ? await _db.Companies.AsNoTracking().Where(c => c.Id == scopeCompanyId.Value).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : company.ParentCompanyId.HasValue
                ? await _db.Companies.AsNoTracking().Where(c => c.Id == company.ParentCompanyId.Value).Select(c => c.Name).FirstOrDefaultAsync(ct)
                : null;
        var (sourceCompanyName, _, _) = ExtractDocumentParties(extractedText, company.Name, systemSourceCompanyName);
        var extractedSupplierName = sourceCompanyName ?? ExtractSupplierName(extractedText);
        var amount = request.Amount
            ?? extractedAmount
            ?? (reviewedLineItems?.Count > 0
                ? ComputeQuoteAmountFromLines(reviewedLineItems, uploadedDiscountMode, overallDiscountPercent)
                : 0m);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var relativePath = $"{QuoteUploadFolder}/{storedFileName}";
        var quote = new Quote
        {
            Id = quoteId,
            QuoteNumber = NumberGenerator.NextQuoteNumber(_db.Quotes),
            CompanyId = request.ClientId,
            SiteId = request.SiteId,
            JobCardId = request.JobCardId,
            ServiceRequestId = request.ServiceRequestId,
            Amount = amount,
            Currency = "ZAR",
            Description = !string.IsNullOrWhiteSpace(request.Description)
                ? request.Description.Trim()
                : extractedSupplierName != null
                ? $"Uploaded quote from {extractedSupplierName}"
                : $"Uploaded quote: {safeOriginalName}",
            Notes = !string.IsNullOrWhiteSpace(request.Notes)
                ? request.Notes.Trim()
                : extractedQuoteNumber != null ? $"Original quote number: {extractedQuoteNumber}" : null,
            Status = QuoteStatus.Draft,
            DeferPricing = reviewedLineItems?.Count > 0 ? false : !extractedAmount.HasValue,
            DiscountMode = uploadedDiscountMode,
            GlobalDiscountPercent = overallDiscountPercent,
            ValidUntil = request.ValidUntil?.Date ?? ExtractDueDate(extractedText),
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow,
            IsUploaded = true,
            UploadedFilePath = relativePath,
            UploadedFileName = safeOriginalName,
            UploadedContentType = request.File.ContentType,
            UploadedAt = DateTime.UtcNow,
            ExtractedQuoteNumber = extractedQuoteNumber,
            ExtractedSupplierName = extractedSupplierName,
            ExtractedText = extractedText
        };
        _db.Quotes.Add(quote);
        if (reviewedLineItems?.Count > 0)
        {
            var sortOrder = 0;
            foreach (var li in reviewedLineItems)
            {
                _db.QuoteLineItems.Add(new QuoteLineItem
                {
                    Id = Guid.NewGuid(),
                    QuoteId = quote.Id,
                    LineType = string.IsNullOrWhiteSpace(li.LineType) ? "Labour" : li.LineType.Trim(),
                    Description = li.Description?.Trim() ?? "",
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = ClampDiscountPercent(li.DiscountPercent),
                    SortOrder = sortOrder++,
                    PartId = li.PartId
                });
            }
        }
        await _db.SaveChangesAsync(ct);

        if (request.JobCardId.HasValue && reviewedLineItems?.Count > 0)
        {
            await SyncPlannedPartsFromQuoteLineItemsAsync(request.JobCardId.Value, reviewedLineItems, ct);
            await _db.SaveChangesAsync(ct);
        }

        var loaded = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .Include(q => q.Site)
            .Include(q => q.LineItems)
            .ThenInclude(li => li.Part)
            .FirstAsync(q => q.Id == quote.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = quote.Id }, MapToDto(loaded, null, null));
    }

    [HttpPost("upload-preview")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(QuoteUploadPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteUploadPreviewDto>> UploadPreview([FromForm] QuoteUploadPreviewRequest request, CancellationToken ct)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest(ApiResponseBodies.Message("No quote file provided."));
        if (request.File.Length > MaxUploadedQuoteFileSizeBytes)
            return BadRequest(ApiResponseBodies.Message("File too large (max 10 MB)."));

        var ext = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || !AllowedUploadedQuoteExtensions.Contains(ext))
            return BadRequest(ApiResponseBodies.Message("Allowed quote file types: PDF, PNG, JPG, WEBP, TXT, CSV."));

        var sigErr = IsTextUpload(ext)
            ? await ValidateTextUploadAsync(request.File, ct)
            : await FileContentSignatureHelper.ValidateContentMatchesExtensionAsync(request.File, ext, ct);
        if (sigErr != null)
            return BadRequest(ApiResponseBodies.Message(sigErr));

        var (scopeCompanyId, isClientScope) = await _currentUser.GetClientScopeAsync(ct);
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SiteId, ct);
        if (company == null || site == null)
            return BadRequest(ApiResponseBodies.Message("Client or site not found."));
        if (site.CompanyId != request.ClientId)
            return BadRequest(ApiResponseBodies.Message("Site must belong to the selected client."));
        if (scopeCompanyId.HasValue)
        {
            var inClientScope = isClientScope
                ? request.ClientId == scopeCompanyId
                : company.ParentCompanyId == scopeCompanyId;
            if (!inClientScope)
                return NotFound();
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{ext}");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await request.File.CopyToAsync(stream, ct);
            }

            var extractedText = await ExtractReadableTextAsync(tempPath, ext, ct);
            var extractedAmount = ExtractAmount(extractedText);
            var overallDiscountPercent = ExtractOverallDiscountPercent(extractedText);
            var extractedQuoteNumber = ExtractQuoteNumber(extractedText);
            var systemSourceCompanyName = !isClientScope && scopeCompanyId.HasValue
                ? await _db.Companies.AsNoTracking().Where(c => c.Id == scopeCompanyId.Value).Select(c => c.Name).FirstOrDefaultAsync(ct)
                : company.ParentCompanyId.HasValue
                    ? await _db.Companies.AsNoTracking().Where(c => c.Id == company.ParentCompanyId.Value).Select(c => c.Name).FirstOrDefaultAsync(ct)
                    : null;
            var (sourceCompanyName, extractedClientName, clientMatchesSelected) = ExtractDocumentParties(extractedText, company.Name, systemSourceCompanyName);
            var extractedSupplierName = sourceCompanyName ?? ExtractSupplierName(extractedText);
            var lineItems = ExtractQuoteLineItems(extractedText);
            await ApplyPartMatchesAsync(request.ClientId, lineItems, ct);

            return Ok(new QuoteUploadPreviewDto
            {
                UploadedFileName = SanitizeFileName(request.File.FileName) ?? request.File.FileName,
                ExtractedQuoteNumber = extractedQuoteNumber,
                ExtractedSupplierName = extractedSupplierName,
                ExtractedSourceCompanyName = sourceCompanyName,
                ExtractedClientName = extractedClientName,
                SelectedClientName = company.Name,
                ClientNameMatchesSelected = clientMatchesSelected,
                ExtractedText = extractedText,
                ExtractedAmount = extractedAmount,
                OverallDiscountPercent = overallDiscountPercent,
                Description = ExtractReference(extractedText)
                    ?? (extractedSupplierName != null ? $"Uploaded quote from {extractedSupplierName}" : $"Uploaded quote: {request.File.FileName}"),
                ValidUntil = ExtractDueDate(extractedText),
                LineItems = lineItems
            });
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
            catch
            {
                // Best-effort temp cleanup only.
            }
        }
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Send(Guid id, [FromQuery] string? toEmail, [FromQuery] bool attachPdf = true, CancellationToken ct = default)
    {
        var quote = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (quote == null)
            return NotFound();
        if (string.Equals(quote.Status, QuoteStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponseBodies.Message("Cancelled quotes cannot be sent to the client."));
        var sent = await _emailService.SendQuoteToClientAsync(id, toEmail, attachPdf, ct);
        if (!sent)
            return BadRequest(ApiResponseBodies.Message(
                "The quote email was not sent. Common causes: missing client contact email (or toEmail query), missing uploaded quote file, wrong email provider settings, or the email provider rejected the message — check API logs."));
        quote.SentAt = DateTime.UtcNow;
        quote.Status = QuoteStatus.Sent;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Client accepts a sent quote, or staff records acceptance (ManagePurchaseOrders).</summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = "RequireViewJobCards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return Unauthorized();

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        var canManage = await _permissionService.HasPermissionAsync(user, "ManagePurchaseOrders");

        var quote = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (quote == null)
            return NotFound();

        if (isClient)
        {
            if (!companyId.HasValue || quote.CompanyId != companyId.Value)
                return Forbid();
        }
        else if (!canManage)
        {
            return Forbid();
        }

        if (!_statusTransitions.TryTransitionQuote(quote.Status, QuoteStatus.Accepted, out var nextStatus, out var transitionError))
            return BadRequest(ApiResponseBodies.Message(transitionError));
        quote.Status = nextStatus;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> UpdateStatus(Guid id, [FromBody] UpdateQuoteStatusRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct);
        if (q == null)
            return NotFound();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!_statusTransitions.TryTransitionQuote(q.Status, request.Status, out var nextStatus, out var transitionError))
                return BadRequest(ApiResponseBodies.Message(transitionError));
            q.Status = nextStatus;
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuoteDto>> Update(Guid id, [FromBody] UpdateQuoteRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (q == null)
            return NotFound();
        if (q.IsUploaded)
            return BadRequest(ApiResponseBodies.Message("Uploaded quotes are read-only and cannot be edited."));
        var amount = request.Amount;
        var lineItems = request.LineItems ?? new List<QuoteLineItemDto>();
        var discountMode = NormalizeDiscountMode(request.DiscountMode);
        var globalDiscountPercent = ClampDiscountPercent(request.GlobalDiscountPercent);
        if (lineItems.Count > 0 && !q.DeferPricing)
            amount = ComputeQuoteAmountFromLines(lineItems, discountMode, globalDiscountPercent);

        if (request.LineItems != null)
        {
            var requestedPartIds = lineItems
                .Where(li => IsPartLine(li) && li.PartId.HasValue && li.Quantity > 0)
                .Select(li => li.PartId!.Value)
                .ToList();
            var allowedPartIds = await GetAllowedPartIdsAsync(q.CompanyId, q.Company?.ParentCompanyId, requestedPartIds, ct);
            var invalidPart = requestedPartIds.FirstOrDefault(id => !allowedPartIds.Contains(id));
            if (invalidPart != Guid.Empty)
                return BadRequest(ApiResponseBodies.Message("One or more selected parts are outside your tenant scope or invalid for this quote."));
        }
        q.Amount = amount;
        q.Currency = request.Currency?.Trim() ?? "ZAR";
        q.Description = request.Description.Trim();
        q.DiscountMode = discountMode;
        q.GlobalDiscountPercent = globalDiscountPercent;
        q.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        q.ValidUntil = request.ValidUntil;
        if (request.LineItems != null)
        {
            var allowedPartIds = await GetAllowedPartIdsAsync(q.CompanyId, q.Company?.ParentCompanyId,
                lineItems.Where(x => IsPartLine(x) && x.PartId.HasValue && x.Quantity > 0).Select(x => x.PartId!.Value), ct);
            _db.QuoteLineItems.RemoveRange(q.LineItems);
            var sortOrder = 0;
            foreach (var li in lineItems)
            {
                if (li.Quantity <= 0) continue;
                var partId = IsPartLine(li) && li.PartId.HasValue && allowedPartIds.Contains(li.PartId.Value) ? li.PartId : null;
                _db.QuoteLineItems.Add(new QuoteLineItem
                {
                    Id = Guid.NewGuid(),
                    QuoteId = id,
                    LineType = (li.LineType ?? "Labour").Trim().Length > 0 ? li.LineType!.Trim() : "Labour",
                    Description = li.Description?.Trim() ?? "",
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    DiscountPercent = ClampDiscountPercent(li.DiscountPercent),
                    SortOrder = sortOrder++,
                    PartId = partId
                });
            }
            if (q.JobCardId.HasValue)
                await SyncPlannedPartsFromQuoteLineItemsAsync(q.JobCardId.Value, lineItems, ct);
            else if (q.ServiceRequestId.HasValue)
            {
                var relatedJobIds = await _db.JobCards.AsNoTracking()
                    .Where(j => j.ServiceRequestId == q.ServiceRequestId.Value)
                    .Select(j => j.Id)
                    .ToListAsync(ct);
                foreach (var jobId in relatedJobIds)
                    await SyncPlannedPartsFromQuoteLineItemsAsync(jobId, lineItems, ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpPost("{id:guid}/link-job-card")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> LinkToJobCard(Guid id, [FromBody] LinkQuoteToJobCardRequest request, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (q == null)
            return NotFound();
        var job = await _db.JobCards.AsNoTracking().Include(j => j.Site).FirstOrDefaultAsync(j => j.Id == request.JobCardId, ct);
        if (job == null)
            return BadRequest(ApiResponseBodies.Message("Job card not found."));
        if (job.SiteId != q.SiteId)
            return BadRequest(ApiResponseBodies.Message("Quote site must match job card site."));
        q.JobCardId = request.JobCardId;
        await SyncPlannedPartsFromQuoteLineItemsAsync(request.JobCardId, q.LineItems.Select(li => new QuoteLineItemDto
        {
            LineType = li.LineType,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            DiscountPercent = li.DiscountPercent,
            PartId = li.PartId
        }), ct);
        await _db.SaveChangesAsync(ct);
        return await Get(id);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireManagePurchaseOrders")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var q = await LoadQuoteInScopeForUpdateAsync(id, ct, includeLineItems: true);
        if (q == null)
            return NotFound();

        if (q.JobCardId.HasValue)
        {
            var completedJob = await _db.JobCards.AsNoTracking()
                .AnyAsync(j => j.Id == q.JobCardId.Value
                    && (j.Status == JobCardStatus.Completed
                        || j.Status == JobCardStatus.Done
                        || j.Status == JobCardStatus.Closed), ct);
            if (completedJob)
                return BadRequest(ApiResponseBodies.Message("Cannot delete quote because it is linked to a completed or closed job."));
        }

        var linkedLockedInvoice = await _db.Invoices.AsNoTracking()
            .AnyAsync(i => i.QuoteId == id && i.Status != InvoiceStatus.Draft, ct);
        if (linkedLockedInvoice)
            return BadRequest(ApiResponseBodies.Message("Cannot delete quote because it is linked to a sent or paid invoice."));

        var linkedLockedPo = await _db.PurchaseOrders.AsNoTracking()
            .AnyAsync(po => po.QuoteId == id
                && po.Status != PurchaseOrderStatus.Draft
                && po.Status != PurchaseOrderStatus.Cancelled, ct);
        if (linkedLockedPo)
            return BadRequest(ApiResponseBodies.Message("Cannot delete quote because it is linked to a sent or ordered purchase order."));

        var safeInvoices = await _db.Invoices.Where(i => i.QuoteId == id).ToListAsync(ct);
        foreach (var invoice in safeInvoices)
            invoice.QuoteId = null;

        var safePos = await _db.PurchaseOrders.Where(po => po.QuoteId == id).ToListAsync(ct);
        foreach (var po in safePos)
            po.QuoteId = null;

        if (q.JobCardId.HasValue)
        {
            var quotePartIds = q.LineItems
                .Where(li => li.PartId.HasValue)
                .Select(li => li.PartId!.Value)
                .Distinct()
                .ToList();
            if (quotePartIds.Count > 0)
            {
                var plannedParts = await _db.JobCardPlannedParts
                    .Where(jpp => jpp.JobCardId == q.JobCardId.Value && quotePartIds.Contains(jpp.PartId))
                    .ToListAsync(ct);
                _db.JobCardPlannedParts.RemoveRange(plannedParts);
            }
        }

        var uploadedPath = q.UploadedFilePath;
        _db.QuoteLineItems.RemoveRange(q.LineItems);
        _db.Quotes.Remove(q);
        await _db.SaveChangesAsync(ct);
        DeleteUploadedFileBestEffort(uploadedPath, _env.ContentRootPath);
        return NoContent();
    }

    private static void DeleteUploadedFileBestEffort(string? relativePath, string rootPath)
    {
        var safePath = FilePathHelper.ValidateAndNormalize(relativePath);
        if (safePath == null)
            return;
        var fullPath = Path.Combine(rootPath, safePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }
        catch
        {
            // Best effort only; database delete must not fail because a file is locked.
        }
    }

    internal async Task SyncPlannedPartsFromQuoteLineItemsAsync(Guid jobCardId, IEnumerable<QuoteLineItemDto> lineItems, CancellationToken ct)
    {
        var lineList = lineItems?.ToList() ?? new List<QuoteLineItemDto>();
        if (lineList.Count == 0) return;

        var job = await _db.JobCards.AsNoTracking()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job?.Site == null) return;

        if (!job.Site.CompanyId.HasValue) return;
        var allowedCompanyIds = new HashSet<Guid> { job.Site.CompanyId.Value };
        if (job.Site.Company?.ParentCompanyId.HasValue == true)
            allowedCompanyIds.Add(job.Site.Company.ParentCompanyId.Value);

        var candidatePartIds = lineList
            .Where(li =>
                string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase)
                && li.PartId.HasValue
                && li.Quantity > 0)
            .Select(li => li.PartId!.Value)
            .Distinct()
            .ToList();
        if (candidatePartIds.Count == 0) return;

        var attachablePartIds = await _db.Parts.AsNoTracking()
            .Where(p =>
                candidatePartIds.Contains(p.Id)
                && p.CompanyId.HasValue
                && allowedCompanyIds.Contains(p.CompanyId.Value)
                && !p.IsLabour)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (attachablePartIds.Count == 0) return;
        var attachableSet = attachablePartIds.ToHashSet();

        var existingPlanned = await _db.JobCardPlannedParts
            .Where(jpp => jpp.JobCardId == jobCardId)
            .ToListAsync(ct);
        var plannedByPartId = existingPlanned.ToDictionary(jpp => jpp.PartId);
        var syncedAnyPart = false;

        var requiredQtyByPart = lineList
            .Where(li =>
                string.Equals(li.LineType, "Part", StringComparison.OrdinalIgnoreCase)
                && li.PartId.HasValue
                && li.Quantity > 0
                && attachableSet.Contains(li.PartId.Value))
            .GroupBy(li => li.PartId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(li => (int)Math.Max(1, Math.Ceiling(li.Quantity))));

        foreach (var kv in requiredQtyByPart)
        {
            var partId = kv.Key;
            var qty = kv.Value;
            syncedAnyPart = true;
            if (plannedByPartId.TryGetValue(partId, out var existing))
                existing.Quantity = Math.Max(existing.Quantity, qty);
            else
            {
                var planned = new JobCardPlannedPart
                {
                    Id = Guid.NewGuid(),
                    JobCardId = jobCardId,
                    PartId = partId,
                    Quantity = qty
                };
                _db.JobCardPlannedParts.Add(planned);
                plannedByPartId[partId] = planned;
            }
        }
        if (syncedAnyPart)
        {
            var jobForPartsFlag = await _db.JobCards.FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
            if (jobForPartsFlag != null)
                jobForPartsFlag.PartsRequired = true;
        }
    }

    private async Task<Quote?> LoadQuoteInScopeForUpdateAsync(Guid id, CancellationToken ct, bool includeLineItems = false)
    {
        IQueryable<Quote> query = _db.Quotes;
        if (includeLineItems)
            query = query.Include(q => q.LineItems);
        var quote = await query
            .Include(q => q.Company)
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote == null) return null;

        var (companyId, isClient) = await _currentUser.GetClientScopeAsync(ct);
        if (!companyId.HasValue) return quote;

        var inScope = await _scopeGuard.CanAccessCompanyAsync(quote.CompanyId, quote.Company, ct);
        return inScope ? quote : null;
    }
}
