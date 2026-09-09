# ClinicApp — Backend Build + Frontend Integration Roadmap

Pairs every backend phase with the frontend work that consumes it, so each phase
ends with something **demonstrably working end-to-end**, not just a merged PR.

- Backend: `Project02-be` (this repo) — ASP.NET Core `net10.0`, EF Core, SQL Server
- Frontend: `Project02-fe` — Next.js 16, currently talking directly to Supabase
- Wire contract: `../Project02-fe/DOTNET_FRONTEND_CONTRACT.md` (§0–15 = the 1:1
  contract, §16 = amendments, §17 = open risks, §18 = checklist). **JSON keys and
  enum strings are frozen by that doc — paths may differ, shapes may not.**

---

## Strategy

### Incremental cutover, not big-bang

The frontend has 267 `.from(...)` call sites and 29 `auth.*` calls. Migrating all
at once is untestable. Instead:

1. **`src/lib/api/` typed client** (Phase 0) — `apiGet/apiPost/apiPut/apiDelete`,
   base URL from `NEXT_PUBLIC_API_URL`, attaches the .NET bearer token.
2. **Per-resource switch** — `src/lib/data/<resource>.ts` modules expose the same
   function signatures the pages already use. Internally each checks
   `API_MODE` (env `NEXT_PUBLIC_API_MODE`, or a per-resource override map) and
   routes to either the Supabase path (today) or the `api/` client (new).
   A phase "flips" a resource by changing its default and deleting the Supabase branch once verified.
3. **Auth is the one hard cutover** (Phase 1) — it can't be half-migrated. Before
   Phase 1 the FE holds a Supabase session; after, a .NET JWT session. Everything
   after Phase 1 assumes the .NET token exists.

### Parity harness (the safety net)

`scripts/parity/` in `Project02-fe`: for a given query, hit **both** Supabase and
the .NET API and deep-diff the JSON (ignoring row order). Run per phase against a
seeded DB. A resource isn't "flipped" until its parity diff is empty (or every
delta is explained in the phase's PR).

### Definition of done — every phase

- [ ] `dotnet build` + `dotnet test` green; `next build` + `next lint` green
- [ ] New/changed endpoints return contract §4/§6 keys **exactly** (snake_case,
      PascalCase enum strings) — checked by `scripts/parity/` and a `.http` file
- [ ] Role gate enforced (`[Authorize(Roles=...)]` matches contract §11)
- [ ] Error body shape consistent (`{ "error": "...", "detail": "..." }`) and
      the FE data module maps it the way callers expect
- [ ] Parity diff empty for every flipped resource
- [ ] Both repos: one branch + PR per phase, cross-linked; roadmap checkbox ticked
- [ ] Manual smoke script in the phase section below passes

### Branch / PR convention

`Project02-be`: `phase-N-<slug>` · `Project02-fe`: `phase-N-<slug>` · PRs reference
each other. Merge backend first, then frontend.

### Rollback

Any post-Phase-1 phase reverts by flipping its resources' `API_MODE` back to
`supabase`. Phase 1 reverts by restoring the Supabase `SessionProvider`/proxy
(keep them behind the flag until Phase 7).

---

## Phase 0 — Backend runs locally + integration scaffolding

**Goal:** `dotnet run` serves a contract-correct API against the Docker SQL Server;
the FE gains the API client but behaviour is unchanged.

### Backend
- [ ] `appsettings.Development.json` → Docker connection string
      (`Server=localhost,1433;Database=ClinicAppDb;User Id=sa;Password=Clinic_Dev_Pass123;TrustServerCertificate=True`);
      keep the Windows `PHL8DPROG004\SQLEXPRESS` string only in `appsettings.json`.
- [ ] `docker compose up -d` → `dotnet ef database update` → confirm all tables +
      the 4 views + seed rows (`clinic_settings`, operating hours, payment methods,
      icd10, vital templates, medicines).
- [ ] `Program.cs`: CORS allows `http://localhost:3000`; JSON = snake_case policy +
      `JsonStringEnumConverter`; `[Authorize]` default with `[AllowAnonymous]` on
      `/api/auth/*`.
