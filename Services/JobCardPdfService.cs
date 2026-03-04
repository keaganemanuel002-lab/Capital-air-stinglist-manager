using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestImage = QuestPDF.Infrastructure.Image;
using StingListManager.Data.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingListManager.Services;

public class JobCardPdfService
{
    private const string Navy = "#0D2B4E";
    private const string SkyBlue = "#1A73C8";
    private const string LightBackground = "#EAF2FB";
    private const string MidGrey = "#7A8A9A";
    private const string BorderGrey = "#D8E4EE";
    private readonly string _companyName = "Capital Air (Pty) Ltd";

    private static readonly HashSet<string> SupportedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
    };

    public sealed class JobCardPdfData
    {
        public JobCard JobCard { get; init; } = new();
        public Quote? RelatedQuote { get; init; }
        public IReadOnlyList<Attachment> TechnicianPhotos { get; init; } = Array.Empty<Attachment>();
    }

    private sealed class RenderablePhoto
    {
        public required Attachment Attachment { get; init; }
        public required QuestImage Image { get; init; }
    }

    private enum TextAlignMode
    {
        Left,
        Center,
        Right
    }

    public byte[] BuildJobCardPdf(JobCard jobCard)
    {
        return BuildJobCardPdf(new JobCardPdfData { JobCard = jobCard });
    }

    public byte[] BuildJobCardPdf(JobCardPdfData data)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var logoPath = ResolveLogoPath();
        var renderablePhotos = BuildRenderablePhotos(data.TechnicianPhotos);

        return Document.Create(container =>
        {
            container.Page(page => ComposePage(page, data, logoPath, renderablePhotos));
        }).GeneratePdf();
    }

    public byte[] BuildMultipleJobCardsPdf(List<JobCard> jobCards)
    {
        var pdfData = jobCards.Select(j => new JobCardPdfData { JobCard = j }).ToList();
        return BuildMultipleJobCardsPdf(pdfData);
    }

    public byte[] BuildMultipleJobCardsPdf(List<JobCardPdfData> jobCards)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var logoPath = ResolveLogoPath();

        return Document.Create(container =>
        {
            foreach (var data in jobCards)
            {
                var renderablePhotos = BuildRenderablePhotos(data.TechnicianPhotos);
                container.Page(page => ComposePage(page, data, logoPath, renderablePhotos));
            }
        }).GeneratePdf();
    }

    private void ComposePage(PageDescriptor page, JobCardPdfData data, string? logoPath, IReadOnlyList<RenderablePhoto> renderablePhotos)
    {
        page.Size(PageSizes.A4);
        page.Margin(24);
        page.DefaultTextStyle(x => x.FontSize(10));

        page.Header().Element(container => ComposeHeader(container, data.JobCard, logoPath));
        page.Content().PaddingTop(10).Element(container =>
            ComposeContent(container, data.JobCard, data.RelatedQuote, data.TechnicianPhotos.Count, renderablePhotos));
        page.Footer().Element(ComposeFooter);
    }

    private void ComposeHeader(IContainer container, JobCard jobCard, string? logoPath)
    {
        var jobCardReference = JobCardReferenceFormatter.Format(jobCard);

        container.Column(col =>
        {
            col.Item().Background(Navy).Padding(10).Row(row =>
            {
                row.Spacing(12);

                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(_companyName).FontSize(12).SemiBold().FontColor(Colors.White);
                    left.Item().Text("TRACKING UNIT INSTALLATION - JOB CARD")
                        .FontSize(8)
                        .FontColor("#A8C8E8");
                });

                row.RelativeItem().AlignCenter().AlignMiddle()
                    .Text("JOB CARD")
                    .FontSize(22)
                    .Bold()
                    .FontColor(Colors.White);

                row.ConstantItem(190).AlignRight().Column(right =>
                {
                    right.Item().Height(42).AlignRight().Element(e =>
                    {
                        if (!string.IsNullOrWhiteSpace(logoPath))
                            e.Image(logoPath!).FitArea();
                    });

                    right.Item().PaddingTop(4).AlignRight().Background(SkyBlue).PaddingVertical(4).PaddingHorizontal(8)
                        .Text($"JOB CARD NO. {jobCardReference}")
                        .FontSize(9)
                        .SemiBold()
                        .FontColor(Colors.White);
                });
            });

            col.Item().Height(4).Background(SkyBlue);
        });
    }

    private void ComposeContent(
        IContainer container,
        JobCard jobCard,
        Quote? relatedQuote,
        int totalTechnicianPhotoCount,
        IReadOnlyList<RenderablePhoto> renderablePhotos)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Element(c => ComposeStatusBar(c, jobCard));
            col.Item().Element(c => ComposeInfoSection(c, "1 - CLIENT COMPANY INFORMATION", BuildClientRows(jobCard)));
            col.Item().Element(c => ComposeInfoSection(c, "2 - VEHICLE INFORMATION", BuildVehicleRows(jobCard)));
            col.Item().Element(c => ComposeInfoSection(c, "3 - TRACKING UNIT INFORMATION", BuildTrackingRows(jobCard)));
            col.Item().Element(c => ComposeQuoteSection(c, jobCard, relatedQuote));
            col.Item().Element(c => ComposePhotoSection(c, jobCard, totalTechnicianPhotoCount, renderablePhotos));

            if (!string.IsNullOrWhiteSpace(jobCard.Notes))
            {
                col.Item().Column(notes =>
                {
                    notes.Item().Text("NOTES / OBSERVATIONS").FontSize(9).SemiBold().FontColor(SkyBlue);
                    notes.Item()
                        .Border(1)
                        .BorderColor(BorderGrey)
                        .Background("#F5F9FC")
                        .Padding(8)
                        .Text(jobCard.Notes!.Trim())
                        .FontSize(9.5f);
                });
            }
        });
    }

    private static void ComposeStatusBar(IContainer container, JobCard jobCard)
    {
        var statusChecklist = BuildStatusChecklist(jobCard.Status);

        container.Border(1).BorderColor(BorderGrey).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(2.3f);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(3.3f);
            });

            AddLabelCell(table, "Date");
            AddValueCell(table, DateTime.Now.ToString("dd MMM yyyy"));
            AddLabelCell(table, "Status");
            AddValueCell(table, statusChecklist);

            AddLabelCell(table, "Scheduled For");
            AddValueCell(table, FormatDateTime(jobCard.ScheduledFor));
            AddLabelCell(table, "Completed");
            AddValueCell(table, FormatDateTime(jobCard.CompletedAt));
        });
    }

    private static void ComposeInfoSection(IContainer container, string title, List<(string Label, string Value)> fields)
    {
        container.Column(col =>
        {
            col.Item().Element(c => ComposeSectionHeader(c, title));
            col.Item().Element(c => ComposeInfoFieldsTable(c, fields));
        });
    }

    private static void ComposeQuoteSection(IContainer container, JobCard jobCard, Quote? quote)
    {
        var lineItems = quote?.LineItems?
            .OrderBy(x => x.LineNumber)
            .ToList() ?? [];

        container.Column(col =>
        {
            col.Spacing(6);

            col.Item().Element(c => ComposeSectionHeader(c, "4 - RELATED QUOTE DETAILS"));
            col.Item().Element(c => ComposeInfoFieldsTable(c, BuildQuoteRows(jobCard, quote)));

            if (lineItems.Count == 0)
            {
                col.Item()
                    .Border(1)
                    .BorderColor(BorderGrey)
                    .Background("#F5F9FC")
                    .Padding(8)
                    .Text("No quote line items found for this linked quote.")
                    .FontSize(9)
                    .FontColor(MidGrey);
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(32);
                    columns.RelativeColumn(3.5f);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(92);
                    columns.ConstantColumn(104);
                });

                AddQuoteLineHeaderCell(table, "#");
                AddQuoteLineHeaderCell(table, "Description");
                AddQuoteLineHeaderCell(table, "Qty");
                AddQuoteLineHeaderCell(table, "Unit Ex VAT");
                AddQuoteLineHeaderCell(table, "Line Total Ex VAT");

                foreach (var item in lineItems)
                {
                    AddQuoteLineValueCell(table, item.LineNumber.ToString(), TextAlignMode.Center);
                    AddQuoteLineValueCell(table, BuildLineItemDescription(item), TextAlignMode.Left);
                    AddQuoteLineValueCell(table, item.Quantity.ToString(), TextAlignMode.Center);
                    AddQuoteLineValueCell(table, FormatCurrency(item.UnitPriceExVat), TextAlignMode.Right);
                    AddQuoteLineValueCell(table, FormatCurrency(item.LineTotalExVat), TextAlignMode.Right);
                }

                var quoteAmountExVat = quote is null ? "-" : FormatCurrency(quote.AmountExVat);

                table.Cell().ColumnSpan(4)
                    .Border(0.5f)
                    .BorderColor(BorderGrey)
                    .Background(LightBackground)
                    .Padding(5)
                    .AlignRight()
                    .Text("Quote Amount Ex VAT")
                    .FontSize(8.5f)
                    .SemiBold()
                    .FontColor(MidGrey);

                table.Cell()
                    .Border(0.5f)
                    .BorderColor(BorderGrey)
                    .Background(LightBackground)
                    .Padding(5)
                    .AlignRight()
                    .Text(quoteAmountExVat)
                    .FontSize(9)
                    .SemiBold();

            });
        });
    }

    private static void ComposeSectionHeader(IContainer container, string title)
    {
        container.Background(Navy).PaddingVertical(5).PaddingHorizontal(8)
            .Text(title).FontSize(9).Bold().FontColor(Colors.White);
    }

    private static void ComposeInfoFieldsTable(IContainer container, List<(string Label, string Value)> fields)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.1f);
                columns.RelativeColumn(2.2f);
                columns.RelativeColumn(1.1f);
                columns.RelativeColumn(2.2f);
            });

            for (var i = 0; i < fields.Count; i += 2)
            {
                var left = fields[i];
                var hasRight = i + 1 < fields.Count;
                var right = hasRight ? fields[i + 1] : ("", "");

                AddLabelCell(table, left.Label);
                AddValueCell(table, left.Value);
                AddLabelCell(table, right.Item1);
                AddValueCell(table, right.Item2);
            }
        });
    }

    private static void ComposePhotoSection(
        IContainer container,
        JobCard jobCard,
        int totalTechnicianPhotoCount,
        IReadOnlyList<RenderablePhoto> renderablePhotos)
    {
        container.Column(col =>
        {
            col.Spacing(6);

            col.Item().Element(c => ComposeSectionHeader(c, "5 - TECHNICIAN PHOTOS"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(2.2f);
                });

                AddLabelCell(table, "Installation Technician");
                AddValueCell(table, ValueOrDash(jobCard.InstallationTechnician));
            });

            if (totalTechnicianPhotoCount <= 0)
            {
                col.Item()
                    .Border(1)
                    .BorderColor(BorderGrey)
                    .Background("#F5F9FC")
                    .Padding(8)
                    .Text("No technician photos are attached to this job card.")
                    .FontSize(9)
                    .FontColor(MidGrey);
                return;
            }

            if (renderablePhotos.Count == 0)
            {
                col.Item()
                    .Border(1)
                    .BorderColor(BorderGrey)
                    .Background("#F5F9FC")
                    .Padding(8)
                    .Text("Technician photos exist, but none could be rendered (missing file or unsupported format).")
                    .FontSize(9)
                    .FontColor(MidGrey);
                return;
            }

            if (renderablePhotos.Count < totalTechnicianPhotoCount)
            {
                var hiddenCount = totalTechnicianPhotoCount - renderablePhotos.Count;
                col.Item().Text($"{hiddenCount} photo(s) omitted from PDF (missing file or unsupported format).")
                    .FontSize(8)
                    .FontColor(MidGrey);
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                for (var i = 0; i < renderablePhotos.Count; i += 2)
                {
                    var left = renderablePhotos[i];
                    table.Cell().Padding(4).Element(c => ComposePhotoCard(c, left));

                    if (i + 1 < renderablePhotos.Count)
                    {
                        var right = renderablePhotos[i + 1];
                        table.Cell().Padding(4).Element(c => ComposePhotoCard(c, right));
                    }
                    else
                    {
                        table.Cell().Padding(4).Element(c =>
                            c.Border(1).BorderColor(BorderGrey).Background(Colors.White).Height(190));
                    }
                }
            });
        });
    }

    private static void ComposePhotoCard(IContainer container, RenderablePhoto photo)
    {
        var label = BuildPhotoLabel(photo.Attachment);
        container.Border(1).BorderColor(BorderGrey).Background(Colors.White).Padding(6).Column(col =>
        {
            col.Item().Height(145).AlignCenter().AlignMiddle().Element(e => e.Image(photo.Image).FitArea());
            col.Item().PaddingTop(4).Text(label)
                .FontSize(8)
                .SemiBold();
            col.Item().Text($"{photo.Attachment.AddedAt.ToLocalTime():yyyy-MM-dd HH:mm} - {ValueOrDash(photo.Attachment.AddedBy)}")
                .FontSize(7)
                .FontColor(MidGrey);
        });
    }

    private static string BuildPhotoLabel(Attachment photo)
    {
        var verificationLabel = ExtractVerificationLabel(photo.Notes);
        var fileName = string.IsNullOrWhiteSpace(photo.FileName) ? null : photo.FileName.Trim();

        if (!string.IsNullOrWhiteSpace(verificationLabel) && !string.IsNullOrWhiteSpace(fileName))
            return $"{verificationLabel}: {fileName}";

        if (!string.IsNullOrWhiteSpace(verificationLabel))
            return verificationLabel;

        if (!string.IsNullOrWhiteSpace(fileName))
            return fileName;

        return "Technician Photo";
    }

    private static string? ExtractVerificationLabel(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var markerStart = notes.IndexOf("[Verification:", StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0)
            return null;

        var markerEnd = notes.IndexOf(']', markerStart);
        if (markerEnd <= markerStart)
            return null;

        var marker = notes.Substring(markerStart, markerEnd - markerStart + 1).Trim();

        return marker.ToLowerInvariant() switch
        {
            "[verification:vehicle]" => "Vehicle Verification",
            "[verification:registration]" => "Registration Verification",
            "[verification:vin]" => "VIN Verification",
            "[verification:trackingunit]" => "Tracking Unit Verification",
            "[verification:serialiccid]" => "Serial/ICCID Verification",
            _ => "Technician Photo"
        };
    }

    private static List<(string Label, string Value)> BuildClientRows(JobCard jobCard)
    {
        return
        [
            ("Company", ValueOrDash(jobCard.Company)),
            ("Fleet Number", ValueOrDash(jobCard.FleetNumber)),
            ("Job Type", jobCard.Type.ToString()),
            ("Status", jobCard.Status.ToString())
        ];
    }

    private static List<(string Label, string Value)> BuildVehicleRows(JobCard jobCard)
    {
        return
        [
            ("Registration", ValueOrDash(jobCard.Registration)),
            ("Grid Location", ValueOrDash(jobCard.GridLocation)),
            ("Make", ValueOrDash(jobCard.Make)),
            ("Model", ValueOrDash(jobCard.Model)),
            ("Colour", ValueOrDash(jobCard.Colour)),
            ("VIN", ValueOrDash(jobCard.VinNumber))
        ];
    }

    private static List<(string Label, string Value)> BuildTrackingRows(JobCard jobCard)
    {
        return
        [
            ("Tracking Unit Make", ValueOrDash(jobCard.TrackingUnitMake)),
            ("IMEI", ValueOrDash(jobCard.Imei)),
            ("Serial Number", ValueOrDash(jobCard.SerialNumber)),
            ("ICCID", ValueOrDash(jobCard.Iccid)),
            ("SIM Number", ValueOrDash(jobCard.SimNumber)),
            ("Inspection Outcome", jobCard.InspectionOutcome.ToString())
        ];
    }

    private static List<(string Label, string Value)> BuildQuoteRows(JobCard jobCard, Quote? quote)
    {
        return
        [
            ("Quote Reference", quote is null ? "-" : QuoteReferenceFormatter.Format(quote.QuoteNumber)),
            ("Quote Status", quote?.Status.ToString() ?? "-"),
            ("Quote Type", quote?.Type.ToString() ?? "-"),
            ("Approved", FormatDateTime(quote?.ApprovedAt))
        ];
    }

    private static string BuildLineItemDescription(QuoteLineItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            var description = item.Description.Trim();
            if (string.Equals(description, "Auto-added monthly fee", StringComparison.OrdinalIgnoreCase))
                return "Monthly fee";

            return description;
        }

        if (!string.IsNullOrWhiteSpace(item.ProductName))
            return item.ProductName.Trim();

        if (!string.IsNullOrWhiteSpace(item.ProductType))
            return item.ProductType.Trim();

        return "Line Item";
    }

    private static void AddQuoteLineHeaderCell(TableDescriptor table, string text)
    {
        table.Cell()
            .Border(0.5f)
            .BorderColor(BorderGrey)
            .Background(LightBackground)
            .Padding(5)
            .AlignCenter()
            .Text(text)
            .FontSize(8)
            .SemiBold()
            .FontColor(MidGrey);
    }

    private static void AddQuoteLineValueCell(TableDescriptor table, string text, TextAlignMode alignment)
    {
        var cell = table.Cell()
            .Border(0.5f)
            .BorderColor(BorderGrey)
            .Padding(5);

        cell = alignment switch
        {
            TextAlignMode.Left => cell.AlignLeft(),
            TextAlignMode.Center => cell.AlignCenter(),
            TextAlignMode.Right => cell.AlignRight(),
            _ => cell
        };

        cell.Text(ValueOrDash(text)).FontSize(8.5f);
    }

    private static List<RenderablePhoto> BuildRenderablePhotos(IReadOnlyList<Attachment> photos)
    {
        if (photos.Count == 0)
            return [];

        var renderable = new List<RenderablePhoto>();

        foreach (var photo in photos.OrderBy(x => x.AddedAt))
        {
            if (string.IsNullOrWhiteSpace(photo.StoredPath))
                continue;

            if (!File.Exists(photo.StoredPath))
                continue;

            if (!SupportedPhotoExtensions.Contains(Path.GetExtension(photo.StoredPath)))
                continue;

            try
            {
                var image = QuestImage.FromFile(photo.StoredPath);
                renderable.Add(new RenderablePhoto
                {
                    Attachment = photo,
                    Image = image
                });
            }
            catch
            {
                // Skip invalid image files to keep PDF generation resilient.
            }
        }

        return renderable;
    }

    private static void AddLabelCell(TableDescriptor table, string text)
    {
        table.Cell()
            .Border(0.5f)
            .BorderColor(BorderGrey)
            .Background(LightBackground)
            .Padding(5)
            .Text(ValueOrDash(text))
            .FontSize(8)
            .SemiBold()
            .FontColor(MidGrey);
    }

    private static void AddValueCell(TableDescriptor table, string text)
    {
        table.Cell()
            .Border(0.5f)
            .BorderColor(BorderGrey)
            .Padding(5)
            .Text(ValueOrDash(text))
            .FontSize(9.5f);
    }

    private void ComposeFooter(IContainer container)
    {
        container.Background(Navy).PaddingVertical(6).PaddingHorizontal(8).Row(row =>
        {
            row.RelativeItem().Text("Capital Air - Tracking Unit Job Card - Confidential")
                .FontSize(7)
                .FontColor("#A8C8E8");

            row.AutoItem().Text(text =>
            {
                text.Span("Page ").FontSize(7).FontColor(Colors.White);
                text.CurrentPageNumber().FontSize(7).FontColor(Colors.White);
            });
        });
    }

    private static string BuildStatusChecklist(JobStatus status)
    {
        var open = status == JobStatus.Open ? "[x]" : "[ ]";
        var completed = status == JobStatus.Completed ? "[x]" : "[ ]";
        var cancelled = status == JobStatus.Cancelled ? "[x]" : "[ ]";

        return $"{open} Open    {completed} Completed    {cancelled} Cancelled";
    }

    private static string FormatDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("dd MMM yyyy HH:mm") : "-";
    }

    private static string FormatCurrency(decimal value)
    {
        return $"R {value:0.00}";
    }

    private static string ValueOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static string? ResolveLogoPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "capital-air-logo.png"),
            Path.Combine(AppContext.BaseDirectory, "capital-air-logo.png"),
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
}
