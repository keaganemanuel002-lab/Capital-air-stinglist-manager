using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StingListManager.Data.Entities;
using System;
using System.IO;
using System.Linq;

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
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11));

                // Header with logo + company details
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(12);

                        row.AutoItem().AlignLeft().Width(141).Column(logo =>
                        {
                            logo.Item().Height(94).Element(e =>
                            {
                                if (!string.IsNullOrWhiteSpace(logoPath))
                                    e.Image(logoPath);
                            });

                            logo.Item().PaddingTop(1).Text("Co. Reg. No: 1979/06598/07").FontSize(6);
                            logo.Item().Text("VAT No: 4120110046").FontSize(6);
                        });

                        row.RelativeItem().Column(info =>
                        {
                            info.Item().AlignRight().Text(_companyName).FontSize(18).SemiBold();

                            info.Item().PaddingTop(4).Row(addressRow =>
                            {
                                addressRow.Spacing(4);

                                addressRow.RelativeItem().AlignRight().Row(addressCols =>
                                {
                                    // Business Address
                                    addressCols.ConstantItem(110).Column(business =>
                                    {
                                        business.Item().AlignRight().Text("BUSINESS ADDRESS").FontSize(8).Bold();
                                        business.Item().AlignRight().Text("Hanger 3H").FontSize(8);
                                        business.Item().AlignRight().Text("Rand Airport").FontSize(8);
                                        business.Item().AlignRight().Text("Germiston").FontSize(8);
                                        business.Item().AlignRight().Text("South Africa").FontSize(8);
                                    });
                                    // Spacer (reduced)
                                    addressCols.ConstantItem(4).Text("");
                                    // Postal Address
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
                        .Text($"Ref: {quote.QuoteNumber}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                    col.Item().AlignLeft().Text(DateTime.Now.ToString("dd MMMM yyyy")).FontSize(10).FontColor(Colors.Grey.Darken1);
                });

                // Main content
                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    // Recipient section
                    col.Item().Text("TO: " + quote.Company).FontSize(10);
                    col.Item().Text("VIA EMAIL").FontSize(10).FontColor(Colors.Grey.Darken1);
                    
                    col.Item().PaddingBottom(10).LineHorizontal(1f);

                    // Greeting
                    col.Item().Text($"Dear Sir/Madam,").FontSize(11);

                    // Reference
                    var refText = quote.Type == QuoteType.Install 
                        ? $"INSTALLATION - {quote.Company}" 
                        : $"REMOVAL - {quote.Registration}";
                    col.Item().Text("REF: " + refText).FontSize(10).Bold();

                    col.Item().PaddingBottom(12).LineHorizontal(1f);

                    // Intro text
                    col.Item().Text($"Please find enclosed a Quotation for {quote.Company}.");

                    col.Item().PaddingTop(12).LineHorizontal(0);

                    // Line items table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3f);
                            columns.RelativeColumn(1.5f);
                        });

                        // Header row
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text("Description").FontSize(10).Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).AlignRight().Text("Amount").FontSize(10).Bold();
                        });

                        // Line items - ensure they display
                        if (lineItems.Any())
                        {
                            foreach (var item in lineItems)
                            {
                                var description = item.ProductName ?? item.ProductType ?? "Service";
                                if (item.Quantity > 1)
                                    description += $" ({item.Quantity}x)";
                                
                                table.Cell().Padding(4).Text(description).FontSize(10);
                                table.Cell().Padding(4).AlignRight().Text($"R {item.LineTotalExVat:0.00}").FontSize(10);
                            }

                            // Add blank row for spacing
                            table.Cell().Padding(2).Text("").FontSize(8);
                            table.Cell().Padding(2).Text("").FontSize(8);
                        }

                        // Subtotal row
                        table.Cell().Padding(4).Text("Subtotal Ex VAT").FontSize(10).Bold();
                        table.Cell().Padding(4).AlignRight().Text($"R {priceResult.AmountExVat:0.00}").FontSize(10).Bold();

                        // VAT row
                        table.Cell().Padding(4).Text("Plus VAT @ 15%").FontSize(10).Bold();
                        table.Cell().Padding(4).AlignRight().Text($"R {priceResult.VatAmount:0.00}").FontSize(10).Bold();

                        // Total row
                        table.Cell().Padding(4).Background(Colors.Grey.Lighten2).Text("TOTAL").FontSize(11).Bold();
                        table.Cell().Padding(4).AlignRight().Background(Colors.Grey.Lighten2).Text($"R {priceResult.AmountIncVat:0.00}").FontSize(11).Bold();
                    });

                    col.Item().PaddingTop(12).LineHorizontal(1f);

                    // Notes if present
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

                    // Closing message
                    col.Item().PaddingTop(12).Text("We hope this quotation will meet your approval.").FontSize(10);

                    col.Item().PaddingTop(20).LineHorizontal(1f);

                    // Banking details
                    col.Item().PaddingTop(12).Text("Banking Details:").FontSize(10).Bold();
                    col.Item().Column(bank =>
                    {
                        bank.Spacing(0);
                        bank.Item().Text(_companyName).FontSize(9);
                        bank.Item().Text(_bankName).FontSize(9);
                        bank.Item().Text(_bankBranch).FontSize(9);
                        bank.Item().Text(_bankCode).FontSize(9);
                        bank.Item().Text($"Account: {_accountNumber}").FontSize(9);
                    });
                });

                // Footer
                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1f);
                    col.Item().PaddingTop(6).Text("Please note this quotation is valid for 14 days from the date of issue and excludes reactivation fees.").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }
}

