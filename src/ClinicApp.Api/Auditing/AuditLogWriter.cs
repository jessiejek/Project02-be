using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;

namespace ClinicApp.Api.Auditing;

/// <summary>Central place controllers call to record an audit_logs row. Callers
/// still own db.SaveChangesAsync() — this only stages the row via db.AuditLogs.Add.</summary>
public static class AuditLogWriter
{
    public static void Add(ClinicAppDbContext db, AuditEntityType entityType, Guid entityId, string action, Guid? performedBy, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Details = details,
            PerformedByUserId = performedBy,
            PerformedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Builds a "Field: old → new" details string from only the fields that changed.
    /// Values are formatted compactly and long text is truncated so details stays scannable.</summary>
    public static string? DiffDetails(params (string Field, object? OldValue, object? NewValue)[] fields)
    {
        var changed = fields.Where(f => !Equals(Normalize(f.OldValue), Normalize(f.NewValue))).ToList();
        if (changed.Count == 0) return null;
        return string.Join("; ", changed.Select(f => $"{f.Field}: {Fmt(f.OldValue)} → {Fmt(f.NewValue)}"));
    }

    private static object? Normalize(object? v) => v is string s && s.Length == 0 ? null : v;

    private static string Fmt(object? v)
    {
        if (v is null) return "(none)";
        var s = v.ToString() ?? "(none)";
        return s.Length > 60 ? s[..57] + "..." : s;
    }
}
