using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StingListManager.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingListManager.Services;

public class JobCardPdfService
{
    private readonly string _companyName = "Capital Air (Pty) Ltd";

    public byte[] BuildJobCardPdf(JobCard jobCard)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(11));

                // Header
                page.Header().Column(col =>
                {
                    var jobCardReference = JobCardReferenceFormatter.Format(jobCard);
                    col.Item().Text(_companyName).FontSize(18).SemiBold();
                    col.Item().Text($"Job Card {jobCardReference}").FontSize(14).SemiBold();
                    col.Item().Text(DateTime.Now.ToString("dd MMMM yyyy")).FontSize(10).FontColor(Colors.Grey.Darken1);
                });

                // Main content
                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    col.Item().PaddingBottom(10).LineHorizontal(1f);

                    // Job Type Badge
                    col.Item().Row(row =>
                    {
                        row.AutoItem().Background(jobCard.Type == JobType.Install ? Colors.Green.Lighten2 : Colors.Orange.Lighten2)
                            .Padding(6)
                            .Text(jobCard.Type.ToString().ToUpper())
                            .FontSize(10)
                            .Bold();
                        
                        row.AutoItem().PaddingLeft(10).Background(GetStatusColor(jobCard.Status))
                            .Padding(6)
                            .Text(jobCard.Status.ToString().ToUpper())
                            .FontSize(10)
                            .Bold();
                    });

                    col.Item().PaddingTop(12).Text("CLIENT INFORMATION").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(3f);
                        });

                        table.Cell().Padding(6).Text("Company:").FontSize(10).FontColor(Colors.Grey.Darken2);
                        table.Cell().Padding(6).Text(jobCard.Company ?? "-").FontSize(10);
                        table.Cell().Padding(6).Text("Registration:").FontSize(10).FontColor(Colors.Grey.Darken2);
                        table.Cell().Padding(6).Text(jobCard.Registration ?? "-").FontSize(10);
                        if (!string.IsNullOrWhiteSpace(jobCard.FleetNumber))
                        {
                            table.Cell().Padding(6).Text("Fleet Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.FleetNumber).FontSize(10);
                        }
                    });

                    col.Item().PaddingTop(12).Text("VEHICLE INFORMATION").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(3f);
                        });

                        if (!string.IsNullOrWhiteSpace(jobCard.Make))
                        {
                            table.Cell().Padding(6).Text("Make:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Make).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.Model))
                        {
                            table.Cell().Padding(6).Text("Model:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Model).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.Colour))
                        {
                            table.Cell().Padding(6).Text("Colour:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Colour).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.VinNumber))
                        {
                            table.Cell().Padding(6).Text("VIN:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.VinNumber).FontSize(10);
                        }
                    });

                    col.Item().PaddingTop(12).Text("TRACKING UNIT INFORMATION").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(3f);
                        });

                        if (!string.IsNullOrWhiteSpace(jobCard.TrackingUnitMake))
                        {
                            table.Cell().Padding(6).Text("Tracking Unit Make:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.TrackingUnitMake).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.Imei))
                        {
                            table.Cell().Padding(6).Text("IMEI:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Imei).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.SerialNumber))
                        {
                            table.Cell().Padding(6).Text("Serial Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.SerialNumber).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.Iccid))
                        {
                            table.Cell().Padding(6).Text("ICCID:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Iccid).FontSize(10);
                        }
                        if (!string.IsNullOrWhiteSpace(jobCard.SimNumber))
                        {
                            table.Cell().Padding(6).Text("SIM Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.SimNumber).FontSize(10);
                        }
                    });

                    col.Item().PaddingTop(12).Text("SCHEDULE INFORMATION").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(3f);
                        });

                        table.Cell().Padding(6).Text("Created:").FontSize(10).FontColor(Colors.Grey.Darken2);
                        table.Cell().Padding(6).Text(jobCard.CreatedAt.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                        if (jobCard.ScheduledFor.HasValue)
                        {
                            table.Cell().Padding(6).Text("Scheduled For:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.ScheduledFor.Value.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                        }
                        if (jobCard.CompletedAt.HasValue)
                        {
                            table.Cell().Padding(6).Text("Completed:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.CompletedAt.Value.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                        }
                    });

                    // Notes
                    if (!string.IsNullOrWhiteSpace(jobCard.Notes))
                    {
                        col.Item().PaddingTop(12).Text("NOTES").FontSize(12).Bold();
                        col.Item().Background(Colors.Grey.Lighten3).Padding(10).Text(jobCard.Notes).FontSize(10);
                    }
                });

                // Footer
                page.Footer().Column(col =>
                {
                    var jobCardReference = JobCardReferenceFormatter.Format(jobCard);
                    col.Item().LineHorizontal(1f);
                    col.Item().PaddingTop(6).AlignCenter().Text($"Job Card {jobCardReference} - {_companyName}").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    public byte[] BuildMultipleJobCardsPdf(List<JobCard> jobCards)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            foreach (var jobCard in jobCards)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    // Header
                    page.Header().Column(col =>
                    {
                        var jobCardReference = JobCardReferenceFormatter.Format(jobCard);
                        col.Item().Text(_companyName).FontSize(18).SemiBold();
                        col.Item().Text($"Job Card {jobCardReference}").FontSize(14).SemiBold();
                        col.Item().Text(DateTime.Now.ToString("dd MMMM yyyy")).FontSize(10).FontColor(Colors.Grey.Darken1);
                    });

                    // Main content
                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().PaddingBottom(10).LineHorizontal(1f);

                        // Job Type Badge
                        col.Item().Row(row =>
                        {
                            row.AutoItem().Background(jobCard.Type == JobType.Install ? Colors.Green.Lighten2 : Colors.Orange.Lighten2)
                                .Padding(6)
                                .Text(jobCard.Type.ToString().ToUpper())
                                .FontSize(10)
                                .Bold();
                            
                            row.AutoItem().PaddingLeft(10).Background(GetStatusColor(jobCard.Status))
                                .Padding(6)
                                .Text(jobCard.Status.ToString().ToUpper())
                                .FontSize(10)
                                .Bold();
                        });

                        col.Item().PaddingTop(12).Text("CLIENT INFORMATION").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(3f);
                           });

                            table.Cell().Padding(6).Text("Company:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Company ?? "-").FontSize(10);
                            table.Cell().Padding(6).Text("Registration:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.Registration ?? "-").FontSize(10);
                            if (!string.IsNullOrWhiteSpace(jobCard.FleetNumber))
                            {
                                table.Cell().Padding(6).Text("Fleet Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.FleetNumber).FontSize(10);
                            }
                        });

                        col.Item().PaddingTop(12).Text("VEHICLE INFORMATION").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(3f);
                            });

                            if (!string.IsNullOrWhiteSpace(jobCard.Make))
                            {
                                table.Cell().Padding(6).Text("Make:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.Make).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.Model))
                            {
                                table.Cell().Padding(6).Text("Model:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.Model).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.Colour))
                            {
                                table.Cell().Padding(6).Text("Colour:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.Colour).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.VinNumber))
                            {
                                table.Cell().Padding(6).Text("VIN:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.VinNumber).FontSize(10);
                            }
                        });

                        col.Item().PaddingTop(12).Text("TRACKING UNIT INFORMATION").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(3f);
                            });

                            if (!string.IsNullOrWhiteSpace(jobCard.TrackingUnitMake))
                            {
                                table.Cell().Padding(6).Text("Tracking Unit Make:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.TrackingUnitMake).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.Imei))
                            {
                                table.Cell().Padding(6).Text("IMEI:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.Imei).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.SerialNumber))
                            {
                                table.Cell().Padding(6).Text("Serial Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.SerialNumber).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.Iccid))
                            {
                                table.Cell().Padding(6).Text("ICCID:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.Iccid).FontSize(10);
                            }
                            if (!string.IsNullOrWhiteSpace(jobCard.SimNumber))
                            {
                                table.Cell().Padding(6).Text("SIM Number:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.SimNumber).FontSize(10);
                            }
                        });

                        col.Item().PaddingTop(12).Text("SCHEDULE INFORMATION").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1.5f);
                                columns.RelativeColumn(3f);
                            });

                            table.Cell().Padding(6).Text("Created:").FontSize(10).FontColor(Colors.Grey.Darken2);
                            table.Cell().Padding(6).Text(jobCard.CreatedAt.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                            if (jobCard.ScheduledFor.HasValue)
                            {
                                table.Cell().Padding(6).Text("Scheduled For:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.ScheduledFor.Value.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                            }
                            if (jobCard.CompletedAt.HasValue)
                            {
                                table.Cell().Padding(6).Text("Completed:").FontSize(10).FontColor(Colors.Grey.Darken2);
                                table.Cell().Padding(6).Text(jobCard.CompletedAt.Value.ToString("dd MMMM yyyy HH:mm")).FontSize(10);
                            }
                        });

                        // Notes
                        if (!string.IsNullOrWhiteSpace(jobCard.Notes))
                        {
                            col.Item().PaddingTop(12).Text("NOTES").FontSize(12).Bold();
                            col.Item().Background(Colors.Grey.Lighten3).Padding(10).Text(jobCard.Notes).FontSize(10);
                        }
                    });

                    // Footer
                    page.Footer().Column(col =>
                    {
                        var jobCardReference = JobCardReferenceFormatter.Format(jobCard);
                        col.Item().LineHorizontal(1f);
                        col.Item().PaddingTop(6).AlignCenter().Text($"Job Card {jobCardReference} - {_companyName}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            }
        }).GeneratePdf();
    }

    private string GetStatusColor(JobStatus status)
    {
        return status switch
        {
            JobStatus.Open => Colors.Blue.Lighten2,
            JobStatus.Completed => Colors.Green.Lighten2,
            JobStatus.Cancelled => Colors.Red.Lighten2,
            _ => Colors.Grey.Lighten2
        };
    }
}
