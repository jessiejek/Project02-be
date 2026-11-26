//JJQR - AI (Nov 21, 2026) cloud sync: push / pull wire types (see Project02-desktop/docs/SYNC-API-CONTRACT.md)
using System.Text.Json;

namespace ClinicApp.Infrastructure.Sync;

public static class SyncStatuses
{
    public const string Applied = "applied";
    public const string Merged = "merged";
    public const string Duplicate = "duplicate";
    public const string Conflict = "conflict";
    public const string Rejected = "rejected";
    public const string Retry = "retry";
}

public static class SyncReasons
{
    public const string UnknownAggregate = "unknown_aggregate";
    public const string CloudOwned = "cloud_owned";
    public const string EntityIdMismatch = "entity_id_mismatch";
    public const string InvalidPayload = "invalid_payload";
    public const string InvalidOperation = "invalid_operation";
    public const string MissingDependency = "missing_dependency";
    public const string FinishedImmutable = "finished_immutable";
    public const string IllegalTransition = "illegal_transition";
    public const string CloudFurther = "cloud_further";
    public const string NotOwner = "not_owner";
    public const string RefundOfflineForbidden = "refund_offline_forbidden";
    public const string AmountMismatch = "amount_mismatch";
    public const string AppendOnly = "append_only";
    public const string AlreadyRecorded = "already_recorded";
    public const string DeleteNotAllowed = "delete_not_allowed";
    public const string PatientCodeTaken = "patient_code_taken";
    public const string CloudEditOverwritten = "cloud_edit_overwritten";
    //JJQR - AI (Nov 21, 2026) amendments + allergies
    public const string StaleAmendment = "stale_amendment";
    public const string PaymentSettled = "payment_settled";
    public const string AllergyUnion = "allergy_union";
    public const string QueueNumberReassigned = "queue_number_reassigned"; // queue-number reconciliation
    //JJQR - END
    //JJQR - AI (21 Nov 2026) the same OR number is on another payment: both are kept as printed and flagged for admin review
    public const string OrNumberClash = "or_number_clash";
    //JJQR - END
    //JJQR - AI (21 Nov 2026) a printed queue number is shared with another booking: both kept as printed (never renumbered), flagged for desk review
    public const string QueueNumberClash = "queue_number_clash";
    //JJQR - END
    //JJQR - AI (2026-11-26) - GAV-11: a visit was moved to another patient on the cloud ("Wrong patient?"); a push never moves it back
    public const string PatientReassigned = "patient_reassigned";
    //JJQR - END
    //JJQR - AI (2026-11-26) - GAV-12: the pushed patient was merged into another record on the cloud; the change went to the kept record (blank fields only, allergies combined)
    public const string PatientMerged = "patient_merged";
    //JJQR - END
    //JJQR - AI (Nov 21, 2026) offline self-edits (doctor / doctor_schedule / doctor_blocked_date)
    public const string ActorRequired = "actor_required";
    public const string ActorUnknown = "actor_unknown";
    public const string ActorNotAllowed = "actor_not_allowed";
    public const string FieldNotAllowed = "field_not_allowed";
    public const string CloudFieldNewer = "cloud_field_newer";
    public const string NaturalKeyRemapped = "natural_key_remapped";
    //JJQR - END
}

public sealed class SyncPushRequest
{
    public List<SyncPushOperation>? Operations { get; set; }
}

public sealed class SyncPushOperation
{
    public Guid OperationId { get; set; }
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string OperationType { get; set; } = "";
    public long? BaseRowVersion { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    //JJQR - AI (Nov 21, 2026) the signed-in node user who made the change (required for doctor / doctor_schedule / doctor_blocked_date)
    public Guid? ActorUserId { get; set; }
    //JJQR - END
    public JsonElement Payload { get; set; }

    public bool IsDelete => string.Equals(OperationType, "delete", StringComparison.OrdinalIgnoreCase);

    /// <summary>payload.data (or an Undefined element).</summary>
    public JsonElement Data =>
        Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty("data", out var d) ? d : default;

    public List<string>? ChangedFields =>
        Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty("changed_fields", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : null;
}

public sealed class SyncOperationResult
{
    public Guid OperationId { get; set; }
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Status { get; set; } = "";
    public long? RowVersion { get; set; }
    public string? ReasonCode { get; set; }
    public string? Message { get; set; }
    public List<string>? ConflictFields { get; set; }
    public object? Current { get; set; }
    public bool Replayed { get; set; }
}

public sealed class SyncPushResponse
{
    public DateTimeOffset ServerTime { get; set; }
    public List<SyncOperationResult> Results { get; set; } = [];
}

public sealed class SyncPullChange
{
    public string Aggregate { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Operation { get; set; } = "upsert";
    public long RowVersion { get; set; }
    public Guid? OriginNodeId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public object? Data { get; set; }
}

public sealed class SyncPullResponse
{
    public DateTimeOffset ServerTime { get; set; }
    public string NextCursor { get; set; } = "0";
    public bool HasMore { get; set; }
    public List<SyncPullChange> Changes { get; set; } = [];
}
//JJQR - END