- [ ] `ClinicApp.Api.http` covering every existing endpoint; eyeball the JSON keys.
- [ ] Spot-check tricky columns in the DB: `philhealth_number`, `hmo_card_number`,
      `ptr_number`, `s2_number` did **not** get re-split by the naming convention.

### Frontend
- [ ] `src/lib/api/client.ts` — fetch wrapper, `NEXT_PUBLIC_API_URL`, bearer token
      hook (no token yet), typed error.
- [ ] `src/lib/data/` folder + `mode.ts` (`API_MODE`, per-resource override map),
      all defaulting to `supabase`.
- [ ] `.env.local` / `env.example`: add `NEXT_PUBLIC_API_URL=http://localhost:5xxx`,
      `NEXT_PUBLIC_API_MODE=supabase`.
- [ ] `scripts/parity/` skeleton + one worked example (`doctors`).

### Verify
`curl -X POST /api/auth/login` then `curl /api/doctors` → JSON has `doctor_id`,
nested `staff_accounts.full_name`, enum strings intact. FE `next build` green,
app still fully on Supabase.

---

## Phase 1 — Auth cutover  ▲ IN PROGRESS

**Goal:** every role logs into the real app through the .NET JWT; the .NET JWT is
what middleware and the session context trust. A *parallel* Supabase session is
still established at login so not-yet-migrated `.from(...)` calls keep working
(removed in Phase 7). Fully behind `NEXT_PUBLIC_AUTH_MODE` (`supabase` default =
no change; `dotnet` = the new path).

### Backend — ✅ done
- [x] `/api/auth`: `login`, `register`, `refresh-token`, `logout`, `me`,
      `forgot-password` (logs link — no SMTP), `reset-password`, `set-password`,
      `invite`, `DELETE users/{id}` all present. `google`/`facebook` = 501.
      **Wire is camelCase** (`accessToken`, `firstName`) — ported from the Angular
      backend; the FE auth client matches that, data resources stay snake_case.
- [x] `login` → `{ accessToken, refreshToken, user{ id, role, fullName, ... } }`;
      `role` = `profiles.role`; JWT `sub` = `Users.Id`, role claim set.
- [x] Refresh-token rotation + revoke-on-logout; SHA-256 hash at rest.
- [x] `DevDataSeeder` (Development only) — seeds the 5 `account-list.txt` logins
      (same emails/ids as Supabase) with password **`ClinicDev123!`** so the FE
      can authenticate without a full data import. Idempotent.

### Frontend — ✅ done (gated by `NEXT_PUBLIC_AUTH_MODE=dotnet`)
- [x] `src/lib/auth/` — `mode.ts` (switch), `cookies.ts` (httpOnly `clinic_at`/
      `clinic_rt`), `jwt.ts` (payload decode — **no sig verify yet**, §17),
      `dotnet.ts` (/api/auth client), `session.ts` (`getServerSession()`),
      `types.ts` (`SessionInfo`).
- [x] `src/app/api/session/login/route.ts` — .NET login → sets httpOnly cookies;
      best-effort parallel `supabase.auth.signInWithPassword`.
- [x] `src/proxy.ts` — `proxyDotnet()` gates on the JWT cookie (+ silent refresh
      via the refresh cookie); `proxySupabase()` unchanged. `/api` excluded from
      the matcher.
- [x] `SessionProvider` — takes `initialSession` from the server (root layout is
      now `async` → `getServerSession()`); no client auth call in dotnet mode.
- [x] `login/page.tsx` — posts to `/api/session/login` in dotnet mode.
- [x] `logout/route.ts` — .NET logout + clear cookies + Supabase signOut.
- [x] Verified by curl: all 5 roles log in → correct dashboard; cross-role →
      redirect; no cookie → `/login`; bad password → 401; `next build` green;
      `supabase` mode regression-clean.

### Deferred to Phase 1b / later (still on Supabase, work via the parallel session)
- [ ] Password change + "verify current password" in `{patient,staff,doctor}/profile`
      (`auth.signInWithPassword` + `auth.updateUser`)
