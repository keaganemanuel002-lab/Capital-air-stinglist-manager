using StingListManager.Data;
using StingListManager.Data.Entities;

namespace StingListManager.Services;

public class AuditService
{
    public void Log(string actor, string action, string entityType, int? entityId, string? reg, string? details = null)
    {
        using var db = new AppDbContext();
        db.AuditEvents.Add(new AuditEvent
        {
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Registration = reg,
            Details = details
        });
        db.SaveChanges();
    }
}
