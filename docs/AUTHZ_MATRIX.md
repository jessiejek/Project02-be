# Authorization matrix (RLS → .NET)

Source of truth for *who may do what* in `ClinicApp.Api`. It replaces the Supabase
`supabase/rls.sql` policies (contract §17.2 #5). Every rule below is enforced **server-side in
the controller** and covered by `tests/ClinicApp.Api.Tests` (`IdorTests`, `RoleTests`,
`FileAccessTests`). If a rule here and the code disagree, the code is a bug — fix one and the test.

**Roles** (JWT `role` claim): `Patient`, `Staff` (front desk), `Doctor`, `Admin`.
"Staff-like" = Admin + Staff + Doctor (RLS `is_staff_like()`).

**Helpers** (`src/ClinicApp.Api/Security/Actor.cs`, one lookup per request):

| RLS helper | .NET |
|---|---|
| `current_patient_id()` | `Actor.PatientId` (patient row whose `user_id` = JWT `sub`) |
| `current_doctor_id()` | `Actor.StaffId` (`staff_accounts.staff_id` doubles as `doctors.doctor_id`) |
| `is_staff_like()` / `is_admin()` | `Actor.IsStaffLike` / `Actor.IsAdmin` |
| `owns_booking()` / `patient_id = current_patient_id()` | `Actor.CanAccessPatient(patientId)` |
| — (new, RLS had no per-doctor rule) | `Actor.ActsAsDoctor(doctorId)` = Admin, or that doctor |

**Denial style.** Lookup by id of a row you may not see → `404` (indistinguishable from a
nonexistent id, like RLS returning no row). Explicit filter for someone else (`?patientId=<other>`)
or a role that may never call the endpoint → `403`. Unauthenticated → `401`.

Legend: **own** = own row only · **any** = every row · **—** = denied · **anon** = no token.

---

## 1. People

| Resource / action | Patient | Staff | Doctor | Admin | Anon |
|---|---|---|---|---|---|
| `patients` list / search / create | — | any | any | any | — |
| `patients/{id}` read | own | any | any | any | — |
| `patients/{id}` update, `…/consent` | own | any | any | any | — |
| `patients/me`, `patients/me/vaccinations` | own | — | — | — | — |
| `patients/{id}/vaccinations` | — | any | any | any | — |
| `staff-accounts` list / search | — | any | — | any | — |
| `staff-accounts/{id}` read | active Doctor rows + own | any | any | any | — |
| `staff-accounts/{id}` update | — | own | own | any | — |
| …change another user's `status` | — | — | — | ✔ | — |
| `doctors` list / `{id}` / schedules / blocked-dates / day-status **read** | ✔ public | ✔ | ✔ | ✔ | ✔ (booking wizard) |
| `doctors/{id}` update, `…/services` | — | — | — | ✔ | — |
| `doctors/{id}/schedules`, `…/blocked-dates` write | — | — | own | any | — |
| `doctors/{id}/day-status` write | — | any | own | any | — |

## 2. Bookings & queue

| Action | Patient | Staff | Doctor | Admin |
|---|---|---|---|---|
| `GET bookings` (list) | own (filter forced; other patient id → 403) | any | any | any |
| `GET bookings/{id}`, `bookings/me` | own | any | any | any |
| `POST bookings`, `POST bookings/walk-in`, `POST queue` (walk-in) | — | ✔ | — | ✔ |
| `POST bookings/book` (online self-booking) | ✔ (patient forced to self) | — | — | — |
| `PUT bookings/{id}/cancel` | own, `Pending` only | — | — | — |
| `PUT bookings/{id}/status` | — | ✔ | own bookings | ✔ |
| `PUT queue/{id}/check-in`, `…/cancel` | — | ✔ | — | ✔ |
| `PUT queue/{id}/call\|hold\|complete\|no-show` | — | ✔ | own bookings | ✔ |
| `GET queue` | — | ✔ | ✔ | ✔ |
| `GET bookings/staff/today\|all\|for-payment` | — | ✔ | — | ✔ |
| `GET bookings/doctor/today\|today-summary\|patients` | — | — | ✔ (own doctor id from JWT) | — |

## 3. Payments

| Action | Patient | Staff | Doctor | Admin |
|---|---|---|---|---|
| `GET payments/{id}`, `payments/booking/{id}` | own booking | any | any | any |
| `POST payments/{id}/confirm` | — | ✔ | — | ✔ |
| `POST payments/{id}/waive` | — | **only if the doctor already set `pf_decision = 'Waive'` on that consultation** | own bookings | ✔ |
| `POST payments/{id}/refund` | — | — | — | ✔ |

Waive is the doctor's call (contract §17.2 #5; roadmap Phase 4d "waive = Doctor/Admin"). The front
desk may only *record* a waiver the doctor decided; it cannot waive unilaterally.

## 4. Clinical record

`patient_id` / `doctor_id` on every write are **checked against the booking (or consultation)**, never
trusted from the body. "Doctor own" = the booking's/consultation's `doctor_id` is the caller.

| Resource | Read: Patient | Read: staff-like | Write |
|---|---|---|---|
| `consultations` (list, `{id}`, `by-booking`) | own | any | Doctor own / Admin (`PUT by-booking`) |
| `consultations/{id}/diagnoses` | own | any | Doctor own / Admin |
| `prescription-groups` (list, `{id}`) | own | any | Doctor own / Admin (upsert, delete) |
| `lab-orders` (list, `by-consultation`, `by-booking`) | own | any | Doctor own / Admin |
| `follow-ups` | own | any | Doctor own / Admin |
| `medical-certificates` | own | any | Doctor own / Admin |
| `patient-vaccinations` (list, `by-consultation`) | own | any | consult replace: Doctor own / Admin; `POST`: Doctor, Staff, Admin |
| `vitals` | own | any | Staff, Doctor, Admin (`PUT by-booking`, contract §16.1) |

## 5. Files (PHI)

| Action | Patient | Staff | Doctor | Admin | Anon |
|---|---|---|---|---|---|
| `GET patient-documents`, `patient-lab-results` (list) | own | any | any | any | — |
| `GET …/{id}/file` (bytes) | own | any | any | any | — |
| `POST patient-documents`, `patient-lab-results` (upload) | own patient, **own booking** | any | any | any | — |
| `GET /uploads/...` | **not served — 404 for everyone** | | | | |

* Uploads live in `App_Data/uploads` (override with `FileStorage:RootPath`). There is **no static-file
  mount**; the only read path is the authorized `/file` endpoint (bearer token required).
* `file_url` in a row is an internal storage key, resolved by `IFileStorageService.ResolvePath`, which
  rejects anything outside the upload root (`..`, absolute paths, external URLs) → 404.
* Served with `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`; a stored content
  type outside the upload allow-list is served as `application/octet-stream`.
* Upload requires `booking_id` (and any `consultation_id` / `lab_order_id`) to belong to the same patient.

## 6. Reviews

| Action | Patient | Staff / Doctor / Admin |
|---|---|---|
| `GET reviews` | own reviews; **plus** any doctor's reviews via `?doctorId=` (public ratings, RLS `reviews_select_authenticated`) | any |
| `POST reviews` | own booking only; `patient_id` / `doctor_id` taken from the booking | — |

## 7. Doctor tooling (private per doctor)

| Resource | Patient | Staff | Doctor | Admin |
|---|---|---|---|---|
| `soap-phrases`, `soap-templates`, `medical-certificate-templates`, `doctor-diagnosis-templates` | — | — | own (+ system templates on read) | any |
| `prescription-templates` | — | read | own (+ system) | any |
| `doctor-favorite-medicines` | — | — | own | any |
| create/edit/delete a **system** template | — | — | — | ✔ |

## 8. Admin / reference

| Resource | Patient | Staff | Doctor | Admin | Anon |
|---|---|---|---|---|---|
| `audit-logs` (list) | — | — | `entityType=Consultation` only (amend history) | any | — |
| `audit-logs/search` | — | — | — | ✔ | — |
| `audit-logs` POST | — | ✔ | ✔ | ✔ | — |
| `announcements`, `settings`, `admin/operating-hours`, `admin/payment-methods` **read** | ✔ | ✔ | ✔ | ✔ | ✔ (public home page) |
| …the same, **write** | — | — | — | ✔ | — |
| `services`, `doctor-services` read / write | ✔ / — | ✔ / — | ✔ / — | ✔ / ✔ | ✔ / — |
| `medicines`, `vital-field-templates`, `lab-test-catalog`, `icd10-codes` (reference data) | ✔ | ✔ | ✔ | ✔ | ✔ (see risks) |
| `reports/*` | `doctor-ratings` only | per endpoint | per endpoint | ✔ | — |
| `auth/invite`, `auth/users/{id}` DELETE | — | — | — | ✔ | — |

---

## Gaps found and fixed (API was weaker than RLS)

Before this pass every route below was `[Authorize]` only — *any signed-in patient* could call it with any id.

| # | Gap | Fix |
|---|---|---|
| 1 | `GET bookings`, `bookings/{id}` — any patient read any booking (embedding the other patient's full record) | own-patient scoping (`Actor`) |
| 2 | `GET payments/{id}`, `payments/booking/{id}` | own booking only |
| 3 | `GET/PUT patients/{id}`, `PUT patients/{id}/consent` — a patient could read **and overwrite** another patient's record and consent | own patient only |
| 4 | `GET consultations*`, `…/diagnoses`, `prescription-groups*`, `lab-orders*`, `follow-ups*`, `medical-certificates*`, `patient-vaccinations*`, `vitals` — clinical notes, diagnoses, Rx readable across patients | list filters forced to the caller; by-id/by-booking/by-consultation own-only |
| 5 | `patient-documents` / `patient-lab-results` — list any patient's; **upload into any patient's record** | scoped list; upload checks patient + booking (+ consultation / lab order) ownership |
| 6 | **Files served publicly**: `UseStaticFiles` mounted `/uploads` with no auth (PHI at a URL with no token) | mount removed; authorized `/file` endpoints; traversal-safe resolver |
| 7 | `POST reviews` trusted `patient_id` / `doctor_id` from the body — a patient could post reviews as another patient; `GET reviews` listed everyone's comments | identity from the booking, Patient-only; list scoped |
| 8 | `waive` was open to any Staff; contract says doctor's call | Doctor own / Admin; Staff only to record a doctor-decided waiver |
| 9 | Any Doctor could write **another doctor's** consultation / Rx / labs / follow-ups / certificates / vaccinations / schedule / templates / favorites; `patient_id`/`doctor_id` in bodies were trusted (a consultation could be pointed at any patient) | `ActsAsDoctor` checks; ids validated against the booking / consultation |
| 10 | `audit-logs` readable by Staff & Doctor (RLS: Admin only) — rows carry before/after patient field values | Admin only; Doctor limited to `Consultation` entries (needed for amend history) |
| 11 | `staff-accounts/{id}` readable by patients (admin/staff email, phone); `PUT` let Staff edit anyone's row incl. status (RLS: own or Admin) | active-Doctor rows or own; update own or Admin |
| 12 | `prescription-templates`, `doctor-favorite-medicines` GET open to patients | staff-like / Doctor only |
| 13 | A Doctor could change status / queue position of another doctor's bookings | Doctor limited to own |

## Decisions that differ from RLS (deliberate)

* **Doctor-vs-doctor isolation** for writes (RLS let any staff-like user write any row). Cheap, and correct the day the clinic has a second doctor.
* **Patients can no longer write payments** (RLS `payments_update_own_or_staff` let a patient UPDATE their own payment row — i.e. mark it Paid).
* **Patient can't write clinical rows** (RLS didn't either; stated because the .NET port had to make it explicit).
* **Doctors don't get the full audit trail** — only `Consultation` entries.

## Residual risks / not done in this pass

1. **Role comes from the JWT** (60-min access token). Demoting/deactivating a user doesn't take effect until the token expires; there's no revocation check per request. Deactivated staff keep access for up to an hour.
2. **Staff/Doctor see every patient** (RLS parity; a doctor isn't limited to "their" patients). Fine for one clinic, revisit for multi-doctor.
3. **Public reference endpoints** (`medicines`, `vital-field-templates`, `lab-test-catalog`, `icd10-codes`, `settings`, `announcements`, hours, payment methods) are anonymous. RLS said *authenticated*; the FE deliberately calls them with `anonymous: true`, so tightening needs an FE change. No PHI, but it is a difference.
4. **Public doctor catalog exposes `license_number` / `ptr_number` / `s2_number`** via `GET /api/doctors` (RLS parity: `doctors_select_all`). Confirm the clinic is happy publishing these.
5. **`POST audit-logs`** lets any Staff/Doctor/Admin write arbitrary audit rows (RLS parity) — the trail can be polluted by a compromised staff account. Server-side audit writes exist for booking/patient/consultation; the client-written path should be retired.
6. **Payment state machine is not enforced**: confirm/waive/refund don't check current status (double-refund, refund of an Unpaid payment, waive after Paid). Same for booking status transitions (P2.1). Out of scope here.
7. **`GET reviews?doctorId=`** returns other patients' `patient_id` (UUID) and comment text to any patient (RLS parity — public reviews). No names, but consider redacting.
8. **`PUT patients/{id}` binds the whole `Patient` entity** for staff callers and only copies an allow-list of fields; a patient can't change `email`, `patient_code`, `user_id`, `is_guest`. Verified by code review, not a dedicated test.
9. SignalR hub group membership is derived from the JWT role/sub (already server-side); not re-audited here.
10. **Old files**: rows imported from Supabase with absolute `file_url`s (https://…supabase…) are not resolvable by the new endpoint (→ 404). If any exist they need migrating into `App_Data/uploads`.

## Test map

| Rule | Test |
|---|---|
| Patient A → B: bookings, patient record, payments, consultation, Rx, labs, follow-ups, certificates, vaccinations, vitals, reviews | `IdorTests` |
| Patient can't hit staff endpoints / can't mutate payments / can't write clinical data | `IdorTests` |
| Review identity from booking; staff can't post reviews | `IdorTests` |
| Anonymous → 401; forged JWT → 401 | `IdorTests` |
| Refund Admin-only; waive rules (Staff/Doctor/Admin); confirm Admin/Staff | `RoleTests` |
| Doctor-vs-doctor writes, templates, favorites, schedules, booking status | `RoleTests` |
| Consultation ids must match booking | `RoleTests` |
| Audit trail scoping; staff-directory writes | `RoleTests` |
| No public `/uploads`; `/file` auth; upload ownership; traversal; content-type; missing file | `FileAccessTests` |

Run: `dotnet test tests/ClinicApp.Api.Tests` (SQLite in-memory-style temp DB, real JWTs, no external services).
