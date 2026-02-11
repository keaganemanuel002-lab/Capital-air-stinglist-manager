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
            // First page: Quotation
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

            // Second page: Terms and Conditions (landscape)
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(5.5f).FontFamily("Arial").FontColor("#002D62"));
                page.Header().Text("STING MONITORING, RECOVERY AND SERVICES AGREEMENT").FontSize(10).Bold().AlignCenter().FontFamily("Arial").FontColor("#002D62");
                page.Content().Row(row =>
                {
                    row.RelativeColumn().Column(col1 =>
                    {
                        col1.Item().Text("1. Interpretation").Bold();
                        col1.Item().Text("In this agreement unless the context indicates to the contrary, the intention:-");
                        col1.Item().Text("1.1. ‘Capital’ shall mean for the purposes of this agreement Capital Air (Pty) Ltd (Registration Number 76/06598/07) specifically and includes for the purpose of this agreement its associated companies; Capital Air Recovery Services (Pty) Ltd (CARS) and Capital Control Centre (Pty) Ltd (CCC). It is acknowledged by the Customer that by the nature of the services that are to be rendered pursuant to this agreement, Capital Air will of necessity utilise the services of CARS and CCC where and when applicable.");
                        col1.Item().Text("1.2. ‘this Agreement’ shall mean this agreement set out hereunder read together with all schedules thereto;");
                        col1.Item().Text("1.3. ‘Asset’ shall mean the specified Asset/Assets of the Customer as detailed in the schedule hereto, in which the Sting Module is or will be installed;");
                        col1.Item().Text("1.4. ‘Connection’ shall mean the electronic connection of the Sting Module to the system to enable the services to be provided;");
                        col1.Item().Text("1.5. ‘Customer’ shall mean the Customer whose detail appears in the schedule;");
                        col1.Item().Text("1.6. ‘Group Assets’ shall mean the collective Assets of the Customer under similar agreements with Capital which are recorded as such in the respective agreement and or schedule;");
                        col1.Item().Text("1.7. ‘Implementation of Recovery Services’ shall mean the activation of the system to locate the whereabouts of the Assets and the call out of recovery agents when required;");
                        col1.Item().Text("1.8. ‘Maintain’ in relation to the Sting Module means the remote testing, configuration and repair thereof as required and determined by Capital from time to time. ‘Maintenance’ shall have the corresponding meaning;’");
                        col1.Item().Text("1.9. ‘Recovery Agent’ means the third-party entity or authority which is appointed by Capital to undertake the recovery services or any part thereof;");
                        col1.Item().Text("1.10. ‘Recovery Services’ means the services provided by Capital or the recovery agent to facilitate the recovery of the Asset for the Customer;");
                        col1.Item().Text("1.11. ‘Schedule/s’ means the schedule to this agreement containing information pertaining ot the Customer, the Asset, the option chosen by the Customer, the pricing and all particulars relevant to this agreement for the rendering of the services;’");
                        col1.Item().Text("1.12. ‘Services’ means the services referred to in the schedule read together with the recovery option selected by the Customer as reflected in the schedule or any other services as may be agreed to in writing by the Customer and Capital from time to time;’");
                        col1.Item().Text("1.13. ‘Sting Module’ means the device supplied by Capital or any equipment which is required for the operation of the system and which will be installed in the Customer’s Asset/s;’");
                        col1.Item().Text("1.14. ‘System’ means the tracking system used by Capital from time to time providing the services to the Customer;");
                        col1.Item().Text("1.15. ‘Variable Charges’ means the variable component of monthly charges as specified in the schedule and calculated by Capital from time to time;’");
                        col1.Item().Text("1.16. Words denoting the singular shall include the plural and vice versa.");
                        col1.Item().Text("1.17. Clause headings of this agreement have been inserted for the convenience only and shall not be taken into account in its interpretation;");
                        col1.Item().Text("1.18. This agreement shall be governed by and interpreted in accordance with the laws of the Republic of South Africa;’");
                        col1.Item().Text("1.19. The ‘contra proferentum’ rule shall not apply to this agreement and accordingly none of the provisions hereof shall be construed against or interpreted to the disadvantage of the party responsible for the drafting or preparation of this agreement;");
                        col1.Item().Text("1.20. Any reference to a party includes the reference to that party’s successors in title or assigns as may be allowed in law;");
                        col1.Item().Text("1.21. Where any number of days is prescribed in this agreement same shall be reckoned exclusive of the first and inclusive of the last day unless the last day falls on a Saturday, Sunday or public holiday in which case the last day shall be the next succeeding day which is not a Saturday, Sunday or Public Holiday;");
                        col1.Item().Text("2. Provision of Services").Bold();
                        col1.Item().Text("Subject to the provision of this agreement into the option chosen by the Customer in the schedule hereto, Capital and or its associated companies as referred to above shall:-");
                        col1.Item().Text("2.1. Install or procure the installation of the Sting Module in the Asset/s;");
                        col1.Item().Text("2.2. Connect or procure the connection of the Sting Module to the system;");
                        col1.Item().Text("2.3. Upon receipt of notification from the Customer in terms of clause 8.1. below that the Asset/s has been lost as a result of theft or hi-jacking:-");
                        col1.Item().Text("2.3.1. Use its reasonable endeavours to determine the virtual position of the Asset by utilising the system;");
                        col1.Item().Text("2.3.2. If requested by the Customer, provided that Capital has successfully determined the virtual position of the Asset in terms of 2.3.1. above, procure the implementation of the recovery services;");
                        col1.Item().Text("2.3.3. After physically locating the Asset, Capital and/or its associated companies will maintain a surveillance of the Asset for a period of not exceeding 1 (one) hour thereafter;");
                        col1.Item().Text("2.3.4. required by the Customer and if so stipulated in the schedule periodically monitor within the system the virtual position of the Asset as agreed between Capital and the Customer from time to time;");
                        col1.Item().Text("3. Payment").Bold();
                        col1.Item().Text("The Customer shall pay to Capital:-");
                        col1.Item().Text("3.1. The total installation charges as recorded in the schedule and within the time specified in the schedule;");
                        col1.Item().Text("3.2. The fixed monthly charges reflected in the schedule;");
                        col1.Item().Text("3.3. The variable monthly charges reflected in the schedule as calculated by Capital based on the use of the services selected by the Customer during such month;");
                        col1.Item().Text("3.4. All other expenses incurred by Capital pursuant to written instructions received from the Customer to carry out services in addition to those referred to in clause 2 above.");
                        col1.Item().Text("3.5. Payment of the amount due in terms of 3.1. shall be made at the time of the signing of the agreement by the Customer or such later date as Capital may determine and agree to in writing but in any event, on installation and prior to connection of the Sting Module;");
                        col1.Item().Text("3.6. Payment of the amounts due in terms of 3.2., 3.3. and 3.4. shall be made as specified in the schedule and failing such specification within seven (7) days of the date of Capital’s invoice to the Customer;");
                        col1.Item().Text("3.7. As stipulated in the schedule, the Customer shall pay to Capital the deposit reflected in the schedule at the time that this agreement is signed by the Customer. Capital shall be entitled to apply the whole or any portion of such deposit towards the payment of any sums whatsoever due to Capital by the Customer.");
                        col1.Item().Text("3.8. Capital may vary all or any of the charges payable by the Customer as specified in the schedule in terms of the this agreement by giving at least 30 (thirty) days prior notice in writing to the Customer.");
                        col1.Item().Text("4. Duration").Bold();
                        col1.Item().Text("This agreement shall commence on the date of signing of the schedule and shall continue until terminated:-");
                        col1.Item().Text("4.1. By Capital giving notice in terms of clause 4.3. or Capital terminating this agreement in terms of clause 12;");
                        col1.Item().Text("4.2. By the Customer giving not less than 90 (ninety) days’ written notice to Capital provided that no such notice may be given by the Customer so as to terminate this agreement prior to the end date stipulated in the schedule;");
                        col1.Item().Text("4.3. Capital shall be entitled to terminate this agreement forthwith by giving not less than 7 (seven) days notice to the effect:-");
                        col1.Item().Text("4.3.1. If any authority required to operate the system is revoked, terminated or modified for any reason, or");
                        col1.Item().Text("4.3.2. Capital is unable to operate the system as a result of any provider of the services to Capital which are required for the operation of the system discontinuing or suspending such services.");
                        // up to 10.1.2
                        col1.Item().Text("10. Exclusion of Liability").Bold();
                        col1.Item().Text("10.1. The Customer acknowledges that the services to be provided by Capital in terms of this agreement:-");
                        col1.Item().Text("10.1.1. Shall be limited to the scope and capabilities of the system;’");
                        col1.Item().Text("10.1.2. May from time to time be impaired or curtailed by physical features and occurrences beyond the control of Capital;");
                    });
                    row.RelativeColumn().Column(col2 =>
                    {
                        col2.Item().Text("10.1.3. The Customer agrees and acknowledges that the services to be provided by Capital are intended to reduce the risk of loss to the Asset but not to eliminate such risk and no warranties, representations or undertakings of any nature whatsoever are given by Capital in regard thereto. In particular without limiting the generality of the foregoing, Capital does not warrant that it will be successful in locating and recovering the Asset in the event that it is lost or stolen.’");
                        col2.Item().Text("10.1.4. Capital shall not be liable for any loss or damage of whatsoever nature and howsoever arising, including but not limited to loss of the Asset, personal injury, death, loss of profits and consequential damages. The provisions of this clause shall apply notwithstanding that any loss or damage or injury or death may occur or be sustained in consequence of any act or omission by Capital or any failure by Capital to perform the services in terms of this agreement notwithstanding any negligence on the part of Capital.’");
                        col2.Item().Text("10.1.5. The Customer hereby indemnifies Capital and holds it harmless against any claim of whatsoever nature that may be made against Capital by any person as a direct result of or arising out of any act or omission of Capital in providing the services notwithstanding any negligence on the part of Capital.");
                        col2.Item().Text("10.1.6. Without derogating the generality of the foregoing, Capital shall not be liable to be Customer as a result of any failure on Capital’s part to perform any of its obligations under this agreement, if such failure is due to or arising out of technical problems relating to the system, termination of any services provided to Capital which are required for the operation of the system, vis major and any authority which has jurisdiction of control over the operation of the system, default of any product supplied, sub-contractors, industrial action, disputes of any nature or any causes being beyond Capital’s control.");
                        col2.Item().Text("10.1.7. For the purpose of this clause all reference to Capital shall include the reference to CARS and CCC, their agents, directors, servants, subcontractors and independent contractors.");
                        col2.Item().Text("11. Suspension of services").Bold();
                        col2.Item().Text("11.1. Capital shall have the right from time to time and without notice to suspend the services in any of the following circumstances:-‘");
                        col2.Item().Text("11.1.1. As a result of any technical failure, modification or maintenance of the system;");
                        col2.Item().Text("11.1.2. If the Customer fails to comply with any of the terms and conditions of this agreement;’");
                        col2.Item().Text("11.1.3. Until any breach of this agreement (if capable of remedy) has been remedied; or");
                        col2.Item().Text("11.1.4. If any act of omission by the Customer has a negative effect on the operation of the system or the services that Capital in its discretion may decide.");
                        col2.Item().Text("11.2. If Capital exercises its rights in terms of 11.1 above the Customer shall remain liable for all charges in terms of this agreement unless Capital shall otherwise determines.");
                        col2.Item().Text("12. Breach").Bold();
                        col2.Item().Text("12.1  If the Customer fails to pay any amount due in terms of this agreement on due date; or’");
                        col2.Item().Text("12.2.  Fails in the performance of any of its/his/her obligations hereunder or breaches any terms and conditions of this agreement; or’");
                        col2.Item().Text("12.3  In the discretion of Capital reasonably exercised the Customer consistently raises false alarms or abuses the service provided by Capital;");
                        col2.Item().Text("12.4. Capital may without prejudice to any other right which it may immediately suspend its obligations under this agreement and will simultaneously therewith terminate this agreement without further notice to the Customer.");
                        col2.Item().Text("12.5. Upon termination of this agreement for any reason whatsoever:-");
                        col2.Item().Text("12.5.1. Capital shall disconnect the Sting Module from the system;’");
                        col2.Item().Text("12.5.2. the Customer shall comply with its obligations in terms of clause 6.2. ;");
                        col2.Item().Text("12.5.3. the Customer shall pay on demand all outstanding charges including any disconnection and de- installation charges which Capital may charge.");
                        col2.Item().Text("12.6. In the event that Capital is required to institute any action against the Customer in consequence of the Customer’s breach of this agreement for any reason whatsoever, the Customer shall be liable to pay Capital’s costs on the attorney and own client scale.");
                        col2.Item().Text("13. Assignment").Bold();
                        col2.Item().Text("13.1. The Customer shall not cede, transfer, encumber, delegate or assign any of its rights or obligations under this agreement to any third party.");
                        col2.Item().Text("13.2. Capital shall be entitled at any time to cede, assign, transfer, encumber or delegate its rights and obligations under this agreement to any other party and in event it shall notify the Customer of such cession and/or assignment, transfer or delegation of all rights and obligations");
                        col2.Item().Text("14. Severability").Bold();
                        col2.Item().Text("In the event that any clause contained in this agreement is invalid or unenforceable, then such clause shall not affect the validity or enforceability insofar as the remaining clauses are concerned and in the event that any clause is invalid or unenforceable it shall in the opinion of Capital adversely affect Capital’s right to receive payment of fees, remuneration or by whatever means payable to it, then Capital shall have the right to terminate this agreement on 90 (ninety) days notice in writing to the Customer.’");
                        col2.Item().Text("15. General").Bold();
                        col2.Item().Text("15.1. This agreement together with the schedules constitutes the entire agreement between the parties and no other conditions, stipulations, warranties, statements of fact, opinions or representations whatsoever have been made by either party other than as specifically included herein.");
                        col2.Item().Text("15.2. No variation or cancellation of the provisions hereof shall be of any force or effect unless reduced to writing and signed by and on behalf of both parties.’");
                        col2.Item().Text("15.3. No party to this agreement shall be regarded as having waived or be precluded of exercising any right under this agreement by reason merely that such party has shown any indulgence to the other party or parties hereto or fails to exercise or delays in exercising any right under this agreement whether the same right or any right.");
                        col2.Item().Text("15.4. A certificate signed by a director of Capital whose authority need not be proved as to the existence and the amount of the Customer’s indebtedness to Capital and to the fact that such amount is due and payable and the amount of interest accrued thereon shall constitute prime facie proof of the contents and correctness thereof.’");
                        col2.Item().Text("16. Domicilium and notices").Bold();
                        col2.Item().Text("16.1. The parties choose as their domiclium citandi et executandi the addresses stated in the schedule provided that any party may change its domicilium aforesaid on 15 (fifteen) days written notice to the other party.");
                        col2.Item().Text("16.2. Any notice given in terms of this clause shall be given by prepaid registered post, by hand or by email to the other party at the address chosen by it as reflected in the schedule. In the event that the notice is sent by registered post then the written notice shall be deemed to have been received 5 (five) business days after the stamp of such registered posting; if notice is given by hand or by email on such date of hand delivery or transmission of the email.");
                    });
                    row.RelativeColumn().Column(col3 =>
                    {
                        // If there are any additional sections, add them here, or leave blank if all content is covered in col1 and col2.
                    });
                });
            });
        }).GeneratePdf();
    }
}