- [ ] Booking-flow signup (`booking/page.tsx` `auth.signUp` / `signInWithPassword`)
- [ ] `forgot-password` page → `/api/auth/forgot-password`; new set-password page
- [ ] `patient/dashboard` email-verify resend (`auth.resend`)
- [ ] 4 server actions → `/api/auth/invite` + `DELETE /api/auth/users/{id}`
      (they still work via the parallel Supabase session for now)
- [ ] 3 RSC `supabase.auth.getUser()` sites → a `getCurrentUserId()` helper
- [ ] **Sig-verify the JWT in `jwt.ts`** before production (`jose` + shared secret)
- [ ] Sync the 5 Supabase test-account passwords to `ClinicDev123!` (or import
      real hashes) so `supabaseLinked` is true — needed only if RLS gets enabled

### Verify (done)
Log in as each role → correct `/{role}/dashboard`; wrong-role URL → redirected;
`next build` green. Remaining: register-new-patient + invite flows (deferred set).

---

## Phase 2 — Identity & directory reads  ▲ IN PROGRESS

**Resources:** `profiles`, `patients`, `staff_accounts`, `doctors`,
`doctor_schedules`, `doctor_services`, `doctor_blocked_dates`,
`doctor_day_statuses`

### Data mirror (prerequisite — done)
- [x] `Project02-be` `POST /api/dev/import` (Development only) — upserts rows by
      their real PK. `DevDataSeeder` slimmed to `users`+`profiles` only.
- [x] `Project02-fe/scripts/dev/import-from-supabase.mjs` — pulls each table from
      Supabase (service role) → `/api/dev/import` in FK order. Run once after
      `dotnet run`. Re-run any time to re-sync.
- [x] Schema-fidelity fix: `staff_accounts.user_id` / `patients.user_id` unique
      indexes were missing (migration `AddUserIdUniqueIndexes`).
- [x] Parity harness now uses the service-role key (compares data, not RLS).

### `doctors` — done
- [x] `src/lib/data/doctors.ts` — `queryDoctors()` / `queryDoctorById()`, both
      backends projected to the canonical §4/§6 shape. Flipped in `mode.ts`.
- [x] Parity `doctors` case: **clean** (2 rows).
- [x] Migrated read sites: `patient/doctors`, `patient/doctors/[id]`,
      `admin/doctors`. Live-verified via `AUTH_MODE=dotnet` — page renders from
      .NET, Inactive doctor correctly hidden (Supabase anon-key path leaked it).

### `doctors` reads — done (all 16 sites)
- [x] `patient/doctors`, `patient/doctors/[id]`, `admin/doctors` (increment 1)
- [x] `booking`, `patient/dashboard`, `admin/dashboard`, `admin/bookings`,
      `staff/bookings`, `admin/calendar`, `admin/walk-in`, `staff/walk-in`,
      `admin/services`, `staff/doctor-status` (increment 2). `next build` green.

### `patients` + `staff_accounts` reads — done
- [x] `StaffAccountsController` (new): `GET /api/staff-accounts?role=`, `/{id}`,
      `/me`, `PUT /{id}`. Patients controller already had list/{id}/me/consent.
- [x] `src/lib/data/patients.ts` + `staff.ts`; parity `patients` (2) +
      `staff_accounts` (4) **clean**. Flipped in `mode.ts`.
- [x] Browser token flow: `GET /api/session/token` (same-origin) + browser
      `tokenProvider` in `src/lib/api/client.ts` (server reads the cookie
      directly) + 401-retry. Needed for client components to hit protected
      .NET endpoints. **§17: token is handed to same-origin JS — harden later.**
- [x] Migrated: `admin/patients`, `staff/patients`, `admin/staff`,
      `admin/walk-in`, `staff/walk-in` (patient picker). In-browser verified.

### Writes — done (doctor / staff / patient scalar edits)
- [x] `updateDoctor` / `updateStaffAccount` / `updatePatient` /
      `updatePatientConsent` in the data modules — fetch-merge-PUT in dotnet
      mode (the .NET PUTs replace the whole row).
