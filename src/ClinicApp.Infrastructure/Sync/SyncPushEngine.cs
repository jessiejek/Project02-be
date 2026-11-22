//JJQR - AI (Nov 21, 2026) cloud sync: applies pushed operations with per-aggregate conflict rules
using System.Text.Json;
using System.Text.RegularExpressions; //JJQR - AI (Nov 21, 2026) queue-number reconciliation // JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicApp.Infrastructure.Sync;

public sealed partial class SyncPushEngine(ClinicAppDbContext db, IPatientCodeAllocator codes)
{
    public const int MaxBatch = 200;

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "update", "upsert", "replace", "delete", "status", "cancel", "confirm", "confirm-waiver", "complete"
    };

    public async Task<List<SyncOperationResult>> PushAsync(Guid nodeId, IReadOnlyList<SyncPushOperation> ops, CancellationToken ct)
    {
        var results = new List<SyncOperationResult>(ops.Count);
        foreach (var op in ops)
            results.Add(await ApplyOneAsync(nodeId, op, ct));
        db.ChangeTracker.Clear();
        return results;
    }

    private async Task<SyncOperationResult> ApplyOneAsync(Guid nodeId, SyncPushOperation op, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.SyncWriterNodeId = null;

        var prior = await db.SyncOperations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == op.OperationId, ct);
        if (prior is not null) return Replay(prior, nodeId, op);

        SyncOperationResult result;
        if (!SyncAggregates.IsKnown(op.Aggregate))
            result = Fail(op, SyncStatuses.Rejected, SyncReasons.UnknownAggregate, $"Unknown aggregate '{op.Aggregate}'.");
        else if (!SyncAggregates.Pushable.Contains(op.Aggregate) && !SyncAggregates.SelfEditable.Contains(op.Aggregate)) //JJQR - AI (Nov 21, 2026) offline self-edits // JJQR - END
            result = Fail(op, SyncStatuses.Rejected, SyncReasons.CloudOwned, $"'{op.Aggregate}' is owned by the cloud and is pull-only.");
        else if (!KnownTypes.Contains(op.OperationType))
            result = Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidOperation, $"Unknown operation_type '{op.OperationType}'.");
        else if (!Guid.TryParse(op.EntityId, out var entityId))
            result = Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "entity_id must be a GUID.");
        else if (!op.IsDelete && op.Data.ValueKind != JsonValueKind.Object)
            result = Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "payload.data must be an object.");
        else
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.SyncWriterNodeId = nodeId;
            try
            {
                result = op.Aggregate switch
                {
                    SyncAggregates.Patient => await PatientAsync(op, entityId, ct),
                    SyncAggregates.Booking => await BookingAsync(op, entityId, ct),
                    SyncAggregates.Payment => await PaymentAsync(nodeId, op, entityId, ct),
                    SyncAggregates.Consultation => await ConsultationAsync(nodeId, op, entityId, ct), //JJQR - AI (Nov 21, 2026) amendments // JJQR - END
                    SyncAggregates.VitalReading => await VitalReadingAsync(op, entityId, ct),
                    SyncAggregates.AuditLog => await AuditLogAsync(op, entityId, ct),
                    SyncAggregates.PatientConsent => await PatientConsentAsync(op, entityId, ct), //JJQR - AI (21 Nov 2026) // JJQR - END
                    //JJQR - AI (Nov 21, 2026) offline self-edits
                    SyncAggregates.Doctor => await DoctorSelfAsync(nodeId, op, entityId, ct),
                    SyncAggregates.DoctorSchedule => await DoctorScheduleSelfAsync(nodeId, op, entityId, ct),
                    SyncAggregates.DoctorBlockedDate => await DoctorBlockedDateSelfAsync(nodeId, op, entityId, ct),
                    SyncAggregates.DoctorDayStatus => await DoctorDayStatusAsync(nodeId, op, entityId, ct), //JJQR - AI (21 Nov 2026) G5 // JJQR - END
                    //JJQR - END
                    _ => Fail(op, SyncStatuses.Rejected, SyncReasons.UnknownAggregate, null)
                };
                if (result.Status == SyncStatuses.Retry)
                {
                    await tx.RollbackAsync(ct);
                    return result;
                }
                db.SyncWriterNodeId = null;
                db.SyncOperations.Add(Log(nodeId, op, result));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (SyncPayloadException ex)
            {
                await tx.RollbackAsync(ct);
                result = Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, ex.Message);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                // A concurrent request may have stored the same operation id first.
                var raced = await db.SyncOperations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == op.OperationId, ct);
                if (raced is not null) return Replay(raced, nodeId, op);
                result = Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, ex.InnerException?.Message ?? ex.Message);
            }
            finally
            {
                db.SyncWriterNodeId = null;
            }
        }

        // Terminal results that did not touch data are still logged so a replay answers the same.
        db.ChangeTracker.Clear();
        db.SyncOperations.Add(Log(nodeId, op, result));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var raced = await db.SyncOperations.AsNoTracking().FirstOrDefaultAsync(o => o.OperationId == op.OperationId, ct);
            if (raced is not null) return Replay(raced, nodeId, op);
        }
        return result;
    }

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.2 (D4): a NEW patient that has the same first name, last name and birthdate as a live patient with another id is applied as sent, and a
    // "possible duplicate patient" review item is written for the admin. Nothing is merged here; merging is an admin action (step 4.4).
    private async Task FlagPossibleDuplicatesAsync(Patient incoming, CancellationToken ct)
    {
        var first = PersonNameKey.Of(incoming.FirstName);
        var last = PersonNameKey.Of(incoming.LastName);
        if (first.Length == 0 || last.Length == 0) return;
        var sameBirthday = await db.Patients.AsNoTracking()
            .Where(x => x.DateOfBirth == incoming.DateOfBirth && x.PatientId != incoming.PatientId && x.DeletedAt == null)
            .Select(x => new { x.PatientId, x.FirstName, x.LastName, x.PatientCode })
            .ToListAsync(ct);
        foreach (var m in sameBirthday.Where(x => PersonNameKey.Of(x.FirstName) == first && PersonNameKey.Of(x.LastName) == last).Take(3))
        {
            var entity = incoming.PatientId.ToString();
            var other = m.PatientId.ToString();
            if (await db.SyncReviewItems.AnyAsync(i => i.Kind == ReviewItemKinds.PossibleDuplicatePatient && i.EntityId == entity && i.OtherEntityId == other, ct)) continue;
            db.SyncReviewItems.Add(new SyncReviewItem
            {
                Id = Guid.NewGuid(), Kind = ReviewItemKinds.PossibleDuplicatePatient, Status = ReviewItemStatus.Open,
                Title = $"Possible duplicate patient: {incoming.FirstName} {incoming.LastName}, born {incoming.DateOfBirth:yyyy-MM-dd}",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { new_patient_code = incoming.PatientCode, existing_patient_code = m.PatientCode, matched_on = "first name, last name and birthdate" }),
                PatientId = incoming.PatientId, OtherPatientId = m.PatientId, Aggregate = "patient", EntityId = entity, OtherEntityId = other,
                OriginNodeId = db.SyncWriterNodeId, CreatedAt = DateTimeOffset.UtcNow,
            });
        }
    }
    //JJQR - END

    // ── patient: field-level merge ────────────────────────────────────────
    private static readonly HashSet<string> PatientFixed = new() { "patient_id", "patient_code", "user_id", "created_at", "updated_at" };

    private async Task<SyncOperationResult> PatientAsync(SyncPushOperation op, Guid id, CancellationToken ct)
    {
        var existing = await db.Patients.FirstOrDefaultAsync(p => p.PatientId == id, ct);
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "Patients cannot be deleted through sync.");
        var values = SyncRows.Read(db, typeof(Patient), op.Data);
        if (IdMismatch(values, "patient_id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);

        if (existing is null)
        {
            var p = new Patient();
            SyncRows.Set(p, values);
            p.PatientId = id;
            if (p.UserId is Guid uid && !await db.Users.AnyAsync(u => u.Id == uid, ct)) p.UserId = null;
            if (p.CreatedAt == default) p.CreatedAt = DateTimeOffset.UtcNow;
            if (p.UpdatedAt == default) p.UpdatedAt = p.CreatedAt;
            List<string>? kept = null;
            var code = p.PatientCode?.Trim() ?? "";
            if (code.Length == 0 || code.StartsWith("MF-T-", StringComparison.OrdinalIgnoreCase)
                || await db.Patients.AnyAsync(x => x.PatientCode == code && x.PatientId != id, ct))
            {
                p.PatientCode = await codes.NextAsync(db, ct);
                kept = ["patient_code"];
            }
            db.Patients.Add(p);
            await FlagPossibleDuplicatesAsync(p, ct); //JJQR - AI (Nov 22, 2026) - step 4.2: applied anyway, an admin is asked to look (never merged by itself) //JJQR - END
            //JJQR - AI (Nov 21, 2026) allergies on a new patient
            if (op.Data.TryGetProperty("allergies", out var newAllergies)) await ApplyAllergiesAsync(id, newAllergies, true, ct);
            //JJQR - END
            await db.SaveChangesAsync(ct);
            return kept is null
                ? Ok(op, p.RowVersion)
                : await Done(op, SyncStatuses.Merged, SyncReasons.PatientCodeTaken, "A cloud patient code was assigned.", kept, ct);
        }

        var incoming = new Patient();
        SyncRows.Set(incoming, values);
        var candidates = values.Keys.Where(p => !PatientFixed.Contains(SyncRows.Name(p)) && !SyncRows.ServerColumns.Contains(SyncRows.Name(p)))
            .Where(p => !SyncRows.SameValue(SyncRows.Get(existing, p), values[p]))
            .ToList();
        var changed = op.ChangedFields;
        if (changed is not null) candidates = candidates.Where(p => changed.Contains(SyncRows.Name(p), StringComparer.OrdinalIgnoreCase)).ToList();

        var keptFields = new List<string>();
        var nodeWins = op.BaseRowVersion == existing.RowVersion
            || (values.Keys.Any(k => SyncRows.Name(k) == "updated_at") && incoming.UpdatedAt >= existing.UpdatedAt);
        var apply = nodeWins ? candidates : [];
        if (!nodeWins) keptFields.AddRange(candidates.Select(c => SyncRows.Name(c)));
        if (values.Keys.Any(k => SyncRows.Name(k) == "patient_code") && incoming.PatientCode != existing.PatientCode)
            keptFields.Add("patient_code");

        foreach (var p in apply) p.PropertyInfo!.SetValue(existing, values[p]);
        //JJQR - AI (Nov 21, 2026) allergies: replace-set when the node saw the latest cloud copy, otherwise union (never lose an allergy)
        var allergyUnion = false;
        if (op.Data.TryGetProperty("allergies", out var allergiesEl))
        {
            allergyUnion = await ApplyAllergiesAsync(id, allergiesEl, op.BaseRowVersion == existing.RowVersion, ct);
            if (allergyUnion) keptFields.Add("allergies");
        }
        if (apply.Count > 0 || db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        //JJQR - END
        return keptFields.Count == 0
            ? Ok(op, existing.RowVersion)
            : await Done(op, SyncStatuses.Merged, allergyUnion && keptFields.Count == 1 ? SyncReasons.AllergyUnion : null,
                allergyUnion && keptFields.Count == 1 ? "Cloud allergies were kept and merged with the node's list." : "Some fields kept the cloud value.", keptFields, ct);
    }

    // ── booking: state machine decides ────────────────────────────────────
    private static readonly HashSet<string> BookingFixed = new() { "booking_id", "status", "created_at", "created_by_user_id", "updated_at" };

    private async Task<SyncOperationResult> BookingAsync(SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "Cancel the booking instead of deleting it.");
        if (!op.Data.TryGetProperty("booking", out var row)) throw new SyncPayloadException("data.booking is required.");
        var values = SyncRows.Read(db, typeof(Booking), row);
        if (IdMismatch(values, "booking_id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var hasServices = op.Data.TryGetProperty("booking_services", out var servicesEl) && servicesEl.ValueKind == JsonValueKind.Array;

        var existing = await db.Bookings.Include(b => b.BookingServices).FirstOrDefaultAsync(b => b.BookingId == id, ct);
        var incoming = new Booking();
        SyncRows.Set(incoming, values);

        if (existing is null)
        {
            if (!await db.Patients.AnyAsync(p => p.PatientId == incoming.PatientId, ct)
                || !await db.Doctors.AnyAsync(d => d.DoctorId == incoming.DoctorId, ct))
                return Fail(op, SyncStatuses.Retry, SyncReasons.MissingDependency, "Patient or doctor not in the cloud yet.");
            incoming.BookingId = id;
            if (incoming.CreatedAt == default) incoming.CreatedAt = DateTimeOffset.UtcNow;
            if (incoming.UpdatedAt == default) incoming.UpdatedAt = incoming.CreatedAt;
            //JJQR - AI (Nov 21, 2026) queue numbers: keep the node's number unless a live booking already holds it (contract §5.2)
            var (queueNo, queueMoved) = await ReconcileQueueNumberAsync(id, incoming.AppointmentDate, incoming.PatientId, incoming.DiscountCategory, incoming.QueueNumber, ct);
            incoming.QueueNumber = queueNo;
            //JJQR - END
            db.Bookings.Add(incoming);
            if (hasServices && await ReplaceServicesAsync(incoming, servicesEl, ct) is { } missing) return missing(op);
            await db.SaveChangesAsync(ct);
            //JJQR - AI (Nov 21, 2026) queue numbers
            if (queueMoved)
                return await Done(op, SyncStatuses.Merged, SyncReasons.QueueNumberReassigned,
                    "The cloud issued a different queue number; update the local booking and slip.", ["queue_number"], ct);
            //JJQR - END
            //JJQR - AI (21 Nov 2026) accepted with the printed number; a clash is flagged, never renumbered
            var createdOk = Ok(op, incoming.RowVersion);
            if (await FlagQueueClashAsync(incoming, ct)) { createdOk.ReasonCode = SyncReasons.QueueNumberClash; createdOk.Message = "Queue number already used by another booking that day; kept as printed and flagged for desk review."; }
            return createdOk;
            //JJQR - END
        }

        var kept = new List<string>();
        string? reason = null;
        var queueClash = false; //JJQR - AI (21 Nov 2026) //JJQR - END
        if (values.Keys.Any(k => SyncRows.Name(k) == "status") && incoming.Status != existing.Status)
        {
            if (BookingStatusMachine.CanTransition(existing.Status, incoming.Status))
                existing.Status = incoming.Status;
            else if (BookingStatusMachine.CanTransition(incoming.Status, existing.Status))
            {
                kept.Add("status");
                reason = SyncReasons.CloudFurther;
            }
            else
                return await Done(op, SyncStatuses.Conflict, SyncReasons.IllegalTransition,
                    $"Booking cannot move {existing.Status} → {incoming.Status}; cloud kept.", null, ct);
        }

        var nodeWins = op.BaseRowVersion == existing.RowVersion
            || (values.Keys.Any(k => SyncRows.Name(k) == "updated_at") && incoming.UpdatedAt >= existing.UpdatedAt);
        var others = values.Keys.Where(p => !BookingFixed.Contains(SyncRows.Name(p)) && !SyncRows.ServerColumns.Contains(SyncRows.Name(p)))
            .Where(p => !SyncRows.SameValue(SyncRows.Get(existing, p), values[p])).ToList();
        //JJQR - AI (Nov 21, 2026) queue numbers: a changed queue_number is taken only if the node saw the current cloud copy, then reconciled for duplicates
        if (others.FirstOrDefault(p => SyncRows.Name(p) == "queue_number") is { } queueProp)
        {
            others.Remove(queueProp);
            if (op.BaseRowVersion == existing.RowVersion)
            {
                var (qn, moved) = await ReconcileQueueNumberAsync(id, existing.AppointmentDate, existing.PatientId, existing.DiscountCategory, incoming.QueueNumber, ct);
                existing.QueueNumber = qn;
                //JJQR - AI (21 Nov 2026) never renumbered: flag a clash instead
                if (moved) { kept.Add("queue_number"); reason ??= SyncReasons.QueueNumberReassigned; }
                else if (await FlagQueueClashAsync(existing, ct)) queueClash = true;
                //JJQR - END
            }
            else kept.Add("queue_number");
        }
        //JJQR - END
        if (nodeWins)
        {
            foreach (var p in others) p.PropertyInfo!.SetValue(existing, values[p]);
            if (hasServices && await ReplaceServicesAsync(existing, servicesEl, ct) is { } missing) return missing(op);
        }
        else kept.AddRange(others.Select(p => SyncRows.Name(p)));

        await db.SaveChangesAsync(ct);
        //JJQR - AI (21 Nov 2026) accepted as printed + flagged: applied with reason queue_number_clash (like or_number_clash)
        if (kept.Count == 0 && queueClash)
        {
            var flagged = Ok(op, existing.RowVersion);
            flagged.ReasonCode = SyncReasons.QueueNumberClash; flagged.Message = "Queue number already used by another booking that day; kept as printed and flagged for desk review.";
            return flagged;
        }
        //JJQR - END
        return kept.Count == 0
            ? Ok(op, existing.RowVersion)
            : await Done(op, SyncStatuses.Merged, reason, "Some fields kept the cloud value.", kept, ct);
    }

    //JJQR - AI (Nov 21, 2026) queue-number reconciliation
    private static readonly Regex QueueNo = new(@"^(?<lane>[A-Za-z])-(?<seq>\d{1,6})$", RegexOptions.Compiled);
    private static readonly HashSet<BookingStatus> QueueLive = new() { BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.OnHold, BookingStatus.Completed };

    /// <summary>The node issues queue numbers while offline; the cloud may have issued the same number to an online booking.
    /// Rule: the node's number stays with its booking, and the cloud counter is raised to at least that number so it is never issued again.
    /// If another booking of the same day already holds the number: a cloud-created booking that nobody has been served under
    /// (not CheckedIn / InProgress / OnHold / Completed) is renumbered to the next free number of its lane (audited, reaches nodes on pull);
    /// otherwise (a live booking, or another node's) the incoming booking gets the next free number (result `merged`, `queue_number_reassigned`).
    /// Returns the number the booking must keep and whether it differs from what the node sent.</summary>
    private async Task<(string Number, bool Reassigned)> ReconcileQueueNumberAsync(
        Guid bookingId, DateOnly day, Guid patientId, string? discountCategory, string? wantedRaw, CancellationToken ct)
    {
        var wanted = wantedRaw?.Trim() ?? "";
        if (wanted.Length == 0)
            return (await QueueSequencer.NextForPatientAsync(db, day, patientId, discountCategory, ct), true);

        var m = QueueNo.Match(wanted);
        var lane = m.Success ? m.Groups["lane"].Value.ToUpperInvariant() : null;
        if (m.Success && lane is "R" or "S") await EnsureCounterAtLeastAsync(day, lane!, int.Parse(m.Groups["seq"].Value), ct);

        //JJQR - AI (21 Nov 2026) Jessie: a printed queue number is NEVER renumbered, even if it clashes with a live visit or another computer's booking.
        // The node's number is kept as-is (the counter was already raised above); a clash is flagged by FlagQueueClashAsync (audit rows) for the desk to sort out.
        // The old renumber-on-clash code (move an unserved cloud booking, or give the incoming booking the next free number) is intentionally not used any more.
        return (wanted, false);
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) queue number clash: another booking of the same day already holds this number -> keep BOTH as printed and write an audit row on each (ids only,
    // once per pair). Returns true when a clash exists. Never changes, drops or renumbers a booking. No schema change (same approach as FlagOrClashAsync).
    private async Task<bool> FlagQueueClashAsync(Booking b, CancellationToken ct)
    {
        var number = b.QueueNumber?.Trim();
        if (string.IsNullOrEmpty(number)) return false;
        var others = await db.Bookings.AsNoTracking()
            .Where(x => x.AppointmentDate == b.AppointmentDate && x.QueueNumber == number && x.BookingId != b.BookingId)
            .Select(x => new { x.BookingId, x.OriginNodeId, x.Status }).ToListAsync(ct);
        if (others.Count == 0) return false;

        const string action = "Queue number clash";
        var already = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == AuditEntityType.Booking && a.Action == action)
            .Select(a => new { a.EntityId, a.Details }).ToListAsync(ct);
        bool Logged(Guid on, Guid other) => already.Any(a => a.EntityId == on && a.Details != null && a.Details.Contains(other.ToString()));
        string Origin(Guid? o) => o is null ? "cloud" : $"node {o}";
        var writer = db.SyncWriterNodeId;
        foreach (var o in others)
        {
            if (!Logged(b.BookingId, o.BookingId))
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(), EntityType = AuditEntityType.Booking, EntityId = b.BookingId, Action = action, PerformedAt = DateTimeOffset.UtcNow,
                    Details = $"Queue number {number} is also on booking {o.BookingId} ({Origin(o.OriginNodeId)}, {o.Status}) on {b.AppointmentDate:yyyy-MM-dd}; this booking (node {writer}) kept the printed number. Needs desk review."
                });
            if (!Logged(o.BookingId, b.BookingId))
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(), EntityType = AuditEntityType.Booking, EntityId = o.BookingId, Action = action, PerformedAt = DateTimeOffset.UtcNow,
                    Details = $"Queue number {number} is also on booking {b.BookingId} (node {writer}). Needs desk review."
                });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
    //JJQR - END

    private async Task EnsureCounterAtLeastAsync(DateOnly day, string lane, int seq, CancellationToken ct)
    {
        var c = await db.QueueCounters.AsNoTracking().SingleOrDefaultAsync(x => x.Date == day && x.Lane == lane, ct);
        if (c is null)
        {
            var row = new QueueCounter { Date = day, Lane = lane, Value = seq };
            db.QueueCounters.Add(row);
            await db.SaveChangesAsync(ct);
            db.Entry(row).State = EntityState.Detached;
        }
        else if (c.Value < seq)
            await db.QueueCounters.Where(x => x.Date == day && x.Lane == lane && x.Value < seq)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Value, seq), ct);
    }
    //JJQR - END

    private async Task<Func<SyncPushOperation, SyncOperationResult>?> ReplaceServicesAsync(Booking booking, JsonElement arr, CancellationToken ct)
    {
        var wanted = new List<BookingService>();
        foreach (var el in arr.EnumerateArray())
        {
            var s = SyncRows.New<BookingService>(db, el);
            s.BookingId = booking.BookingId;
            wanted.Add(s);
        }
        var ids = wanted.Select(w => w.ServiceId).ToList();
        if (await db.Services.CountAsync(s => ids.Contains(s.ServiceId), ct) != ids.Distinct().Count())
            return o => Fail(o, SyncStatuses.Retry, SyncReasons.MissingDependency, "A booked service is not in the cloud yet.");
        foreach (var old in booking.BookingServices.ToList())
        {
            var w = wanted.FirstOrDefault(x => x.ServiceId == old.ServiceId);
            if (w is null) db.BookingServices.Remove(old);
            else { old.PriceAtBooking = w.PriceAtBooking; wanted.Remove(w); }
        }
        foreach (var w in wanted) db.BookingServices.Add(w);
        return null;
    }

    // ── payment: single writer ────────────────────────────────────────────
    private static readonly HashSet<string> PaymentFixed = new()
    {
        "payment_id", "booking_id", "created_at", "updated_at",
        "refunded_by_user_id", "refund_amount", "refund_reason", "refunded_at"
    };

    private async Task<SyncOperationResult> PaymentAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "Payments cannot be deleted.");
        var values = SyncRows.Read(db, typeof(Payment), op.Data);
        if (IdMismatch(values, "payment_id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var incoming = new Payment();
        SyncRows.Set(incoming, values);

        if (incoming.Status == PaymentStatus.Refunded)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.RefundOfflineForbidden, "Refunds are recorded online by an admin only.");
        if (incoming.Status == PaymentStatus.Paid && incoming.AmountReceived is decimal got && got != incoming.Amount)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.AmountMismatch, "Amount received must equal the amount due.");

        var existing = await db.Payments.FirstOrDefaultAsync(p => p.PaymentId == id, ct);
        if (existing is null)
        {
            if (!await db.Bookings.AnyAsync(b => b.BookingId == incoming.BookingId, ct))
                return Fail(op, SyncStatuses.Retry, SyncReasons.MissingDependency, "Booking not in the cloud yet.");
            var other = await db.Payments.FirstOrDefaultAsync(p => p.BookingId == incoming.BookingId, ct);
            if (other is not null)
            {
                if (other.Status != PaymentStatus.Unpaid || other.ConfirmedAt is not null)
                    return await DoneFor(op, SyncAggregates.Payment, other.PaymentId, SyncStatuses.Conflict, SyncReasons.CloudFurther,
                        "The cloud already recorded a payment for this booking.", ct);
                db.Payments.Remove(other);
                await db.SaveChangesAsync(ct);
            }
            incoming.PaymentId = id;
            incoming.OriginNodeId = nodeId;
            if (incoming.CreatedAt == default) incoming.CreatedAt = DateTimeOffset.UtcNow;
            if (incoming.UpdatedAt == default) incoming.UpdatedAt = incoming.CreatedAt;
            db.Payments.Add(incoming);
            await db.SaveChangesAsync(ct);
            var created = Ok(op, incoming.RowVersion);
            //JJQR - AI (21 Nov 2026) accepted as printed; a duplicate OR is flagged, never dropped or renumbered
            if (await FlagOrClashAsync(incoming, nodeId, ct)) { created.ReasonCode = SyncReasons.OrNumberClash; created.Message = "OR number already used by another payment; kept as printed and flagged for admin review."; }
            //JJQR - END
            return created;
        }

        if (existing.OriginNodeId is Guid owner && owner != nodeId)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.NotOwner, "Another node owns this payment.");
        if (incoming.Status != existing.Status && !PaymentStatusMachine.CanTransition(existing.Status, incoming.Status))
        {
            var reason = PaymentStatusMachine.CanTransition(incoming.Status, existing.Status) || existing.Status == PaymentStatus.Refunded
                ? SyncReasons.CloudFurther : SyncReasons.IllegalTransition;
            return await Done(op, SyncStatuses.Conflict, reason, $"Payment cannot move {existing.Status} → {incoming.Status}; cloud kept.", null, ct);
        }

        var cloudEdited = op.BaseRowVersion is long b && b < existing.RowVersion;
        foreach (var p in values.Keys.Where(p => !PaymentFixed.Contains(SyncRows.Name(p))))
            if (!SyncRows.ServerColumns.Contains(SyncRows.Name(p))) p.PropertyInfo!.SetValue(existing, values[p]);
        existing.OriginNodeId = nodeId; // first node to write it becomes the single writer
        if (cloudEdited)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), EntityType = AuditEntityType.Payment, EntityId = existing.PaymentId,
                Action = "Sync conflict flagged", PerformedAt = DateTimeOffset.UtcNow,
                Details = $"Node {nodeId} overwrote cloud edits (base row_version {op.BaseRowVersion}, cloud {existing.RowVersion})."
            });
        }
        await db.SaveChangesAsync(ct);
        var r = Ok(op, existing.RowVersion);
        //JJQR - AI (21 Nov 2026) accepted as printed; a duplicate OR is flagged, never dropped or renumbered
        var orClash = await FlagOrClashAsync(existing, nodeId, ct);
        if (orClash) { r.ReasonCode = SyncReasons.OrNumberClash; r.Message = "OR number already used by another payment; kept as printed and flagged for admin review."; }
        //JJQR - END
        if (cloudEdited && !orClash) { r.ReasonCode = SyncReasons.CloudEditOverwritten; r.Message = "Cloud edits were overwritten and flagged for review."; } //JJQR - AI (21 Nov 2026) // JJQR - END
        return r;
    }

    // ── consultation: node wins until finished, then immutable ───────────
    private async Task<SyncOperationResult> ConsultationAsync(Guid nodeId, SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.DeleteNotAllowed, "Consultations cannot be deleted.");
        if (!op.Data.TryGetProperty("consultation", out var row)) throw new SyncPayloadException("data.consultation is required.");
        var values = SyncRows.Read(db, typeof(Consultation), row);
        if (IdMismatch(values, "consultation_id", id)) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        var incoming = new Consultation();
        SyncRows.Set(incoming, values);
        incoming.ConsultationId = id;

        var existing = await db.Consultations.FirstOrDefaultAsync(c => c.ConsultationId == id, ct);
        if (existing is null)
        {
            if (!await db.Bookings.AnyAsync(b => b.BookingId == incoming.BookingId, ct))
                return Fail(op, SyncStatuses.Retry, SyncReasons.MissingDependency, "Booking not in the cloud yet.");
            var sameBooking = await db.Consultations.FirstOrDefaultAsync(c => c.BookingId == incoming.BookingId, ct);
            if (sameBooking is not null)
            {
                if (IsFinished(sameBooking.Status))
                    return await DoneFor(op, SyncAggregates.Consultation, sameBooking.ConsultationId, SyncStatuses.Conflict,
                        SyncReasons.FinishedImmutable, "This visit's consultation is already completed in the cloud.", ct);
                await RemoveConsultationAsync(sameBooking, ct); // cloud draft loses to the node's consultation
            }
            if (incoming.CreatedAt == default) incoming.CreatedAt = DateTimeOffset.UtcNow;
            if (incoming.UpdatedAt == default) incoming.UpdatedAt = incoming.CreatedAt;
            db.Consultations.Add(incoming);
            await db.SaveChangesAsync(ct);
            await ReplaceChildrenAsync(incoming, op.Data, ct);
            await db.SaveChangesAsync(ct);
            return Ok(op, incoming.RowVersion);
        }

        //JJQR - AI (Nov 21, 2026) a finished consultation only changes through an amendment (status Amended)
        if (IsFinished(existing.Status))
            return await AmendAsync(nodeId, op, existing, incoming, values, ct);
        //JJQR - END

        SyncRows.Set(existing, values, col => col is not ("consultation_id" or "booking_id" or "created_at"));
        db.Entry(existing).State = EntityState.Modified;
        await ReplaceChildrenAsync(existing, op.Data, ct);
        await db.SaveChangesAsync(ct);
        return Ok(op, existing.RowVersion);
    }

    //JJQR - AI (Nov 21, 2026) amendments + allergies
    private static readonly HashSet<string> AmendFixed = new()
    {
        "consultation_id", "booking_id", "patient_id", "doctor_id", "created_at", "updated_at", "completed_at", "completed_by_user_id"
    };

    /// <summary>A finished (Completed / Amended) consultation is immutable except through an amendment: the node sends the full
    /// snapshot with status Amended. It is applied only when the node saw the latest cloud copy (base_row_version = cloud
    /// row_version, or the node itself was the last writer); a fee decision that contradicts a settled payment is refused.
    /// Every applied amendment is written to the audit log (which sections changed, no clinical text).</summary>
    private async Task<SyncOperationResult> AmendAsync(Guid nodeId, SyncPushOperation op, Consultation existing, Consultation incoming,
        Dictionary<IProperty, object?> values, CancellationToken ct)
    {
        if (incoming.Status != ConsultationStatus.Amended)
            return await Done(op, SyncStatuses.Conflict, SyncReasons.FinishedImmutable,
                "Consultation is already completed in the cloud; finished consultations are immutable (send status Amended to amend it).", null, ct);

        bool Has(string col) => values.Keys.Any(k => SyncRows.Name(k) == col);
        if ((Has("patient_id") && incoming.PatientId != existing.PatientId) || (Has("doctor_id") && incoming.DoctorId != existing.DoctorId)
            || (Has("booking_id") && incoming.BookingId != existing.BookingId))
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "patient_id, doctor_id and booking_id cannot change in an amendment.");

        var idText = SyncAggregates.Id(existing.ConsultationId);
        var lastWriter = await db.SyncChanges.AsNoTracking()
            .Where(c => c.Aggregate == SyncAggregates.Consultation && c.EntityId == idText)
            .OrderByDescending(c => c.Seq).Select(c => c.OriginNodeId).FirstOrDefaultAsync(ct);
        var sawLatest = op.BaseRowVersion == existing.RowVersion || lastWriter == nodeId;
        if (!sawLatest)
            return await Done(op, SyncStatuses.Conflict, SyncReasons.StaleAmendment,
                "The cloud copy changed since the node's version; pull it and amend again.", null, ct);

        if (Has("pf_decision"))
        {
            var newDecision = string.IsNullOrWhiteSpace(incoming.PfDecision) ? null : incoming.PfDecision.Trim();
            var pay = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == existing.BookingId, ct);
            if (existing.PfDecision is not null && pay is not null && pay.Status != PaymentStatus.Unpaid)
            {
                var changedDecision = !string.Equals(existing.PfDecision, newDecision, StringComparison.Ordinal);
                var bad = (changedDecision && ((pay.Status == PaymentStatus.Waived && newDecision == "Charge")
                            || (pay.Status is PaymentStatus.Paid or PaymentStatus.Refunded && newDecision == "Waive")))
                    || (pay.Status == PaymentStatus.Paid && newDecision == "Charge" && incoming.PfAmount is { } amt && amt != pay.Amount);
                if (bad)
                    return await Done(op, SyncStatuses.Conflict, SyncReasons.PaymentSettled,
                        "The professional fee is already settled in the cloud; the amendment would contradict it.", null, ct);
            }
        }

        var before = await SyncDocuments.BuildAsync(db, SyncAggregates.Consultation, idText, ct);
        var beforeJson = JsonSerializer.SerializeToElement(before!.Data, SyncJson.Options);
        SyncRows.Set(existing, values, col => !AmendFixed.Contains(col));
        existing.Status = ConsultationStatus.Amended;
        await ReplaceChildrenAsync(existing, op.Data, ct);
        await db.SaveChangesAsync(ct);

        var after = await SyncDocuments.BuildAsync(db, SyncAggregates.Consultation, idText, ct);
        var afterJson = JsonSerializer.SerializeToElement(after!.Data, SyncJson.Options);
        var sections = afterJson.EnumerateObject()
            .Where(p => !beforeJson.TryGetProperty(p.Name, out var b) || b.GetRawText() != p.Value.GetRawText())
            .Select(p => p.Name).ToList();
        if (sections.Count == 0)
            { var dup = Done0(op, SyncStatuses.Duplicate, existing.RowVersion); dup.Message = "The cloud already has this amendment."; return dup; }

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), EntityType = AuditEntityType.Consultation, EntityId = existing.ConsultationId,
            Action = "Amended", PerformedAt = DateTimeOffset.UtcNow,
            Details = $"Amended via sync from node {nodeId}. Changed: {string.Join(", ", sections)}."
        });
        await db.SaveChangesAsync(ct);
        return Ok(op, existing.RowVersion);
    }

    private sealed record AllergyEntry(Guid? Id, string Allergen, string? Reaction, string? Severity, bool IsNkda);

    private static string AllergyKey(AllergyEntry a) => a.IsNkda ? "\0nkda" : a.Allergen.Trim().ToLowerInvariant();

    /// <summary>Applies the node's allergy list for a patient. If the node saw the latest cloud copy its list replaces the cloud's.
    /// Otherwise the lists are united by allergen (the node's detail wins for a shared allergen): an allergy is never lost to a
    /// stale push. A real allergen always removes the "NKDA" (no known drug allergies) row. Returns true when the result
    /// kept something the node's list did not contain.</summary>
    private async Task<bool> ApplyAllergiesAsync(Guid patientId, JsonElement arr, bool nodeSawLatest, CancellationToken ct)
    {
        if (arr.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
            throw new SyncPayloadException("allergies must be an array.");
        var node = new List<AllergyEntry>();
        if (arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
            {
                var row = SyncRows.New<PatientAllergy>(db, el);
                var nkda = row.IsNkda;
                var name = nkda ? "NKDA" : (row.Allergen ?? "").Trim();
                if (name.Length == 0) continue;
                if (name.Length > 200) throw new SyncPayloadException("allergen is too long.");
                node.Add(new AllergyEntry(row.Id == Guid.Empty ? null : row.Id, name,
                    string.IsNullOrWhiteSpace(row.Reaction) ? null : row.Reaction.Trim(),
                    string.IsNullOrWhiteSpace(row.Severity) ? null : row.Severity.Trim(), nkda));
            }
        node = node.GroupBy(AllergyKey).Select(g => g.First()).ToList();

        var cloud = await db.PatientAllergies.Where(a => a.PatientId == patientId).ToListAsync(ct);
        var final = new List<(AllergyEntry Entry, PatientAllergy? Match)>();
        foreach (var n in node)
            final.Add((n, cloud.FirstOrDefault(c => n.Id is Guid g && c.Id == g && c.PatientId == patientId)
                ?? cloud.FirstOrDefault(c => AllergyKey(new AllergyEntry(c.Id, c.Allergen, c.Reaction, c.Severity, c.IsNkda)) == AllergyKey(n))));
        if (!nodeSawLatest)
            foreach (var c in cloud.Where(c => final.All(f => f.Match != c)))
                final.Add((new AllergyEntry(c.Id, c.Allergen, c.Reaction, c.Severity, c.IsNkda), c));
        if (final.Any(f => !f.Entry.IsNkda)) final.RemoveAll(f => f.Entry.IsNkda);

        var merged = !nodeSawLatest && !final.Select(f => AllergyKey(f.Entry)).ToHashSet().SetEquals(node.Select(AllergyKey));
        var keep = final.Where(f => f.Match is not null).Select(f => f.Match!).ToHashSet();
        db.PatientAllergies.RemoveRange(cloud.Where(c => !keep.Contains(c)));

        var now = DateTimeOffset.UtcNow;
        var used = new HashSet<Guid>(cloud.Select(c => c.Id));
        foreach (var (e, match) in final)
        {
            if (match is not null)
            {
                if (node.Contains(e)) // the node's detail wins for an allergy it also has
                {
                    var changed = match.Allergen != e.Allergen || match.Reaction != e.Reaction || match.Severity != e.Severity || match.IsNkda != e.IsNkda;
                    match.Allergen = e.Allergen; match.Reaction = e.Reaction; match.Severity = e.Severity; match.IsNkda = e.IsNkda;
                    if (changed) match.UpdatedAt = now;
                }
                continue;
            }
            var newId = e.Id is Guid g && g != Guid.Empty && !used.Contains(g)
                && !await db.PatientAllergies.AsNoTracking().AnyAsync(x => x.Id == g, ct) ? g : Guid.NewGuid();
            used.Add(newId);
            db.PatientAllergies.Add(new PatientAllergy
            {
                Id = newId, PatientId = patientId, Allergen = e.Allergen, Reaction = e.Reaction, Severity = e.Severity,
                IsNkda = e.IsNkda, CreatedAt = now, UpdatedAt = now
            });
        }
        return merged;
    }
    //JJQR - END

    private static bool IsFinished(ConsultationStatus s) => s is ConsultationStatus.Completed or ConsultationStatus.Amended;

    private async Task RemoveConsultationAsync(Consultation c, CancellationToken ct)
    {
        db.ConsultationDiagnoses.RemoveRange(await db.ConsultationDiagnoses.Where(x => x.ConsultationId == c.ConsultationId).ToListAsync(ct));
        db.LabOrders.RemoveRange(await db.LabOrders.Where(x => x.ConsultationId == c.ConsultationId).ToListAsync(ct));
        db.FollowUps.RemoveRange(await db.FollowUps.Where(x => x.ConsultationId == c.ConsultationId).ToListAsync(ct));
        db.MedicalCertificates.RemoveRange(await db.MedicalCertificates.Where(x => x.ConsultationId == c.ConsultationId).ToListAsync(ct));
        foreach (var v in await db.PatientVaccinations.Where(x => x.ConsultationId == c.ConsultationId).ToListAsync(ct)) v.ConsultationId = null;
        db.Consultations.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Children are replaced as a set. A missing key = leave the cloud's set alone;
    /// an explicit null / empty array = remove them.</summary>
    private async Task ReplaceChildrenAsync(Consultation c, JsonElement doc, CancellationToken ct)
    {
        var cid = c.ConsultationId;
        if (doc.TryGetProperty("diagnoses", out var dx))
            await ReplaceSetAsync(await db.ConsultationDiagnoses.Where(x => x.ConsultationId == cid).ToListAsync(ct), dx,
                x => x.Id, x => x.ConsultationId = cid, ct);
        if (doc.TryGetProperty("lab_orders", out var labs))
            await ReplaceSetAsync(await db.LabOrders.Where(x => x.ConsultationId == cid).ToListAsync(ct), labs,
                x => x.LabOrderId, x => x.ConsultationId = cid, ct);
        if (doc.TryGetProperty("vaccinations", out var vax))
            await ReplaceSetAsync(await db.PatientVaccinations.Where(x => x.ConsultationId == cid).ToListAsync(ct), vax,
                x => x.Id, x => x.ConsultationId = cid, ct);
        if (doc.TryGetProperty("follow_up", out var fu))
            await ReplaceSetAsync(await db.FollowUps.Where(x => x.ConsultationId == cid).ToListAsync(ct), AsArray(fu),
                x => x.Id, x => x.ConsultationId = cid, ct);
        if (doc.TryGetProperty("medical_certificate", out var mc))
            await ReplaceSetAsync(await db.MedicalCertificates.Where(x => x.ConsultationId == cid).ToListAsync(ct), AsArray(mc),
                x => x.CertificateId, x => x.ConsultationId = cid, ct);
        if (doc.TryGetProperty("prescription", out var rx))
        {
            var groups = await db.PrescriptionGroups.Where(g => g.BookingId == c.BookingId).ToListAsync(ct);
            JsonElement groupEl = default, linesEl = default;
            var hasGroup = rx.ValueKind == JsonValueKind.Object && rx.TryGetProperty("group", out groupEl) && groupEl.ValueKind == JsonValueKind.Object;
            await ReplaceSetAsync(groups, hasGroup ? AsArray(groupEl) : AsArray(default), g => g.GroupId, g => g.BookingId = c.BookingId, ct);
            if (hasGroup)
            {
                var gid = SyncRows.New<PrescriptionGroup>(db, groupEl).GroupId;
                var hasLines = rx.TryGetProperty("line_items", out linesEl);
                await ReplaceSetAsync(await db.PrescriptionLineItems.Where(l => l.GroupId == gid).ToListAsync(ct),
                    hasLines ? linesEl : AsArray(default), l => l.Id, l => l.GroupId = gid, ct);
            }
        }
    }

    private static JsonElement AsArray(JsonElement el)
    {
        var json = el.ValueKind == JsonValueKind.Object ? $"[{el.GetRawText()}]" : "[]";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task ReplaceSetAsync<T>(List<T> existing, JsonElement arr, Func<T, Guid> key, Action<T> enforce, CancellationToken ct)
        where T : class, new()
    {
        var incoming = new List<(T Entity, Dictionary<IProperty, object?> Values)>();
        if (arr.ValueKind == JsonValueKind.Array)
            foreach (var el in arr.EnumerateArray())
            {
                var values = SyncRows.Read(db, typeof(T), el);
                var e = new T();
                SyncRows.Set(e, values);
                enforce(e);
                incoming.Add((e, values));
            }
        var keep = incoming.Select(i => key(i.Entity)).ToHashSet();
        var removed = existing.Where(x => !keep.Contains(key(x))).ToList();
        if (removed.Count > 0)
        {
            db.RemoveRange(removed);
            await db.SaveChangesAsync(ct); // free unique keys (one follow-up / certificate per consultation) before inserting
        }
        foreach (var (e, values) in incoming)
        {
            var match = existing.FirstOrDefault(x => key(x) == key(e));
            if (match is null)
            {
                var stamps = e as IHasUpdatedAt;
                if (stamps is not null && stamps.UpdatedAt == default) stamps.UpdatedAt = DateTimeOffset.UtcNow;
                db.Add(e);
            }
            else
            {
                var keyCols = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Select(p => SyncRows.Name(p)).ToHashSet();
                SyncRows.Set(match, values, col => !keyCols.Contains(col));
                enforce(match);
            }
        }
    }

    // ── append-only ──────────────────────────────────────────────────────
    private async Task<SyncOperationResult> VitalReadingAsync(SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.AppendOnly, "Vital readings are append-only.");
        var r = SyncRows.New<PatientVitalReading>(db, op.Data);
        if (r.Id != Guid.Empty && r.Id != id) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        r.Id = id;
        var same = await db.PatientVitalReadings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (same is not null)
            return same.Value == r.Value
                ? Done0(op, SyncStatuses.Duplicate, same.RowVersion)
                : Fail(op, SyncStatuses.Rejected, SyncReasons.AppendOnly, "Vital readings cannot be changed once recorded.");
        var clash = await db.PatientVitalReadings.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == r.BookingId && x.TemplateId == r.TemplateId, ct);
        if (clash is not null)
            return await DoneFor(op, SyncAggregates.VitalReading, clash.Id, SyncStatuses.Conflict, SyncReasons.AlreadyRecorded,
                "This vital was already recorded for the visit.", ct);
        if (!await db.Bookings.AnyAsync(b => b.BookingId == r.BookingId, ct))
            return Fail(op, SyncStatuses.Retry, SyncReasons.MissingDependency, "Booking not in the cloud yet.");
        if (r.CreatedAt == default) r.CreatedAt = DateTimeOffset.UtcNow;
        if (r.UpdatedAt == default) r.UpdatedAt = r.CreatedAt;
        db.PatientVitalReadings.Add(r);
        await db.SaveChangesAsync(ct);
        return Ok(op, r.RowVersion);
    }

    private async Task<SyncOperationResult> AuditLogAsync(SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.AppendOnly, "Audit logs are append-only.");
        var a = SyncRows.New<AuditLog>(db, op.Data);
        if (a.Id != Guid.Empty && a.Id != id) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        a.Id = id;
        var same = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (same is not null) return Done0(op, SyncStatuses.Duplicate, same.RowVersion);
        if (a.PerformedAt == default) a.PerformedAt = DateTimeOffset.UtcNow;
        db.AuditLogs.Add(a);
        await db.SaveChangesAsync(ct);
        return Ok(op, a.RowVersion);
    }

    //JJQR - AI (21 Nov 2026) consents: append-only history (a decision is never edited); a replay of the same id is a duplicate
    private async Task<SyncOperationResult> PatientConsentAsync(SyncPushOperation op, Guid id, CancellationToken ct)
    {
        if (op.IsDelete) return Fail(op, SyncStatuses.Rejected, SyncReasons.AppendOnly, "Consent history is append-only.");
        var c = SyncRows.New<PatientConsent>(db, op.Data);
        if (c.Id != Guid.Empty && c.Id != id) return Fail(op, SyncStatuses.Rejected, SyncReasons.EntityIdMismatch, null);
        c.Id = id;
        if (!ConsentDecision.All.Contains(c.Decision)) return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidPayload, "decision must be Accepted, Declined or Withdrawn.");
        if (await db.PatientConsents.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return Done0(op, SyncStatuses.Duplicate, null);
        if (!await db.Patients.AnyAsync(p => p.PatientId == c.PatientId, ct))
            return Fail(op, SyncStatuses.Retry, SyncReasons.MissingDependency, "Patient not in the cloud yet.");
        if (c.DecidedAt == default) c.DecidedAt = DateTimeOffset.UtcNow;
        db.PatientConsents.Add(c);
        await db.SaveChangesAsync(ct);
        return Ok(op, 0);
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) OR number clash: if another payment already holds this OR (case-insensitive), keep BOTH as printed and write an audit row on each (ids only).
    // Returns true when a clash exists. Never changes, drops or renumbers a payment.
    private async Task<bool> FlagOrClashAsync(Payment p, Guid nodeId, CancellationToken ct)
    {
        var or = p.OrNumber?.Trim();
        if (string.IsNullOrEmpty(or)) return false;
        var lower = or.ToLower();
        var others = await db.Payments.AsNoTracking()
            .Where(x => x.PaymentId != p.PaymentId && x.OrNumber != null && x.OrNumber.ToLower() == lower)
            .Select(x => new { x.PaymentId, x.BookingId, x.OriginNodeId }).ToListAsync(ct);
        if (others.Count == 0) return false;

        const string action = "OR number clash";
        var already = (await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == AuditEntityType.Payment && a.Action == action)
            .Select(a => new { a.EntityId, a.Details }).ToListAsync(ct));
        bool Logged(Guid on, Guid other) => already.Any(a => a.EntityId == on && a.Details != null && a.Details.Contains(other.ToString()));
        string Origin(Guid? o) => o is null ? "cloud" : $"node {o}";

        foreach (var o in others)
        {
            //JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.3: one review item per pair, naming both payments (and their visits) for the admin; the pair is listed once whichever arrived first
            var mine = p.PaymentId.ToString(); var theirs = o.PaymentId.ToString();
            if (!await db.SyncReviewItems.AnyAsync(i => i.Kind == ReviewItemKinds.OrNumberClash
                    && ((i.EntityId == mine && i.OtherEntityId == theirs) || (i.EntityId == theirs && i.OtherEntityId == mine)), ct))
                db.SyncReviewItems.Add(new SyncReviewItem
                {
                    Id = Guid.NewGuid(), Kind = ReviewItemKinds.OrNumberClash, Status = ReviewItemStatus.Open,
                    Title = $"Receipt number {or} is on two payments",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        or_number = or, payment_id = p.PaymentId, booking_id = p.BookingId, other_payment_id = o.PaymentId, other_booking_id = o.BookingId,
                        origin = Origin(nodeId), other_origin = Origin(o.OriginNodeId),
                    }),
                    Aggregate = "payment", EntityId = mine, OtherEntityId = theirs, OriginNodeId = nodeId, CreatedAt = DateTimeOffset.UtcNow,
                });
            //JJQR - END
            if (!Logged(p.PaymentId, o.PaymentId))
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(), EntityType = AuditEntityType.Payment, EntityId = p.PaymentId, Action = action, PerformedAt = DateTimeOffset.UtcNow,
                    Details = $"OR {or} is also on payment {o.PaymentId} ({Origin(o.OriginNodeId)}); this payment (node {nodeId}) was kept as printed. Needs admin review."
                });
            if (!Logged(o.PaymentId, p.PaymentId))
                db.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.NewGuid(), EntityType = AuditEntityType.Payment, EntityId = o.PaymentId, Action = action, PerformedAt = DateTimeOffset.UtcNow,
                    Details = $"OR {or} is also on payment {p.PaymentId} (node {nodeId}). Needs admin review."
                });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
    //JJQR - END

    // ── helpers ──────────────────────────────────────────────────────────
    private static bool IdMismatch(Dictionary<IProperty, object?> values, string column, Guid id)
    {
        var p = values.Keys.FirstOrDefault(k => SyncRows.Name(k) == column);
        return p is not null && values[p] is Guid g && g != Guid.Empty && g != id;
    }

    private static SyncOperationResult Ok(SyncPushOperation op, long rowVersion) => Done0(op, SyncStatuses.Applied, rowVersion);

    private static SyncOperationResult Done0(SyncPushOperation op, string status, long? rowVersion) => new()
    {
        OperationId = op.OperationId, Aggregate = op.Aggregate, EntityId = op.EntityId, Status = status, RowVersion = rowVersion
    };

    private static SyncOperationResult Fail(SyncPushOperation op, string status, string reason, string? message) => new()
    {
        OperationId = op.OperationId, Aggregate = op.Aggregate, EntityId = op.EntityId, Status = status,
        ReasonCode = reason, Message = message ?? (reason == SyncReasons.EntityIdMismatch ? "entity_id does not match the id inside payload.data." : null)
    };

    /// <summary>Result carrying the cloud's current document of this aggregate.</summary>
    private Task<SyncOperationResult> Done(SyncPushOperation op, string status, string? reason, string? message, List<string>? fields, CancellationToken ct) =>
        DoneFor(op, op.Aggregate, Guid.Parse(op.EntityId), status, reason, message, ct, fields);

    private async Task<SyncOperationResult> DoneFor(SyncPushOperation op, string aggregate, Guid currentId, string status, string? reason,
        string? message, CancellationToken ct, List<string>? fields = null)
    {
        await db.SaveChangesAsync(ct);
        var doc = await SyncDocuments.BuildAsync(db, aggregate, SyncAggregates.Id(currentId), ct);
        return new SyncOperationResult
        {
            OperationId = op.OperationId, Aggregate = op.Aggregate, EntityId = op.EntityId, Status = status,
            RowVersion = doc?.RowVersion, ReasonCode = reason, Message = message, ConflictFields = fields, Current = doc?.Data
        };
    }

    private static SyncOperation Log(Guid nodeId, SyncPushOperation op, SyncOperationResult r) => new()
    {
        OperationId = op.OperationId, NodeId = nodeId, Aggregate = op.Aggregate ?? "", EntityId = op.EntityId ?? "",
        OperationType = op.OperationType ?? "", Status = r.Status, ReasonCode = r.ReasonCode,
        ResultJson = JsonSerializer.Serialize(r, SyncJson.Options), NodeCreatedAt = op.CreatedAt, ReceivedAt = DateTimeOffset.UtcNow
    };

    private static SyncOperationResult Replay(SyncOperation prior, Guid nodeId, SyncPushOperation op)
    {
        if (prior.NodeId != nodeId)
            return Fail(op, SyncStatuses.Rejected, SyncReasons.InvalidOperation, "operation_id was already used by another node.");
        var r = JsonSerializer.Deserialize<SyncOperationResult>(prior.ResultJson, SyncJson.Options) ?? Fail(op, prior.Status, prior.ReasonCode ?? "", null);
        r.Replayed = true;
        return r;
    }
}
//JJQR - END
