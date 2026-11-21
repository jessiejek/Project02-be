//JJQR - AI (Nov 21, 2026) cloud sync: sync columns contract + node / operation / change-log entities
namespace ClinicApp.Domain.Entities;

/// <summary>Sync columns carried by every row the Clinic Node can push (patients, bookings,
/// payments, consultations and their children, vital readings, audit logs).</summary>
public interface ISyncTracked
{
    /// <summary>Incremented on every committed change (web or sync). The node echoes it back
    /// as <c>base_row_version</c>.</summary>
    long RowVersion { get; set; }
    /// <summary>Tombstone marker for rows removed through sync.</summary>
    DateTimeOffset? DeletedAt { get; set; }
    /// <summary>The node that first wrote this row through sync (null = written on the web).
    /// For payments this is the single owning writer.</summary>
    Guid? OriginNodeId { get; set; }
}

/// <summary>A registered Clinic Node (one per clinic). Only a hash of its secret is stored.</summary>
public class SyncNode
{
    public Guid NodeId { get; set; }
    public string Name { get; set; } = "";
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
    public string SecretHash { get; set; } = "";
    /// <summary>'Active' | 'Revoked'.</summary>
    public string Status { get; set; } = SyncNodeStatus.Active;
    public Guid? RegisteredByUserId { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public static class SyncNodeStatus
{
    public const string Active = "Active";
    public const string Revoked = "Revoked";
}

/// <summary>Idempotency log: one row per processed push operation id.</summary>
public class SyncOperation
{
    public Guid OperationId { get; set; }
    public Guid NodeId { get; set; }
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string OperationType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ReasonCode { get; set; }
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset? NodeCreatedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Change log behind the pull cursor. Written in the same SaveChanges as the change.</summary>
public class SyncChange
{
    public long Seq { get; set; }
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public bool Deleted { get; set; }
    public long RowVersion { get; set; }
    /// <summary>Node that made this change (null = web / cloud).</summary>
    public Guid? OriginNodeId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
//JJQR - END

//JJQR - AI (Nov 21, 2026) offline self-edits: per-field last-write time (field-level merge, newest wins)
/// <summary>When one field of a self-editable cloud row (doctor, doctor_schedule, doctor_blocked_date) was last written, by the web
/// or by a node. Lets a node's offline edit win or lose per field instead of per row. Key = (aggregate, entity_id, field).</summary>
public class SyncFieldStamp
{
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Field { get; set; } = "";
    /// <summary>Time of the edit: the node's clock for a synced edit, the cloud clock for a web edit.</summary>
    public DateTimeOffset StampedAt { get; set; }
    public Guid? OriginNodeId { get; set; }
}
//JJQR - END
