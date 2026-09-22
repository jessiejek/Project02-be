# ClinicApp state machines

Server-enforced in `ClinicApp.Domain.BookingStatusMachine` and `PaymentStatusMachine`.
Illegal transitions return **400** with a clear message. Same-status no-ops are allowed.

## Booking (`BookingStatus`)

| From | Allowed next |
|------|----------------|
| Pending | ProofSubmitted, Confirmed, CheckedIn, Cancelled, Expired, Rescheduled, **NoShow** |
| ProofSubmitted | Confirmed, Cancelled, Expired |
| Confirmed | CheckedIn, Cancelled, Expired, Rescheduled, **Completed, NoShow** |
| CheckedIn | InProgress, OnHold, Cancelled, NoShow, **Confirmed, Completed** |
| InProgress | OnHold, Completed, Cancelled, **NoShow** |
| OnHold | CheckedIn, InProgress, Cancelled, NoShow, **Completed** |
| Cancelled / Completed / Expired / NoShow / Rescheduled | _(terminal)_ |

Bold = allowed because a real screen does it (`tests/.../BookingFlowTests.cs` has one row per button):

| Transition | Where the UI does it |
|---|---|
| Pending → NoShow | Staff queue page, "No-show" on a booked-but-absent row |
| InProgress → NoShow | Staff queue page, "No-show" on an in-progress row |
| CheckedIn / OnHold → Completed | Doctor "Complete Consultation" also completes the queue entry; nothing forces the front desk to have pressed "Call" first |
| CheckedIn → Confirmed | "Undo Check-In" (staff booking page, staff dashboard toggle) |
| Confirmed → Completed / NoShow | Admin booking page "Mark Complete" / "Mark No Show" |

Still rejected: anything out of a terminal state (a finished visit can't become a no-show or be re-queued),
Pending → Completed / InProgress (skips check-in), and any jump not listed. Same-status no-ops are allowed
(so a double-clicked "Complete" is harmless).

Wired from:
- `PUT /api/bookings/{id}/status`
- Queue `check-in` / `call` / `hold` / `complete` / `no-show` / staff cancel
- Patient `PUT /api/bookings/{id}/cancel` (Pending → Cancelled only, already)

## Payment (`PaymentStatus`)

| From | Allowed next |
|------|----------------|
| Unpaid | Paid, Waived |
| Paid | Refunded |
| Waived / Refunded | _(terminal)_ |

Wired from `POST /api/payments/{id}/confirm|waive|refund`. Waive still requires doctor `pf_decision` for Staff.

## Consultation immutability

A finalized consultation (`Completed` / `Amended`) is append-only via `Amended`:

- It never goes back to `Draft` (409).
- Re-sending the **same** `Completed` save is an idempotent no-op (200, nothing rewritten, no extra audit row) —
  the doctor page saves in several calls (consultation → diagnoses → labs → …), so a double-click or a retry
  after a partial failure is normal. **Different** content while claiming `Completed` is 409: send `Amended`.
- `Amended` can be saved repeatedly.
- **Diagnoses** (`PUT /api/consultations/{id}/diagnoses`) are accepted while the consultation is `Draft`, `Amended`, or
  `Completed` for at most **2 minutes** after `completed_at` (the completion action itself saves diagnoses right after the
  consultation row). After that a `Completed` record is sealed: 409 until it is amended. This endpoint never changes the
  consultation's status. Changing diagnoses on an `Amended` record writes a `Diagnoses amended` audit row (before → after).

Not covered yet: the other child rows of a consultation (lab orders, vaccinations, follow-up, medical certificate) are
not sealed after completion, and the doctor page's multi-call save is still not one atomic transaction.
