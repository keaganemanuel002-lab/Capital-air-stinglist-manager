using System;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public static class JobCardReferenceFormatter
{
    public static string Format(JobType type, int jobCardNumber)
    {
        var normalized = Math.Max(0, jobCardNumber);
        var typeSegment = type switch
        {
            JobType.Install => "INS",
            JobType.Removal => "REM",
            JobType.Transfer => "TRF",
            JobType.Inspection => "INSP",
            _ => "JOB"
        };

        return $"JC-{typeSegment}-{normalized:0000}";
    }

    public static string Format(JobCard jobCard)
    {
        return Format(jobCard.Type, jobCard.JobCardNumber);
    }
}
