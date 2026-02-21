using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StingListManager.Data.Entities;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace StingListManager.Services;

public class QuotePdfService
{
    private readonly QuotePricingService _pricingService;
    private readonly string _companyName = "Capital Air (Pty) Ltd";
    private readonly string _bankName = "Standard Bank";
    private readonly string _bankBranch = "Johannesburg";
    private readonly string _bankCode = "051001";
    private readonly string _accountNumber = "123456789";

    public QuotePdfService(AppSettings settings)
    {
        _pricingService = new QuotePricingService(settings);
    }

    private static string? ResolveLogoPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "capital-air-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "logo.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "capital-air-logo.png"),
            Path.Combine("C:\\dev\\StingListManager\\Assets", "capital-air-logo.png")
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public byte[] BuildQuotePdf(Quote quote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var priceResult = _pricingService.CalculatePrice(quote);
        var logoPath = ResolveLogoPath();
        const int maxLineItems = 10;
        var allLineItems = (quote.LineItems ?? Enumerable.Empty<QuoteLineItem>()).OrderBy(x => x.LineNumber).ToList();
        var lineItems = allLineItems.Take(maxLineItems).ToList();
        var hasMoreLineItems = allLineItems.Count > lineItems.Count;
        var notes = quote.Notes?.Trim();
        if (!string.IsNullOrWhiteSpace(notes) && notes.Length > 300)
            notes = notes[..300] + "…";

        return Document.Create(container =>
        {
            // First page: Quotation
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Calibri").FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(12);

                        row.AutoItem().AlignLeft().Width(161).Column(logo =>
                        {
                            logo.Item().Height(108).Element(e =>
                            {
                                if (!string.IsNullOrWhiteSpace(logoPath))
                                    e.Image(logoPath);
                            });

                            logo.Item().PaddingTop(1).Text("Co. Reg. No: 1979/06598/07").FontSize(6);
                            logo.Item().Text("VAT No: 4120110046").FontSize(6);
                        });

                        row.RelativeItem().Column(info =>
                        {
                            info.Item().AlignRight().Text(_companyName).FontSize(22).SemiBold();

                            info.Item().PaddingTop(4).Row(addressRow =>
                            {
                                addressRow.Spacing(4);

                                addressRow.RelativeItem().AlignRight().Row(addressCols =>
                                {
                                    addressCols.ConstantItem(110).Column(business =>
                                    {
                                        business.Item().AlignRight().Text("BUSINESS ADDRESS").FontSize(8).Bold();
                                        business.Item().AlignRight().Text("Hanger 3H").FontSize(8);
                                        business.Item().AlignRight().Text("Rand Airport").FontSize(8);
                                        business.Item().AlignRight().Text("Germiston").FontSize(8);
                                        business.Item().AlignRight().Text("South Africa").FontSize(8);
                                    });

                                    addressCols.ConstantItem(4).Text("");

                                    addressCols.ConstantItem(110).Column(postal =>
                                    {
                                        postal.Item().AlignRight().Text("POSTAL ADDRESS").FontSize(8).Bold();
                                        postal.Item().AlignRight().Text("P.O BOX 18009").FontSize(8);
                                        postal.Item().AlignRight().Text("Rand Airport 1419").FontSize(8);
                                        postal.Item().AlignRight().Text("Germiston").FontSize(8);
                                        postal.Item().AlignRight().Text("South Africa").FontSize(8);
                                    });
                                });
                            });

                            info.Item().PaddingTop(6).AlignRight().Text("TEL: +27 11 827 0335").FontSize(8);
                            info.Item().AlignRight().Text("FAX: +27 11 827 3898").FontSize(8);
                        });
                    });

                    col.Item().PaddingTop(6);
                    col.Item().AlignCenter().Text("Quotation").FontSize(20).Bold();
                    col.Item().PaddingTop(2).AlignLeft()
                        .Text($"Ref: {QuoteReferenceFormatter.Format(quote.QuoteNumber)}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Content().Column(col =>
                {
                    col.Item().AlignRight()
                        .Text(DateTime.Now.ToString("dd MMMM yyyy"))
                        .FontSize(10);

                    col.Item().PaddingTop(10);

                    col.Item().Text("TO: " + quote.Company).FontSize(11);
                    col.Item().Text("VIA EMAIL").FontSize(11);

                    col.Item().PaddingTop(10);

                    col.Item().Text("Dear Sir/Madam,");

                    var refText = quote.Type == QuoteType.Install
                        ? $"QUOTATION – {quote.ProductType ?? "STING"}"
                        : $"REMOVAL – {quote.Registration ?? quote.Company}";

                    col.Item().Text("REF: " + refText).Bold();

                    col.Item().PaddingTop(6);

                    col.Item().Text($"Please find enclosed a Quotation for {quote.Company}.");

                    col.Item().PaddingTop(10);

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3f);
                            columns.RelativeColumn(1f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Description").FontSize(10).Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).AlignRight().Text("Quantity").FontSize(10).Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).AlignRight().Text("Unit Price").FontSize(10).Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).AlignRight().Text("Amount").FontSize(10).Bold();
                        });

                        if (lineItems.Any())
                        {
                            foreach (var item in lineItems)
                            {
                                var description = !string.IsNullOrWhiteSpace(item.Description)
                                    ? item.Description
                                    : item.ProductName ?? item.ProductType ?? "Service";

                                table.Cell().Padding(4).Text(description).FontSize(10);
                                table.Cell().Padding(4).AlignRight().Text(item.Quantity.ToString()).FontSize(10);
                                table.Cell().Padding(4).AlignRight().Text($"R {item.UnitPriceExVat:0.00}").FontSize(10);
                                table.Cell().Padding(4).AlignRight().Text($"R {item.LineTotalExVat:0.00}").FontSize(10);
                            }

                            table.Cell().ColumnSpan(4).Padding(2).Text("").FontSize(8);
                        }

                        table.Cell().ColumnSpan(3).Padding(4).Text("Subtotal Ex VAT").FontSize(10).Bold();
                        table.Cell().Padding(4).AlignRight().Text($"R {priceResult.AmountExVat:0.00}").FontSize(10).Bold();

                        table.Cell().ColumnSpan(3).Padding(4).Text("Plus VAT @ 15%").FontSize(10).Bold();
                        table.Cell().Padding(4).AlignRight().Text($"R {priceResult.VatAmount:0.00}").FontSize(10).Bold();

                        table.Cell().ColumnSpan(3).Padding(4).Background(Colors.Grey.Lighten2).Text("TOTAL").FontSize(11).Bold();
                        table.Cell().Padding(4).AlignRight().Background(Colors.Grey.Lighten2).Text($"R {priceResult.AmountIncVat:0.00}").FontSize(11).Bold();
                    });

                    col.Item().PaddingTop(12).LineHorizontal(1f);

                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        col.Item().Text(notes).FontSize(10);
                    }

                    if (hasMoreLineItems)
                    {
                        col.Item().PaddingTop(6).Text("Additional items omitted to keep the quotation to one page.")
                            .FontSize(8)
                            .FontColor(Colors.Grey.Darken1);
                    }

                    col.Item().PaddingTop(12).Text("We hope this quotation will meet your approval.").FontSize(10);
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().LineHorizontal(1f);

                    footer.Item().PaddingTop(8).Text("Banking Details:").FontSize(9).Bold();
                    footer.Item().Column(bank =>
                    {
                        bank.Spacing(0);
                        bank.Item().Text(_companyName).FontSize(8.5f);
                        bank.Item().Text(_bankName).FontSize(8.5f);
                        bank.Item().Text(_bankBranch).FontSize(8.5f);
                        bank.Item().Text(_bankCode).FontSize(8.5f);
                        bank.Item().Text($"Account: {_accountNumber}").FontSize(8.5f);
                    });

                    footer.Item().PaddingTop(6).Text(
                        "Please note these prices are valid for 14 days from the date of issuing this quotation and excludes reactivation fees.")
                        .FontSize(8.5f)
                        .Italic();
                });
            });

            // Second page: Terms and Conditions (landscape)
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(5.5f).FontColor(Colors.Blue.Darken4));

                page.Header().Text("STING MONITORING, RECOVERY AND SERVICES AGREEMENT").FontSize(10).Bold().AlignCenter();

                page.Content().Row(row =>
                {
                    var termsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "terms_and_conditions.txt");
                    var termsText = File.Exists(termsPath) ? File.ReadAllText(termsPath) : "Terms & Conditions text not found. Place a file at Assets/terms_and_conditions.txt";
                    var paragraphs = termsText.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToList();

                    // Assign paragraphs to columns based on clause numbers.
                    // Rules: clauses 4 -> 10.1.2 => column 2; clauses 10.1.3 -> 16.2 => column 3; rest => column 1.
                    List<string> col1 = new();
                    List<string> col2 = new();
                    List<string> col3 = new();

                    int[] ParseClause(string text)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(text, "^\\s*(\\d+(?:\\.\\d+)*)\\b");
                        if (!m.Success) return null;
                        return m.Groups[1].Value.Split('.').Select(s => { int.TryParse(s, out var v); return v; }).ToArray();
                    }

                    int CompareSeq(int[] a, int[] b)
                    {
                        if (a == null && b == null) return 0;
                        if (a == null) return -1;
                        if (b == null) return 1;
                        var max = Math.Max(a.Length, b.Length);
                        for (int i = 0; i < max; i++)
                        {
                            var ai = i < a.Length ? a[i] : 0;
                            var bi = i < b.Length ? b[i] : 0;
                            if (ai < bi) return -1;
                            if (ai > bi) return 1;
                        }
                        return 0;
                    }

                    bool InRange(int[] value, int[] start, int[] end)
                    {
                        if (value == null) return false;
                        return CompareSeq(value, start) >= 0 && CompareSeq(value, end) <= 0;
                    }

                    var startA = new[] { 4 };
                    var endA = new[] { 10, 1, 2 };
                    var startB = new[] { 10, 1, 3 };
                    var endB = new[] { 16, 2 };

                    foreach (var p in paragraphs)
                    {
                        var seq = ParseClause(p);
                        if (seq != null && InRange(seq, startA, endA))
                            col2.Add(p);
                        else if (seq != null && InRange(seq, startB, endB))
                            col3.Add(p);
                        else
                            col1.Add(p);
                    }

                    row.RelativeColumn().Column(c1 =>
                    {
                        foreach (var p in col1)
                            c1.Item().Text(p).FontSize(5.5f);
                    });

                    row.RelativeColumn().Column(c2 =>
                    {
                        foreach (var p in col2)
                            c2.Item().Text(p).FontSize(5.5f);
                    });

                    row.RelativeColumn().Column(c3 =>
                    {
                        foreach (var p in col3)
                            c3.Item().Text(p).FontSize(5.5f);
                    });
                });
            });
        }).GeneratePdf();
    }
}