- [x] Backend: `Doctor.StaffAccount` made nullable — the PUT required the nested
      embed object. No schema change.
- [x] Migrated: `admin/doctors/[id]/edit` (+ read), `DoctorForm`, `doctor/profile`
      (+ read), `staff/profile`, `admin/staff` status toggle, `patient/profile`
      (+ read), `patient/privacy-consent`.
- [x] Verified in-browser: edit doctor in admin → `PUT /api/doctors/{id}` →
      persisted in MSSQL (`specialization` + `bio`). All three PUTs return 200.
- Note: after a write the Supabase copy is stale until `import-from-supabase.mjs`
      re-runs — expected; irrelevant once Supabase is retired (Phase 7).

### Remaining
- [ ] `doctor_services` / `doctor_day_statuses` data modules + read sites
      (booking wizard, walk-in day-status, `admin/doctors/[id]/edit` services list)
- [ ] **`doctor_schedules` / `doctor_blocked_dates` — parity-only, do not deepen.**
      No slots (walk-in FCFS queue); vestigial, replaced in **8.3**.
- [ ] Per-patient detail pages (`admin/patients/[id]`, `staff/patients/[id]`;
      `doctor/patients/[id]` derives from bookings → Phase 4)
- [ ] Patient create (`admin/patients` Add Patient, walk-in new-patient) — needs a
      `POST /api/patients` endpoint (only PUT exists)

### Backend
- [ ] `GET /api/patients/{id}`, `/me`, list (search by `patient_code`/name/contact,
      keyset paging — see §16.2, but a basic version is fine here).
- [ ] `PUT /api/patients/{id}`, `/{id}/consent`.
- [ ] `GET /api/doctors` (+ nested `staff_accounts(full_name,email,status)`),
      `/api/doctors/admin`, `/me`, `PUT /api/doctors/{id}`.
- [ ] Schedules: `GET/PUT /{id}/schedules` (upsert by `doctor_id,day_of_week`).
- [ ] `GET/POST/DELETE /{id}/blocked-dates`; `GET/PUT /{id}/day-status`
      (upsert by `doctor_id,status_date`).
- [ ] `GET/PUT /{id}/services`; `staff_accounts` list + `PUT` (`status`,
      `full_name`, `contact_number`).
- [ ] Embed shapes: `doctors(staff_accounts(...))`, `doctors(specialization, staff_accounts(full_name))`.

### Frontend — flip these resources to `dotnet`
`src/lib/data/{patients,doctors,staff,schedules}.ts`; migrate call sites in:
`booking/page.tsx`, `admin/doctors/**`, `admin/staff`, `admin/patients/**`,
`admin/walk-in`, `doctor/profile`, `doctor/schedule`, `doctor/patients/**`,
`patient/doctors/**`, `staff/patients/**`, `staff/doctor-status`,
`components/admin/DoctorForm.tsx`.

### Verify
Doctor directory + availability on the booking page, admin doctor edit
(schedule + services + blocked dates), admin staff management, patient/staff
patient lists — all served by .NET, parity diff empty.

---

## Phase 3 — Services & catalog

**Resources:** `services`, `medicines`, `vital_field_templates`, `icd10_codes`

### Backend
- [ ] `GET /api/services` (+ `is_active` filter), `GET /{id}`, `POST`, `PUT /{id}`.
- [ ] `GET /api/medicines?q=` autocomplete.
- [ ] `GET /api/vital-field-templates` (defaults + customs).
- [ ] `GET /api/icd10-codes?q=` — **new**, fills contract §15 gap (UI doesn't query
      it today but Phase 5 diagnosis picker will).

### Frontend
`admin/services`, `components/doctor/PrescriptionForm.tsx` (medicine search),
vitals template load in `components/doctor/VitalsEditor.tsx`.

### Verify
Services CRUD on .NET; medicine autocomplete; templates load in the vitals editor.

---

