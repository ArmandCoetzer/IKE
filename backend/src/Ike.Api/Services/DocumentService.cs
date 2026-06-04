using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Ike.Api.Controllers;
using Ike.Api.Data;
using Ike.Api.Helpers;
using Ike.Api.Models;

namespace Ike.Api.Services;

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public DocumentService(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<byte[]?> GetQuotePdfAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await _db.Quotes.AsNoTracking()
            .Include(q => q.Company)
            .Include(q => q.Site)
            .Include(q => q.LineItems).ThenInclude(li => li.Part)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct);
        if (quote == null)
            return null;
        var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                PdfTheme.ApplyA4PageDefaults(p);
                PdfTheme.RenderHeaderBand(p, logo, "Quote", quote.QuoteNumber ?? "—");
                p.Content().Column(c =>
                {
                    c.Spacing(10);
                    PdfTheme.RenderSummaryCard(c, box =>
                    {
                        box.Item().Text("Client: " + (quote.Company?.Name ?? "—"));
                        box.Item().Text("Site: " + (quote.Site?.Name ?? "—"));
                    });
                    if (!string.IsNullOrWhiteSpace(quote.Description))
                        c.Item().Text("Description: " + quote.Description);
                    if (quote.ValidUntil.HasValue)
                        c.Item().Text("Valid until: " + quote.ValidUntil.Value.ToString("d"));
                    if (quote.LineItems.Count > 0)
                    {
                        var discountMode = (quote.DiscountMode ?? "None").Trim().ToLowerInvariant();
                        var currencyPrefix = string.Equals(quote.Currency, "ZAR", StringComparison.OrdinalIgnoreCase) ? "R" : (quote.Currency + " ");
                        decimal LineSubtotal(QuoteLineItem li)
                        {
                            return Math.Round(Math.Max(0m, li.Quantity * li.UnitPrice), 2, MidpointRounding.AwayFromZero);
                        }
                        decimal LineDiscount(QuoteLineItem li)
                        {
                            if (discountMode == "peritem" || discountMode == "peritemandglobal")
                            {
                                var sub = LineSubtotal(li);
                                var pct = li.DiscountPercent < 0 ? 0 : (li.DiscountPercent > 100 ? 100 : li.DiscountPercent);
                                return Math.Round(sub * (pct / 100m), 2, MidpointRounding.AwayFromZero);
                            }
                            return 0m;
                        }
                        decimal LineNet(QuoteLineItem li)
                        {
                            return Math.Round(Math.Max(0m, LineSubtotal(li) - LineDiscount(li)), 2, MidpointRounding.AwayFromZero);
                        }
                        var subtotal = quote.LineItems.Sum(LineSubtotal);
                        var perItemDiscountTotal = quote.LineItems.Sum(LineDiscount);
                        var afterPerItem = Math.Round(Math.Max(0m, subtotal - perItemDiscountTotal), 2, MidpointRounding.AwayFromZero);
                        var globalDiscount = 0m;
                        if (discountMode == "global" || discountMode == "peritemandglobal")
                        {
                            var gp = quote.GlobalDiscountPercent < 0 ? 0 : (quote.GlobalDiscountPercent > 100 ? 100 : quote.GlobalDiscountPercent);
                            globalDiscount = Math.Round(afterPerItem * (gp / 100m), 2, MidpointRounding.AwayFromZero);
                        }
                        var totalDiscount = Math.Round(perItemDiscountTotal + globalDiscount, 2, MidpointRounding.AwayFromZero);
                        var showPerItemDiscountColumns = perItemDiscountTotal > 0m;
                        var finalTotal = Math.Round(Math.Max(0m, afterPerItem - globalDiscount), 2, MidpointRounding.AwayFromZero);
                        c.Item().PaddingTop(8).Text("Line items").Bold().FontSize(12);
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(d =>
                            {
                                d.RelativeColumn(3);
                                d.ConstantColumn(70);
                                d.ConstantColumn(70);
                                if (showPerItemDiscountColumns)
                                    d.ConstantColumn(60);
                                d.ConstantColumn(90);
                                if (showPerItemDiscountColumns)
                                    d.ConstantColumn(130);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Element(CellStyle).Text("Item");
                                h.Cell().Element(CellStyle).AlignRight().Text("Qty");
                                h.Cell().Element(CellStyle).AlignRight().Text("Unit");
                                if (showPerItemDiscountColumns)
                                    h.Cell().Element(CellStyle).AlignRight().Text("Disc %");
                                h.Cell().Element(CellStyle).AlignRight().Text("Total");
                                if (showPerItemDiscountColumns)
                                    h.Cell().Element(CellStyle).AlignRight().Text("Discounted total");
                            });
                            foreach (var li in quote.LineItems.OrderBy(li => li.SortOrder))
                            {
                                var name = !string.IsNullOrWhiteSpace(li.Description)
                                    ? li.Description
                                    : (li.Part?.Name ?? "—");
                                var lineSub = LineSubtotal(li);
                                var lineDisc = LineDiscount(li);
                                var lineNet = LineNet(li);
                                t.Cell().Element(CellStyle).Text(name);
                                t.Cell().Element(CellStyle).AlignRight().Text(li.Quantity.ToString("N2"));
                                t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{li.UnitPrice:N2}");
                                if (showPerItemDiscountColumns)
                                    t.Cell().Element(CellStyle).AlignRight().Text(li.DiscountPercent.ToString("N2"));
                                t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{lineSub:N2}");
                                if (showPerItemDiscountColumns)
                                    t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{lineNet:N2} (-{currencyPrefix}{lineDisc:N2})");
                            }
                        });
                        if (discountMode == "global")
                            c.Item().AlignRight().Text($"Global discount: {quote.GlobalDiscountPercent:N2}%").FontSize(9);
                        if (totalDiscount > 0)
                            c.Item().AlignRight().PaddingTop(4).Text($"Old total (before discount): {currencyPrefix}{subtotal:N2} (-{currencyPrefix}{totalDiscount:N2})").FontSize(9);
                        c.Item().AlignRight().Text($"Total: {currencyPrefix}{finalTotal:N2}").Bold().FontSize(11);
                    }
                });
                PdfTheme.RenderGeneratedFooter(p, "Generated");
            });
        });
        return await Task.Run(() => doc.GeneratePdf(), ct);
    }

    public async Task<byte[]?> GetInvoicePdfAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Company)
            .Include(i => i.Site)
            .Include(i => i.JobCard)
            .Include(i => i.LineItems).ThenInclude(li => li.Part)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null)
            return null;
        var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                PdfTheme.ApplyA4PageDefaults(p);
                PdfTheme.RenderHeaderBand(p, logo, "Invoice", invoice.InvoiceNumber ?? "—");
                p.Content().Column(c =>
                {
                    c.Spacing(10);
                    PdfTheme.RenderSummaryCard(c, box =>
                    {
                        box.Item().Text("Client: " + (invoice.Company?.Name ?? "—"));
                        box.Item().Text("Site: " + (invoice.Site?.Name ?? "—"));
                        box.Item().Text("Job: " + (invoice.JobCard?.JobCardNumber ?? "—"));
                        box.Item().Text("Due: " + invoice.DueDate.ToString("d"));
                    });
                    var currencyPrefix = string.Equals(invoice.Currency, "ZAR", StringComparison.OrdinalIgnoreCase) ? "R" : (invoice.Currency + " ");
                    decimal finalTotal = Math.Round(Math.Max(0m, invoice.Amount), 2, MidpointRounding.AwayFromZero);
                    if (invoice.LineItems.Count > 0)
                    {
                        decimal LineSubtotal(InvoiceLineItem li) => Math.Round(Math.Max(0m, li.Quantity * li.UnitPrice), 2, MidpointRounding.AwayFromZero);
                        decimal LineDiscount(InvoiceLineItem li)
                        {
                            var pct = li.DiscountPercent < 0 ? 0 : (li.DiscountPercent > 100 ? 100 : li.DiscountPercent);
                            return Math.Round(LineSubtotal(li) * (pct / 100m), 2, MidpointRounding.AwayFromZero);
                        }
                        decimal LineTotal(InvoiceLineItem li) => Math.Round(Math.Max(0m, LineSubtotal(li) - LineDiscount(li)), 2, MidpointRounding.AwayFromZero);
                        var subtotal = invoice.LineItems.Sum(LineSubtotal);
                        var discountTotal = invoice.LineItems.Sum(LineDiscount);
                        var showDiscountColumns = discountTotal > 0m;
                        var lineTotal = invoice.LineItems.Sum(LineTotal);
                        finalTotal = Math.Round(Math.Max(0m, lineTotal), 2, MidpointRounding.AwayFromZero);
                        c.Item().PaddingTop(8).Text("Line items").Bold().FontSize(12);
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(d =>
                            {
                                d.RelativeColumn(3);
                                d.ConstantColumn(70);
                                d.ConstantColumn(70);
                                if (showDiscountColumns)
                                    d.ConstantColumn(60);
                                d.ConstantColumn(90);
                                if (showDiscountColumns)
                                    d.ConstantColumn(130);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Element(CellStyle).Text("Item");
                                h.Cell().Element(CellStyle).AlignRight().Text("Qty");
                                h.Cell().Element(CellStyle).AlignRight().Text("Unit");
                                if (showDiscountColumns)
                                    h.Cell().Element(CellStyle).AlignRight().Text("Disc %");
                                h.Cell().Element(CellStyle).AlignRight().Text("Total");
                                if (showDiscountColumns)
                                    h.Cell().Element(CellStyle).AlignRight().Text("Discounted total");
                            });
                            foreach (var li in invoice.LineItems.OrderBy(li => li.SortOrder))
                            {
                                var name = !string.IsNullOrWhiteSpace(li.Description)
                                    ? li.Description
                                    : (li.Part?.Name ?? "—");
                                var sub = LineSubtotal(li);
                                var disc = LineDiscount(li);
                                t.Cell().Element(CellStyle).Text(name);
                                t.Cell().Element(CellStyle).AlignRight().Text(li.Quantity.ToString("N2"));
                                t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{li.UnitPrice:N2}");
                                if (showDiscountColumns)
                                    t.Cell().Element(CellStyle).AlignRight().Text(li.DiscountPercent.ToString("N2"));
                                t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{sub:N2}");
                                if (showDiscountColumns)
                                    t.Cell().Element(CellStyle).AlignRight().Text($"{currencyPrefix}{LineTotal(li):N2} (-{currencyPrefix}{disc:N2})");
                            }
                        });
                        if (discountTotal > 0)
                            c.Item().AlignRight().PaddingTop(4).Text($"Old total (before discount): {currencyPrefix}{subtotal:N2} (-{currencyPrefix}{discountTotal:N2})").FontSize(9);
                    }
                    c.Item().AlignRight().Text($"Total: {currencyPrefix}{finalTotal:N2}").Bold().FontSize(11);
                });
                PdfTheme.RenderGeneratedFooter(p, "Generated");
            });
        });
        return await Task.Run(() => doc.GeneratePdf(), ct);
    }

    public async Task<byte[]?> GetPurchaseOrderPdfAsync(Guid purchaseOrderId, CancellationToken ct = default)
    {
        var po = await _db.PurchaseOrders.AsNoTracking()
            .Include(p => p.Company)
            .Include(p => p.Site)
            .Include(p => p.Quote)
            .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct);
        if (po == null)
            return null;
        var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                PdfTheme.ApplyA4PageDefaults(p);
                PdfTheme.RenderHeaderBand(p, logo, "Purchase order", po.PONumber ?? "—");
                p.Content().Column(c =>
                {
                    c.Spacing(10);
                    PdfTheme.RenderSummaryCard(c, box =>
                    {
                        box.Item().Text("Client: " + (po.Company?.Name ?? "—"));
                        box.Item().Text("Site: " + (po.Site?.Name ?? "—"));
                        box.Item().Text("Status: " + po.Status);
                        if (po.Quote != null)
                            box.Item().Text("Related quote: " + (po.Quote.QuoteNumber ?? "—"));
                        box.Item().Text($"Amount: {po.Currency} {po.Amount:N2}").Bold();
                    });
                    if (!string.IsNullOrEmpty(po.ClientPONumber))
                        c.Item().Text("Client PO #: " + po.ClientPONumber);
                    if (!string.IsNullOrWhiteSpace(po.Notes))
                        c.Item().Text("Notes: " + po.Notes);
                });
                PdfTheme.RenderGeneratedFooter(p, "Generated");
            });
        });
        return await Task.Run(() => doc.GeneratePdf(), ct);
    }

    public async Task<byte[]?> GetJobCardPdfAsync(Guid jobCardId, CancellationToken ct = default)
    {
        var job = await _db.JobCards.AsNoTracking()
            .AsSplitQuery()
            .Include(j => j.Site).ThenInclude(s => s!.Company)
            .Include(j => j.ServiceRequest)
            .Include(j => j.RequiredPermitType)
            .Include(j => j.CreatedByUser)
            .Include(j => j.Assignments).ThenInclude(a => a.User)
            .Include(j => j.ActiveJobPermit).ThenInclude(p => p!.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(j => j.PlannedParts).ThenInclude(jpp => jpp.Part)
            .Include(j => j.Parts).ThenInclude(p => p.CreatedByUser)
            .Include(j => j.Permits).ThenInclude(p => p.PermitTemplate).ThenInclude(t => t!.PermitType)
            .Include(j => j.Documents).ThenInclude(d => d.SignedByUser)
            .Include(j => j.Documents).ThenInclude(d => d.PurchaseOrder)
            .Include(j => j.IncidentReports).ThenInclude(ir => ir.ReportedByUser)
            .FirstOrDefaultAsync(j => j.Id == jobCardId, ct);
        if (job == null)
            return null;

        var quoteByJob = await _db.Quotes.AsNoTracking()
            .Where(qu => qu.JobCardId == jobCardId)
            .OrderByDescending(qu => qu.CreatedAt)
            .FirstOrDefaultAsync(ct);
        Quote? quote = quoteByJob;
        if (quote == null && job.ServiceRequestId.HasValue)
        {
            quote = await _db.Quotes.AsNoTracking()
                .Where(qu => qu.ServiceRequestId == job.ServiceRequestId.Value)
                .OrderByDescending(qu => qu.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        var invoice = await _db.Invoices.AsNoTracking()
            .Where(i => i.JobCardId == jobCardId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var purchaseOrders = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.JobCardId == jobCardId)
            .OrderByDescending(po => po.CreatedAt)
            .ToListAsync(ct);

        var visiblePermits = PaperPermitModeHelper.VisiblePermits(job.Permits).OrderByDescending(p => p.PermitNumber).ToList();

        byte[]? finalClientSignOffImage = null;
        string? finalClientSignOffNote = null;
        var finalSignDoc = job.Documents
            .Where(d => string.Equals(d.DocumentType, JobCardFinalSignOffHelper.DocumentType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.SignedAt)
            .FirstOrDefault();
        if (finalSignDoc?.FilePath != null)
        {
            finalClientSignOffNote = string.IsNullOrWhiteSpace(finalSignDoc.Notes) ? null : finalSignDoc.Notes.Trim();
            var rel = FilePathHelper.ValidateAndNormalize(finalSignDoc.FilePath);
            if (rel != null)
            {
                var fullSignPath = Path.Combine(_env.ContentRootPath, rel);
                if (File.Exists(fullSignPath))
                {
                    var ext = Path.GetExtension(fullSignPath).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg")
                        finalClientSignOffImage = await File.ReadAllBytesAsync(fullSignPath, ct);
                }
            }
        }

        var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(1.5f, Unit.Centimetre);
                PdfTheme.RenderHeaderBand(p, logo, "Job card", job.JobCardNumber);
                p.Content().Column(c =>
                {
                    c.Spacing(8);
                    c.Item().Text("Reference ID: " + job.Id).FontSize(8).FontColor(Colors.Grey.Darken2);
                    c.Item().Background(Colors.Grey.Lighten3).Padding(12).Column(box =>
                    {
                        box.Item().Text("Summary").Bold().FontSize(12);
                        box.Item().Text("Status: " + (job.Status ?? "—"));
                        box.Item().Text("Priority: P" + job.Priority);
                        if (job.DueDate.HasValue)
                            box.Item().Text("Due: " + job.DueDate.Value.ToString("d", CultureInfo.CurrentCulture));
                        box.Item().Text("Permits required: " + (job.PermitsRequired ? "Yes" : "No"));
                        if (job.PermitsRequired && job.RequiredPermitType != null)
                            box.Item().Text("Required permit type: " + (job.RequiredPermitType.Name ?? "—")).FontSize(9);
                        box.Item().Text("Parts / stock planned: " + (job.PartsRequired ? "Yes" : "No"));
                        if (job.PaperPermitMode)
                            box.Item().Text("Paper permit mode: Yes").SemiBold();
                        if (!string.IsNullOrWhiteSpace(job.BlockedReason))
                        {
                            box.Item().Text("Blocked: " + job.BlockedReason).FontColor(Colors.Red.Darken2);
                            if (job.BlockedAt.HasValue)
                                box.Item().Text("Blocked at: " + job.BlockedAt.Value.ToString("u") + " UTC").FontSize(8);
                        }
                    });

                    var activeName = PermitTemplateDurationHelper.PrimaryDisplayName(job.ActiveJobPermit?.PermitTemplate);
                    if (!string.IsNullOrWhiteSpace(activeName))
                    {
                        c.Item().PaddingTop(4).Text("Active permit (on site)").Bold().FontSize(12);
                        c.Item().Text(activeName);
                    }

                    c.Item().PaddingTop(8).Text("Location & client").Bold().FontSize(12);
                    c.Item().Text("Site: " + (job.Site?.Name ?? "—"));
                    c.Item().Text("Client: " + (job.Site?.Company?.Name ?? "—"));
                    if (job.Site != null && !string.IsNullOrWhiteSpace(job.Site.Address))
                        c.Item().Text("Address: " + job.Site.Address).FontSize(9).FontColor(Colors.Grey.Darken2);
                    if (job.Site?.Latitude is { } lat && job.Site.Longitude is { } lon)
                        c.Item().Text($"Coordinates: {lat.ToString("0.######", CultureInfo.InvariantCulture)}, {lon.ToString("0.######", CultureInfo.InvariantCulture)}")
                            .FontSize(8).FontColor(Colors.Grey.Darken2);

                    if (job.ServiceRequest != null)
                    {
                        c.Item().PaddingTop(8).Text("Service request").Bold().FontSize(12);
                        c.Item().Text(job.ServiceRequest.RequestNumber +
                                       (string.IsNullOrWhiteSpace(job.ServiceRequest.Description) ? "" : " — " + job.ServiceRequest.Description));
                    }

                    if (!string.IsNullOrWhiteSpace(job.Description))
                    {
                        c.Item().PaddingTop(8).Text("Job description").Bold().FontSize(12);
                        c.Item().Text(job.Description);
                    }

                    c.Item().PaddingTop(8).Text("Commercial").Bold().FontSize(12);
                    if (quote != null)
                    {
                        c.Item().Text($"Quote: {quote.QuoteNumber} — {quote.Currency} {quote.Amount:N2} ({quote.Status})");
                        if (!string.IsNullOrWhiteSpace(quote.Description))
                            c.Item().Text(TruncateForPdf(quote.Description, 280)).FontSize(9).FontColor(Colors.Grey.Darken2);
                        if (quote.ValidUntil.HasValue)
                            c.Item().Text("Quote valid until: " + quote.ValidUntil.Value.ToString("d")).FontSize(9);
                    }
                    else
                        c.Item().Text("Quote: —").FontSize(9).FontColor(Colors.Grey.Darken2);

                    if (purchaseOrders.Count > 0)
                    {
                        c.Item().PaddingTop(4).Text("Purchase orders").SemiBold().FontSize(10);
                        foreach (var po in purchaseOrders)
                        {
                            var line = $"{po.PONumber} — {po.Currency} {po.Amount:N2} ({po.Status})";
                            if (!string.IsNullOrWhiteSpace(po.ClientPONumber))
                                line += $" — Client PO: {po.ClientPONumber}";
                            c.Item().Text(line).FontSize(9);
                        }
                    }

                    if (invoice != null)
                    {
                        c.Item().PaddingTop(4).Text(
                            $"Invoice: {invoice.InvoiceNumber} — {invoice.Currency} {invoice.Amount:N2} — {invoice.Status} — Due {invoice.DueDate:d}");
                    }

                    c.Item().PaddingTop(8).Text("Team").Bold().FontSize(12);
                    if (job.Assignments.Count == 0)
                        c.Item().Text("No technicians assigned.").FontSize(9).FontColor(Colors.Grey.Darken2);
                    else
                    {
                        foreach (var a in job.Assignments.OrderByDescending(x => x.IsPermitManager).ThenBy(x => FormatUserDisplay(x.User)))
                        {
                            var role = a.IsPermitManager ? " (permit manager)" : "";
                            c.Item().Text($"{FormatUserDisplay(a.User)}{role} — assigned {a.AssignedAt:yyyy-MM-dd}").FontSize(9);
                        }
                    }

                    if (job.PlannedParts.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Planned parts").Bold().FontSize(12);
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(d =>
                            {
                                d.RelativeColumn(3);
                                d.ConstantColumn(50);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Element(JobCardPdfCellStyle).Text("Part").SemiBold();
                                h.Cell().Element(JobCardPdfCellStyle).AlignRight().Text("Qty").SemiBold();
                            });
                            foreach (var jpp in job.PlannedParts.OrderBy(x => x.Part?.Name))
                            {
                                t.Cell().Element(JobCardPdfCellStyle).Text(jpp.Part?.Name ?? "—");
                                t.Cell().Element(JobCardPdfCellStyle).AlignRight().Text(jpp.Quantity.ToString(CultureInfo.InvariantCulture));
                            }
                        });
                    }

                    if (job.Parts.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Parts used / recorded").Bold().FontSize(12);
                        foreach (var part in job.Parts.OrderByDescending(x => x.CreatedAt))
                        {
                            var who = FormatUserDisplay(part.CreatedByUser);
                            var sn = string.IsNullOrWhiteSpace(part.SerialNumber) ? "" : $" — S/N {part.SerialNumber}";
                            var desc = string.IsNullOrWhiteSpace(part.Description) ? "" : $" — {part.Description}";
                            c.Item().Text($"{part.Brand}{sn}{desc} — recorded {part.CreatedAt:yyyy-MM-dd} by {who}").FontSize(9);
                        }
                    }

                    if (visiblePermits.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Permits").Bold().FontSize(12);
                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(d =>
                            {
                                d.ConstantColumn(36);
                                d.RelativeColumn(3);
                                d.ConstantColumn(72);
                                d.RelativeColumn(2);
                            });
                            t.Header(h =>
                            {
                                h.Cell().Element(JobCardPdfCellStyle).Text("#").SemiBold();
                                h.Cell().Element(JobCardPdfCellStyle).Text("Type").SemiBold();
                                h.Cell().Element(JobCardPdfCellStyle).Text("Status").SemiBold();
                                h.Cell().Element(JobCardPdfCellStyle).Text("Notes").SemiBold();
                            });
                            foreach (var permit in visiblePermits)
                            {
                                var name = PermitTemplateDurationHelper.PrimaryDisplayName(permit.PermitTemplate) ?? "Permit";
                                var notes = new List<string>();
                                if (job.PaperPermitMode && !string.IsNullOrWhiteSpace(permit.PaperPermitNumber))
                                    notes.Add("Paper ref: " + permit.PaperPermitNumber);
                                if (permit.ValidTo.HasValue)
                                    notes.Add("Valid to: " + permit.ValidTo.Value.ToString("yyyy-MM-dd HH:mm") + " UTC");
                                t.Cell().Element(JobCardPdfCellStyle).Text(permit.PermitNumber.ToString(CultureInfo.InvariantCulture));
                                t.Cell().Element(JobCardPdfCellStyle).Text(name);
                                t.Cell().Element(JobCardPdfCellStyle).Text(permit.Status ?? "—");
                                t.Cell().Element(JobCardPdfCellStyle).Text(notes.Count > 0 ? string.Join(" · ", notes) : "—");
                            }
                        });
                    }

                    var docsForList = job.Documents
                        .Where(d => !string.Equals(d.DocumentType, JobCardFinalSignOffHelper.DocumentType, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.SignedAt)
                        .ToList();
                    if (docsForList.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Site photos & documents").Bold().FontSize(12);
                        foreach (var d in docsForList)
                        {
                            var poRef = d.PurchaseOrder?.PONumber;
                            var poTxt = string.IsNullOrWhiteSpace(poRef) ? "" : $" — PO {poRef}";
                            var by = FormatUserDisplay(d.SignedByUser);
                            var note = string.IsNullOrWhiteSpace(d.Notes) ? "" : $" — {TruncateForPdf(d.Notes, 120)}";
                            c.Item().Text($"{d.DocumentType} @ {d.SignedAt:yyyy-MM-dd HH:mm} UTC — {by}{poTxt}{note}").FontSize(8).FontColor(Colors.Grey.Darken2);
                        }
                    }

                    if (finalClientSignOffImage is { Length: > 0 })
                    {
                        c.Item().PaddingTop(10).Text("Final client sign-off").Bold().FontSize(12);
                        if (!string.IsNullOrWhiteSpace(finalClientSignOffNote))
                            c.Item().Text("Client print name: " + TruncateForPdf(finalClientSignOffNote, 200)).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(job.FinalClientSignOffCaptureSource))
                            c.Item().Text("Capture source: " + job.FinalClientSignOffCaptureSource).FontSize(8).FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrWhiteSpace(job.FinalClientSignOffFileSha256))
                            c.Item().Text("Evidence SHA-256: " + job.FinalClientSignOffFileSha256).FontSize(8).FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrWhiteSpace(job.FinalClientSignOffEvidenceHash))
                            c.Item().Text("Evidence chain hash: " + job.FinalClientSignOffEvidenceHash).FontSize(8).FontColor(Colors.Grey.Darken2);
                        if (job.FinalClientSignOffEvidenceRecordedAt.HasValue)
                            c.Item().Text("Evidence recorded: " + job.FinalClientSignOffEvidenceRecordedAt.Value.ToString("u") + " UTC").FontSize(8).FontColor(Colors.Grey.Darken2);
                        c.Item().PaddingTop(6).Height(140).Image(finalClientSignOffImage).FitArea();
                    }

                    if (job.IncidentReports.Count > 0)
                    {
                        c.Item().PaddingTop(8).Text("Incident reports").Bold().FontSize(12);
                        foreach (var ir in job.IncidentReports.OrderByDescending(x => x.CreatedAt))
                        {
                            var who = FormatUserDisplay(ir.ReportedByUser);
                            c.Item().Text($"{ir.CreatedAt:yyyy-MM-dd} — {ir.Severity} / {ir.Status} — {who}").SemiBold().FontSize(9);
                            c.Item().Text(TruncateForPdf(ir.Description, 400)).FontSize(8).FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(ir.Resolution))
                                c.Item().Text("Resolution: " + TruncateForPdf(ir.Resolution, 200)).FontSize(8);
                        }
                    }

                    c.Item().PaddingTop(12).Text("Record").Bold().FontSize(12);
                    c.Item().Text("Created: " + job.CreatedAt.ToString("u") + " UTC by " + FormatUserDisplay(job.CreatedByUser)).FontSize(9);
                    if (job.UpdatedAt.HasValue)
                        c.Item().Text("Last updated: " + job.UpdatedAt.Value.ToString("u") + " UTC").FontSize(9);
                });
                PdfTheme.RenderGeneratedFooter(p, "Job card export.");
            });
        });
        return await Task.Run(() => doc.GeneratePdf(), ct);
    }

    private static string FormatUserDisplay(ApplicationUser? user) =>
        user == null
            ? "—"
            : (string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? user.Id) : user.FullName);

    private static string TruncateForPdf(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Trim();
        return t.Length <= maxLen ? t : t[..maxLen] + "…";
    }

    private static IContainer JobCardPdfCellStyle(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8)).Padding(3);

    public async Task<byte[]> GetProgressReportPdfAsync(ProgressReportDto report, CancellationToken ct = default)
    {
        var logo = PdfTheme.LoadPrimaryLogoBytes(_env);
        var doc = Document.Create(container =>
        {
            container.Page(p =>
            {
                PdfTheme.ApplyA4PageDefaults(p);
                PdfTheme.RenderHeaderBand(p, logo, "Report", "Progress report");
                p.Content().Column(c =>
                {
                    c.Spacing(12);
                    c.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(10);
                    c.Item().Text($"Total labour hours: {report.TotalLabourHours:N2}").Bold();
                    c.Item().Text($"Total amount: ZAR {report.TotalAmount:N2}").Bold();
                    if (report.Budget != null && report.Budget.ThresholdAmount > 0)
                    {
                        var budget = report.Budget;
                        var pct = Math.Min(100m, (budget.SpentAmount / budget.ThresholdAmount) * 100);
                        c.Item().PaddingTop(8).Text($"Budget: {budget.Currency} {budget.SpentAmount:N2} / {budget.ThresholdAmount:N2} ({pct:N0}%)");
                        if (budget.WorkPaused)
                            c.Item().Text("Work paused – approval needed").FontColor(Colors.Orange.Darken2);
                    }
                    c.Item().PaddingTop(8).Table(t =>
                    {
                        t.ColumnsDefinition(d =>
                        {
                            d.ConstantColumn(80);
                            d.RelativeColumn(2);
                            d.RelativeColumn(2);
                            d.ConstantColumn(60);
                            d.ConstantColumn(70);
                            d.ConstantColumn(80);
                        });
                        t.Header(h =>
                        {
                            h.Cell().Element(CellStyle).Text("Job #");
                            h.Cell().Element(CellStyle).Text("Request #");
                            h.Cell().Element(CellStyle).Text("Site");
                            h.Cell().Element(CellStyle).Text("Status");
                            h.Cell().Element(CellStyle).Text("Hrs");
                            h.Cell().Element(CellStyle).Text("Amount");
                        });
                        foreach (var i in report.Items)
                        {
                            t.Cell().Element(CellStyle).Text(i.JobCardNumber);
                            t.Cell().Element(CellStyle).Text(i.ServiceRequestNumber ?? "—");
                            t.Cell().Element(CellStyle).Text(i.SiteName ?? "—");
                            t.Cell().Element(CellStyle).Text(i.Status);
                            t.Cell().Element(CellStyle).Text(i.LabourHours.ToString("N2"));
                            t.Cell().Element(CellStyle).Text(i.InvoiceAmount.ToString("N2"));
                        }
                    });
                });
                PdfTheme.RenderGeneratedFooter(p, "Generated");
            });
        });
        return await Task.Run(() => doc.GeneratePdf(), ct);
    }

    static IContainer CellStyle(IContainer c) => c.DefaultTextStyle(x => x.FontSize(9)).Padding(4);
}
