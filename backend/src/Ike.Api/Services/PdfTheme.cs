using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Ike.Api.Services;

public static class PdfTheme
{
    public const float DocumentLogoHeight = 130;
    public const float DocumentLogoWidth = 130;

    public static byte[]? LoadPrimaryLogoBytes(IWebHostEnvironment env)
    {
        foreach (var p in new[]
                 {
                     Path.Combine(env.ContentRootPath, "wwwroot", "images", "ike-logo.png"),
                     Path.Combine(env.ContentRootPath, "wwwroot", "logo", "ike-full.png"),
                     Path.Combine(env.ContentRootPath, "wwwroot", "logo", "ike-icon.png"),
                     Path.Combine(env.ContentRootPath, "branding", "logo.png")
                 })
        {
            if (!File.Exists(p)) continue;
            try
            {
                return File.ReadAllBytes(p);
            }
            catch
            {
                // ignore and try next path
            }
        }

        return null;
    }

    public static string? LoadPrimaryLogoBase64(IWebHostEnvironment env)
    {
        var bytes = LoadPrimaryLogoBytes(env);
        return bytes == null ? null : Convert.ToBase64String(bytes);
    }

    public static void ApplyA4PageDefaults(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(1.5f, Unit.Centimetre);
    }

    public static void RenderHeader(RowDescriptor row, byte[]? logo, string documentType, string documentNumber)
    {
        RenderLogoCell(row, logo);

        row.RelativeItem().AlignRight().Column(col =>
        {
            col.Item().Text(documentType).FontSize(10).FontColor(Colors.Grey.Darken2);
            col.Item().Text(documentNumber).Bold().FontSize(20);
        });
    }

    public static void RenderLogoCell(RowDescriptor row, byte[]? logo)
    {
        var cell = row.ConstantItem(DocumentLogoWidth).Height(DocumentLogoHeight);
        if (logo != null)
            cell.AlignLeft().AlignTop().Image(logo).FitArea();
    }

    public static void RenderHeaderBand(PageDescriptor page, byte[]? logo, string documentType, string documentNumber)
    {
        page.Header().Column(col =>
        {
            col.Spacing(5);
            col.Item().Row(row => RenderHeader(row, logo, documentType, documentNumber));
            col.Item().LineHorizontal(0.8f).LineColor(Colors.Grey.Lighten1);
        });
    }

    public static void RenderSummaryCard(ColumnDescriptor column, Action<ColumnDescriptor> content)
    {
        column.Item().Background(Colors.Grey.Lighten3).Padding(10).Column(content);
    }

    public static void RenderGeneratedFooter(PageDescriptor page, string? prefix = null)
    {
        page.Footer().AlignCenter().Text(text =>
        {
            if (!string.IsNullOrWhiteSpace(prefix))
                text.Span(prefix + " ").FontSize(8).FontColor(Colors.Grey.Medium);
            text.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC").FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }
}