## Phase 4 — Bookings, booking_services, payments (core flow)

Biggest phase — split into 4 PRs per repo.

### 4a — Booking creation (patient + walk-in)
- BE: `POST /api/bookings` (+ `booking_services`, creates the `payments` row),
      queue number = count of that doctor's bookings for the date,
      `POST /api/bookings/walk-in` (`is_walk_in`, `payment_mode=PayAtClinic`).
- FE: `booking/page.tsx` wizard, `staff/walk-in`, `admin/walk-in`.

### 4b — Booking lists & detail
- BE: `GET /api/bookings/me`, `/staff/today`, `/staff/all`, `/doctor/today`,
      `/doctor/today-summary`, `/doctor/patients`, `GET /{id}` with
      `patients(...)`, `doctors(staff_accounts(full_name))`,
      `booking_services(services(name,price))`, `payments(status)` embeds.
- FE: `patient/bookings/**`, `staff/bookings/**`, `admin/bookings/**`,
      `admin/calendar`, `doctor/appointments/**`.

### 4c — Booking status transitions
- BE: `PUT /api/bookings/{id}/status` (Pending → Confirmed → CheckedIn →
      InProgress → Completed; Cancelled/NoShow/OnHold/Rescheduled), with the
      cancel fields (`cancelled_by_user_id`, `cancellation_reason`).
- FE: status controls on staff/doctor/admin booking screens.

### 4d — Payments
- BE: `GET /api/payments/{id}`, `booking/{bookingId}`,
      `POST /{id}/confirm|waive|refund` (contract §4 payment columns;
      waive = Doctor/Admin, refund = Admin — enforce roles).
- FE: `staff/payments`, confirm-payment modal, `admin/bookings/[id]` waive/refund.

### Verify (whole phase)
Book online (PayAtClinic) → staff confirms payment → check-in → doctor marks
InProgress → Completed. Walk-in with queue number. Cancel with reason. Refund.
Parity diff empty on all booking/payment reads.

---

## Phase 5 — Clinical records

**Resources:** `consultations`, `consultation_diagnoses`, `patient_vital_readings`,
`follow_ups`, `prescription_groups`, `prescription_line_items`,
`prescription_templates` (+items), `doctor_favorite_medicines`, `soap_templates`,
`soap_phrases`, `audit_logs` (write)

### Backend (build the controllers that don't exist yet)
- [ ] `PUT /api/consultations/by-booking/{bookingId}` — upsert on `booking_id`.
- [ ] `PUT /api/consultations/{id}/diagnoses` — replace-all `consultation_diagnoses`
      (enforce single `Primary`).
- [ ] `PUT /api/vitals/by-booking/{bookingId}` — upsert each reading on
      `(booking_id, template_id)`.
- [ ] `PUT/DELETE /api/follow-ups/by-consultation/{id}` — upsert on `consultation_id`.
- [ ] `prescription_groups` + `prescription_line_items` CRUD; `prescription_templates`
      (+items) CRUD; `doctor_favorite_medicines` CRUD.
- [ ] `soap_templates` / `soap_phrases` CRUD.
- [ ] `POST /api/audit-logs` — written on consultation amend
      (`entity_type=Consultation`).
- [ ] Embeds: `consultation_diagnoses(custom_description,type)`,
      `follow_ups(follow_up_date,instructions)`,
      `prescription_line_items(*)`, `bookings(appointment_date, doctors(staff_accounts(full_name)))`.

### Frontend
`doctor/consultation/[bookingId]/**` (+ `vitals/` subpage),
`doctor/patients/[id]/**` (+ `prescriptions/create`, `prescriptions/[id]`),
`components/doctor/{VitalsEditor,PrescriptionForm,SoapFieldToolbar}.tsx`,
`patient/medical-records`, `patient/prescriptions`.

### Verify
Doctor opens a CheckedIn booking → records vitals → SOAP + diagnosis → Rx +
follow-up → Complete. Re-open → Amend → `audit_logs` row appears. Patient sees
the consultation, Rx, and follow-up in medical records.

---

