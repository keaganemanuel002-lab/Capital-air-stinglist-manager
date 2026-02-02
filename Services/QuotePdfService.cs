using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StingListManager.Data.Entities;
using System;
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

    public byte[] BuildQuotePdf(Quote quote)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var priceResult = _pricingService.CalculatePrice(quote);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11));

                // Header with company name
                page.Header().Column(col =>
                {
                    col.Item().Text(_companyName).FontSize(18).SemiBold();
                    col.Item().Text(DateTime.Now.ToString("dd MMMM yyyy")).FontSize(10).FontColor(Colors.Grey.Darken1);
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
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(8).Text("Description").FontSize(10).Bold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(8).AlignRight().Text("Amount").FontSize(10).Bold();
                        });

                        // Line items - ensure they display
                        if (quote.LineItems != null && quote.LineItems.Any())
                        {
                            foreach (var item in quote.LineItems.OrderBy(x => x.LineNumber))
                            {
                                var description = item.ProductName ?? item.ProductType ?? "Service";
                                if (item.Quantity > 1)
                                    description += $" ({item.Quantity}x)";
                                
                                table.Cell().Padding(8).Text(description).FontSize(10);
                                table.Cell().Padding(8).AlignRight().Text($"R {item.LineTotalExVat:0.00}").FontSize(10);
                            }

                            // Add blank row for spacing
                            table.Cell().Padding(4).Text("").FontSize(8);
                            table.Cell().Padding(4).Text("").FontSize(8);
                        }

                        // Subtotal row
                        table.Cell().Padding(8).Text("Subtotal Ex VAT").FontSize(10).Bold();
                        table.Cell().Padding(8).AlignRight().Text($"R {priceResult.AmountExVat:0.00}").FontSize(10).Bold();

                        // VAT row
                        table.Cell().Padding(8).Text("Plus VAT @ 15%").FontSize(10).Bold();
                        table.Cell().Padding(8).AlignRight().Text($"R {priceResult.VatAmount:0.00}").FontSize(10).Bold();

                        // Total row
                        table.Cell().Padding(8).Background(Colors.Grey.Lighten2).Text("TOTAL").FontSize(11).Bold();
                        table.Cell().Padding(8).AlignRight().Background(Colors.Grey.Lighten2).Text($"R {priceResult.AmountIncVat:0.00}").FontSize(11).Bold();
                    });

                    col.Item().PaddingTop(12).LineHorizontal(1f);

                    // Notes if present
                    if (!string.IsNullOrWhiteSpace(quote.Notes))
                    {
                        col.Item().Text(quote.Notes).FontSize(10);
                    }

                    // Closing message
                    col.Item().PaddingTop(12).Text("We hope this quotation will meet your approval.").FontSize(10);

                    col.Item().PaddingTop(20).LineHorizontal(1f);

                    // Banking details
                    col.Item().PaddingTop(12).Text("Banking Details:").FontSize(10).Bold();
                    col.Item().Text(_companyName).FontSize(9);
                    col.Item().Text(_bankName).FontSize(9);
                    col.Item().Text(_bankBranch).FontSize(9);
                    col.Item().Text(_bankCode).FontSize(9);
                    col.Item().Text($"Account: {_accountNumber}").FontSize(9);
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

