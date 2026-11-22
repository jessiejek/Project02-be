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

//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.1: a sync problem an admin has to look at (possible duplicate patient, receipt-number clash, a change the server refused or got stuck on).
// Lives on the server, because the admin web portal reads this database. Merging is a separate admin action (step 4.4).
public class SyncReviewItem
{
    public Guid Id { get; set; }
    /// <summary>See <see cref="ReviewItemKinds"/>.</summary>
    public string Kind { get; set; } = "";
    /// <summary>'open' | 'resolved'.</summary>
    public string Status { get; set; } = ReviewItemStatus.Open;
    public string Title { get; set; } = "";
    public string? DetailsJson { get; set; }
    /// <summary>For a possible duplicate: the patient that arrived and the one it may duplicate.</summary>
    public Guid? PatientId { get; set; }
    public Guid? OtherPatientId { get; set; }
    public string? Aggregate { get; set; }
    public string? EntityId { get; set; }
    public string? OtherEntityId { get; set; }
    public Guid? OriginNodeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    /// <summary>'merged' | 'dismissed' | 'fixed'.</summary>
    public string? Resolution { get; set; }
    public string? ResolutionNote { get; set; }
}

public static class ReviewItemKinds
{
    public const string PossibleDuplicatePatient = "possible_duplicate_patient";
    public const string OrNumberClash = "or_number_clash";
    public const string ChangeRejected = "change_rejected";
    public const string ChangeStuck = "change_stuck";
    public static readonly string[] All = [PossibleDuplicatePatient, OrNumberClash, ChangeRejected, ChangeStuck];
    /// <summary>The kinds a desktop may upload (the server makes the other two itself).</summary>
    public static readonly string[] FromNodes = [ChangeRejected, ChangeStuck];
}

public static class ReviewItemStatus
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class ReviewResolutions
{
    public const string Merged = "merged";
    public const string Dismissed = "dismissed";
    public const string Fixed = "fixed";
}
//JJQR - END