## Phase 6 — Patient files, vaccinations, reviews

**Resources:** `patient_documents`, `patient_lab_results`, `patient_vaccinations`,
`reviews`, file storage

### Backend
- [ ] `IFileStorageService` local disk (`App_Data/uploads/{patientId}/{bookingId}/{ts}-{name}`),
      max 10 MB, MIME allowlist (pdf/jpeg/png/webp/gif/msword/docx).
- [ ] `POST /api/patient-files/documents` + `.../lab-results` (multipart) → save file,
      insert row, return the row with `file_url`.
- [ ] `GET /api/files/{**path}` (or static files) — `file_url` must resolve.
- [ ] `GET/POST /api/patient-vaccinations` (write path is a §15 gap — build it).
- [ ] `POST /api/reviews` (`booking_id, doctor_id, patient_id, rating, comment`).

### Frontend
`src/lib/patientUploads.ts` → multipart POST to .NET, `file_url` from the
response (drop `supabase.storage`); `patient/documents`, `patient/lab-results`,
`patient/vaccinations`, `patient/reviews/[bookingId]`.

### Verify
Upload a PDF on `patient/documents` → row created, file downloads from
`file_url`. Submit a review. Vaccination list renders.

---

## Phase 7 — Admin settings, announcements, audit log, reports — then remove Supabase

**Resources:** `clinic_settings`, `clinic_operating_hours`,
`clinic_accepted_payment_methods`, `announcements`, `audit_logs` (read),
`v_doctor_ratings`, `v_daily_booking_summary`, `v_unpaid_completed_visits`,
`v_pending_follow_ups`

### Backend
- [ ] `GET/PUT /api/settings` (singleton `id=1`).
- [ ] `GET/PUT /api/admin/operating-hours[/{dayOfWeek}]`; `GET /api/admin/payment-methods`.
- [ ] `announcements` CRUD; `GET /api/audit-logs` (filter by entity).
- [ ] `GET /api/reports/{doctor-ratings,daily-booking-summary,unpaid-completed-visits,pending-follow-ups}`.

### Frontend
`admin/settings`, `admin/announcements` + `staff/announcements`,
`admin/audit-logs`, `admin/reports`, and the four dashboards
(`{patient,doctor,staff,admin}/dashboard`).

### Cleanup (the payoff)
- [ ] Delete `src/lib/supabase/{client,server,admin}.ts`, remove `@supabase/*`
      from `package.json`, drop `NEXT_PUBLIC_SUPABASE_*` from env + `env.example`.
- [ ] Remove `API_MODE` flag + every Supabase branch in `src/lib/data/`.
- [ ] `grep -r "supabase" src` → only comments/historical docs remain.
- [ ] `src/data/supabase-types.ts` → keep as the type source or regenerate from
      the .NET DTOs; note the decision in the PR.

### Verify
Full app, every role, no Supabase env vars set. `next build` green with
`@supabase/*` uninstalled.

---

## Phase 8 — Contract §16 amendments

Each sub-phase = schema migration → endpoint → FE screen → verify. Independent;
order by clinic priority.

### ✅ Confirmed clinic facts (owner, this migration) — build 8.3/8.4/8.6 to these

- **No appointment slots. Walk-in only, FCFS, manual queueing.** The whole slot
  model in the schema (`doctor_schedules`, `bookings.slot_*`, `doctors.slot_*`)
  is vestigial and gets replaced by the queue, not extended.
- **One doctor.** Hours are the clinic's, single session per day (NOT the
  letterhead's split-shift — owner explicitly rejected that):
  | Day | Hours |
  |-----|-------|
  | Mon–Fri | 08:00 – 17:00 |
  | Saturday | 10:00 – 17:00 |
  | Sunday | closed |
  → `clinic_operating_hours` seed fix (current Sat 08:00–12:00 is wrong).
- **Fee schedule (§16.6), clinic-wide flat, doctor-set at consultation:**
  | Item | ₱ |
  |------|---|
  | Standard consultation | 450 |
  | Senior citizen / PWD | 400 |
  | Follow-up consultation | 350 |
  | Medical Certificate (add-on) | +50 |
  Senior/PWD discount model still parked (§16.6 working assumption: −20% of total).
- **Identity (§16.8):** Grace Medical Clinic · Allyn Grace T. España-Gavino, MD
  (Family & Community Medicine / Adult & Pedia) · 3ML Quezon National Highway,
  Buaya, Lapu-Lapu City · 09285612976 · License No. 0125232.

| # | Amendment (contract ref) | Backend | Frontend |
|---|---|---|---|
| 8.1 | Staff vitals at intake — `recorded_by_user_id` (§16.1) | migration adds the column (already stubbed in `WireModels.cs`); vitals write authorizes any `is_staff_like` | vitals step in `staff/walk-in` / `staff/bookings/[id]`, reuse `VitalsEditor` |
| 8.2 | Server-side pagination + search (§16.2) | keyset cursor + `q`/`sort` on every list endpoint; response envelope `{ rows, next_cursor, total }` | list screens consume the envelope; move `doctor/patients` card grid → `DataTable` |
| 8.3 | **Manual walk-in FCFS queue** (§16.3) — replaces slots entirely | queue model (per-day sequence number), `POST /api/queue/*`, ticket payload; deprecate `bookings.slot_*` / `doctors.slot_*` / `doctor_schedules` | `staff/walk-in` reserve+queue flow, queue board, printable ticket; remove slot pickers from the booking wizard |
| 8.4 | Fee schedule + booking tagging (§16.6) | fee columns on `clinic_settings` (`fee_consultation` 450, `fee_follow_up` 350, `fee_senior_pwd` 400, `fee_med_cert` 50, `discount_pct` 0.20); `bookings.visit_type` enum `('New','FollowUp')` (doctor-set); `patients.senior_id_number`/`pwd_id_number`; `bookings.discount_category`/`discount_amount` snapshot; server-side fee recompute on consultation-complete | fee shown as provisional at reservation; doctor sets `visit_type` + issues med-cert at consultation; secretary collects against the final amount |
| 8.5 | Printable clinical docs (§16.7–16.8) | `medical_certificates` (§16.8 columns), `prescription_line_items` new cols (`timing`, `meal_relation`, `duration_kind`, `duration_value`, `indication`), `lab_test_catalog` + `lab_orders.lab_test_id`; print-letterhead endpoint | Rx pad, med-cert, lab-request print views matching the real paper forms |
| 8.6 | Real clinic identity + **single-session** hours (§16.8, owner-corrected) | seed `clinic_settings` (name "Grace Medical Clinic", address, contact 09285612976) + `doctors` (name "Allyn Grace T. España-Gavino, MD", license `0125232`); correct `clinic_operating_hours` to Mon–Fri 08:00–17:00 / Sat 10:00–17:00 / Sun closed. **No split-shift schema change needed** — owner rejected the letterhead's evening session | settings screen, app title string |
| 8.7 | Doctor earnings / visits dashboard (§16.9) | `v_doctor_earnings` view + `GET /api/reports/doctor-earnings` (doctor-only) | `doctor/dashboard` earnings panel |
| 8.9 | Broken-today fixes + infra + compliance (§17) | §17.1 bug fixes; §17.2 rate-limit, structured logging, request IDs, DB backup; §17.3 data-retention / consent audit | as each surfaces |

---

## Sequencing summary

```
0  scaffold ────────────────────────────────────────── both repos, no behaviour change
1  auth ───────────────────────────────────────────────  hard cutover
2  identity/directory reads ─┐
3  services/catalog ─────────┤  read-heavy, low risk
4  bookings + payments ──────┤  core flow (4a→4d)
5  clinical records ─────────┤  build missing controllers
6  files / vax / reviews ────┤  + file storage
7  admin/settings/reports ───┘  → delete Supabase
8  §16 amendments ──────────────────────────────────── independent, priority-ordered
```

Phases 2–6 can overlap once their backend endpoints are merged; keep one FE
integration PR in flight at a time to keep parity diffs readable.
