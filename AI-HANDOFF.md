# AI-HANDOFF — Project02-be (ClinicApp.Api, .NET 10)

> **Who this is for:** an AI agent (or developer) picking up this repo cold.
> **Companion file:** `../Project02-fe/AI-HANDOFF.md` (Next.js frontend). Read both. The FE↔BE contract summary is in §14 below and mirrored there.
> **Written:** Nov 20, 2026 (Asia/Manila), from reading the actual code at `main @ fde7164` (`github.com/jessiejek/Project02-be`).
> Anything not verified is marked **(unverified)** with what was checked.

---

## 0. TL;DR

- **Product:** clinic management system for **Dr. Grace Gavino Medical Clinic** (the FE brand). The printed letterhead in the FE says **"ALLYN GRACE T. ESPAÑA-GAVINO, MD, Family and Community Medicine, Adult and Pedia, GRACE MEDICAL CLINIC, 3ML Quezon National Highway, Buaya, Lapu Lapu City"**. Jessie calls it the Gavino Clinic app / "Project02". It's sold to a **non-technical Family Medicine doctor in the Philippines**.
- **This repo:** the REST API + SignalR hub + EF Core schema. ASP.NET Core `net10.0`, EF Core 10 (SQL Server; SQLite fallback for demo), JWT HS256 auth, snake_case JSON.
- **Run locally:** Docker SQL Server `clinic-mssql` (DB `ClinicAppDb`) under Colima → `set -a && source .env && set +a && cd src/ClinicApp.Api && dotnet run --urls http://localhost:5055`.
- **Tests:** `tests/ClinicApp.Api.Tests` (xUnit + WebApplicationFactory). Last known: **361 passing**.
- **The README.md in this repo is stale** (it still says the FE "runs against Supabase until the rewire phase"). The migration is **done**: the FE talks only to this API, Supabase is torn out of the FE bundle. Trust this file and `INTEGRATION_ROADMAP.md` over README "Next steps".

---

## 1. Purpose, users and roles

Outpatient clinic app: patients book online (or walk in at the desk), front desk queues and collects payment, the doctor charts the consultation (SOAP notes, vitals, Rx, labs, vaccinations, follow-up, medical certificate, professional fee decision) and prints forms; admin manages doctors/staff/settings/reports.

| JWT `role` | Who | Notes |
|---|---|---|
| `Patient` | Self-registered patients | Own data only (bookings, records, Rx, files, privacy requests). |
| `Staff` | Front desk | Queue, walk-ins, payments (can record a waiver only if the doctor decided Waive). |
| `Doctor` | The doctor(s) | Own patients/visits only; templates are private per doctor. |
| `Admin` | Clinic admin | Everything incl. settings, staff/doctor management, audit logs, refunds. |

"Staff-like" = Admin + Staff + Doctor. Full who-can-do-what: **`docs/AUTHZ_MATRIX.md`** (source of truth; enforced in controllers, covered by `IdorTests`, `RoleTests`, `FileAccessTests`, `DoctorScopeTests`). Sections there: People · Bookings & queue · Payments · Clinical record · Files (PHI) · Reviews · Doctor tooling · Admin/reference · Gaps fixed · Deliberate differences from RLS · Residual risks · Test map.

**Identity model:** `users` (auth, in `ClinicApp.Auth`) 1:1 `profiles` (role). Staff/Doctor/Admin have a `staff_accounts` row; **`staff_accounts.staff_id` == `doctors.doctor_id`** (shared PK) for doctors. Patients have a `patients` row (`user_id` nullable: walk-in/guest patients have no login).

---

## 2. Tech stack & versions (from the .csproj files)

| Thing | Version / detail |
|---|---|
| .NET | `net10.0` for all 5 projects |
| ASP.NET Core JWT bearer | `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.11` |
| OpenAPI | `Microsoft.AspNetCore.OpenApi 10.0.11` (mapped in Development only: `/openapi/v1.json`) |
| EF Core | `Microsoft.EntityFrameworkCore.SqlServer` / `.Sqlite` / `.Design` / `.Tools` `10.0.11` |
| Naming | `EFCore.NamingConventions 10.0.1` → `UseSnakeCaseNamingConvention()` |
| Auth lib | `Microsoft.AspNetCore.Identity 2.3.12` (password hasher only), `System.IdentityModel.Tokens.Jwt 8.22.0` |
| Realtime | ASP.NET Core SignalR (hub at `/hubs/clinic`) |
| Tests | xUnit 2.x, `Microsoft.AspNetCore.Mvc.Testing 10.0.*`, `Microsoft.NET.Test.Sdk 17.*` |
| `dotnet-tools.json` | pins `dotnet-ef 9.0.0` (local tool). README also suggests a global `dotnet-ef`. |
| DB (local) | SQL Server 2022 Express, image `mcr.microsoft.com/mssql/server:2022-latest`, `linux/amd64` under Colima + Rosetta |
| Solution | `ClinicApp.slnx` (new XML solution format) |

---

## 3. Repo layout

```
Project02-be/
├── AI-HANDOFF.md                 ← this file
├── README.md                     original setup notes (partly STALE, see §0)
├── INTEGRATION_ROADMAP.md        phase-by-phase BE build + FE integration log (Phase 0–8); Supabase teardown done 2026-09-10
├── ClinicApp.slnx                solution: 4 src projects + 1 test project
├── docker-compose.yml            local SQL Server container "clinic-mssql" (reads SQL_SA_PASSWORD from .env)
├── .env.example                  env var NAMES (copy to gitignored .env)
├── deploy-credentials.md         gitignored, local only — DO NOT read/print/commit
├── dotnet-tools.json             dotnet-ef local tool
├── .github/workflows/deploy-backend.yml   push to main ⇒ macOS runner runs deploy/publish.sh (FTP deploy)
├── deploy/
│   ├── publish.sh                dotnet publish (framework-dependent win-x64) + per-file curl FTP upload; never deletes remote files (App_Data/uploads safe)
│   ├── migrate.sql               full idempotent EF script (early migrations) for the hosted DB
│   ├── migrate-phase96-97.sql    hosted-DB script for Phase96BookingCreatedBy + Phase97QueueCounter
│   └── migrate-templates-revamp.sql   hosted-DB script for 20261119103723_TemplatesRevamp (MUST be applied on non-local DBs; not applied by author)
├── docs/
│   ├── AUTHZ_MATRIX.md           authorization rules (RLS → .NET)
│   └── STATE_MACHINES.md         booking/payment/consultation state rules
├── publish/, publish-sc/         build output (gitignored)
├── App_Data/uploads/             local uploaded PHI files (gitignored)
├── src/
│   ├── ClinicApp.Api/            ASP.NET host
│   │   ├── Program.cs            DI, JSON (snake_case + string enums), EF provider switch, JWT, rate limiting, CORS, seeding, HSTS, hub, /health
│   │   ├── appsettings.json      base config (Windows SQLEXPRESS conn string, Jwt issuer/audience/expiry, CORS, FileStorage, RateLimiting)
│   │   ├── appsettings.Development.json  CORS 3000/3001, loose rate limits, provider SqlServer
│   │   ├── appsettings.Production.json   logging only, no secrets
│   │   ├── Properties/launchSettings.json  default http://localhost:5000 (Jessie overrides with --urls :5055)
│   │   ├── Controllers/          35 controllers (see §8)
│   │   ├── Hubs/ClinicHub.cs     SignalR: groups "staff" (Admin+Staff) and "doctor:{doctorId}"
│   │   ├── Auditing/             AuditLogWriter (ChangedFields/DiffDetails — field names, not clinical text), AccessLog
│   │   ├── Middleware/RequestContextMiddleware.cs   correlation id + structured request logging
│   │   ├── Security/             ActorResolver/Actor (who is calling: IsPatient/IsDoctor/ActsAsDoctor/CanAccessPatient), AccountGuard (deactivated accounts)
│   │   └── Validation/           Check.cs, Rules.cs (input validation helpers)
│   ├── ClinicApp.Domain/         entities + enums, NO EF dependency
│   │   ├── Entities/*.cs         Admin, Booking, Clinical, Identity, LabsAndFiles, PatientAllergy, Prescriptions, Privacy, Scheduling, Services, Views
│   │   ├── Enums/Enums.cs        all enums (serialized as exact member names)
│   │   ├── StateMachines.cs      BookingStatusMachine, PaymentStatusMachine
│   │   ├── ClinicFees.cs         flat fee schedule computation
│   │   └── ClinicClock.cs        Asia/Manila (UTC+8) business dates — ALWAYS use this, never UtcNow.Date
│   ├── ClinicApp.Infrastructure/
│   │   ├── ClinicAppDbContext.cs (+ Factory for design-time)
│   │   ├── Migrations/           EF migrations (see §10)
│   │   ├── Icd10Seeder.cs        loads WHO ICD-10 2019 (12,221 codes) at every startup, idempotent
│   │   ├── Icd10UsToWho.cs       remaps US ICD-10-CM leftovers to WHO codes in saved data
│   │   ├── Data/Icd10Who2019/    verbatim WHO files + SOURCE.md (licence note) — embedded resource
│   │   ├── BuiltInTemplateCatalog.cs / BuiltInTemplateCopier.cs   built-in templates copied into each doctor's own rows
│   │   ├── SeedData.cs           HasData seeds (operating hours, medicines, vital templates, payment methods, settings…)
│   │   ├── DevDataSeeder.cs      Development-only login accounts (@clinic.test)
│   │   ├── PatientCodes.cs       patient_code allocator (sequence)
│   │   ├── QueueSequencer.cs     daily queue numbers (queue_counters table)
│   │   ├── SqliteDemoViews.cs    view DDL for the SQLite fallback
│   │   └── Files/                LocalFileStorageService, FileSignature (magic-byte checks), IFileStorageService
│   └── ClinicApp.Auth/           User/RefreshToken entities, TokenService (JWT), PasswordHasherService, PasswordPolicy, JwtOptions, Dtos (camelCase auth wire)
└── tests/ClinicApp.Api.Tests/    xUnit integration tests (see §12)
```

Related docs that live in the **FE repo** but govern this API: `../Project02-fe/DOTNET_FRONTEND_CONTRACT.md` (wire format: JSON keys, enum strings, embed shapes — **"Source of truth. Read it before changing any DTO."**) and `../Project02-fe/DOTNET_BACKEND_PLAN.md`.

---

## 4. How to run locally (macOS, Jessie's MacBoy)

```bash
# 1. DB container (once per boot)
colima start --cpu 4 --memory 6 --disk 40 --vm-type=vz --vz-rosetta
cd ~/Documents/Allyn/Project02-be && docker compose up -d      # container: clinic-mssql, localhost:1433, DB: ClinicAppDb

# 2. API (Jessie's actual command; port 5055, NOT the launchSettings 5000)
cd ~/Documents/Allyn/Project02-be
set -a && source .env && set +a && cd src/ClinicApp.Api && dotnet run --urls http://localhost:5055
#   background variant used by agents:
#   ... && nohup dotnet run --urls http://localhost:5055 > /tmp/project02-be-dev.log 2>&1 &

# 3. Migrations (when a new one is added)
cd src && dotnet ef database update --project ClinicApp.Infrastructure --startup-project ClinicApp.Api
#   (needs the .env loaded so ConnectionStrings__DefaultConnection points at the local container)
```

- `ASPNETCORE_ENVIRONMENT` is Development under `dotnet run` (launchSettings) ⇒ OpenAPI, DevDataSeeder, `/api/dev/import` enabled.
- Startup always runs `Icd10Seeder.SeedAsync` (all environments), then DevDataSeeder in Development.
- Smoke checks: `GET /health` (no DB), `GET /api/settings` (DB reachable), `GET /api/version` (version.txt + DB/migration status).
- **Zero-infra fallback:** `Database:Provider=Sqlite` + `DefaultConnection=Data Source=App_Data/clinicapp.dev.db` ⇒ `EnsureCreated()` + SQLite views (no migrations).
- Dev logins (Development only, seeded by `DevDataSeeder`): `admin@clinic.test`, `staff@clinic.test`, `doctor@clinic.test`, `phase5doctor@clinic.test`, `patient@clinic.test`, `spare-staff@clinic.test`. The dev password is in `../Project02-fe/e2e/README.md` / `account-list.txt` (not repeated here).
- **Rules:** never kill processes by name, never point tests at the live `:5055`/`:3000`, back up the local DB before any DB-changing work (§15).

---

## 5. Configuration & environment variables (NAMES only)

Secrets live in the gitignored **`.env`** at repo root (never read/print it; use `.env.example`).

| Name | Where used | Purpose |
|---|---|---|
| `SQL_SA_PASSWORD` | docker-compose | sa password of the local container |
| `ConnectionStrings__DefaultConnection` | API (overrides appsettings) | local: `Server=localhost,1433;Database=ClinicAppDb;User Id=sa;…;TrustServerCertificate=True;Encrypt=False` |
| `Jwt__Secret` | API | HS256 signing key, 32+ chars. **Must equal FE `JWT_SECRET`.** Production refuses to start if it's short or looks like a placeholder (regex `change|dev|placeholder|replace|example|secret`). |
| `ASPNETCORE_ENVIRONMENT` | API | Development / Production |
| CI only (GitHub Secrets/Vars): `FTP_HOST`, `FTP_USER`, `FTP_PASS`, `FTP_PORT`, `FTP_REMOTE_DIR`, `BACKEND_URL` | deploy/publish.sh | FTP deploy to the Windows host (site4now-style shared hosting) |

appsettings keys: `Database:Provider` (SqlServer|Sqlite), `Jwt:{Secret,Issuer=ClinicApp,Audience=ClinicApp,AccessTokenExpiryMinutes=60,RefreshTokenExpiryDays=7}`, `Email:{FromAddress,FromName,SmtpHost,SmtpPort,SmtpUser,SmtpPass}` (all empty in repo ⇒ email not configured), `ClientBaseUrl`, `Cors:AllowedOrigins`, `FileStorage:{RootPath,PublicPathPrefix,MaxFileSizeBytes=10MB}`, `RateLimiting:{GlobalPermitPerMinute=300,AuthPermitPerMinute=10}` (dev: 6000/120).

Note: `appsettings.json` contains a committed dev `Jwt:Secret` placeholder and a Windows `PHL8DPROG004\SQLEXPRESS` integrated-security connection string (Jessie's work-PC origin). Both are overridden by `.env` locally.

---

## 6. Wire format (critical)

- **Data resources: snake_case JSON** (`JsonNamingPolicy.SnakeCaseLower`), **enums as exact member names** (`"PayAtClinic"`, `"FollowUp"`, `"CheckedIn"`; integers rejected). Do not change the enum converter's naming policy.
- **`/api/auth/*` is camelCase** (`accessToken`, `refreshToken`, `user.fullName`, …) — see `ClinicApp.Auth/Dtos.cs`.
- `ReferenceHandler.IgnoreCycles`; navigations shaped with `[JsonPropertyName]`/`[JsonIgnore]` to match contract §6 embeds.
- Errors: `{ "message": "..." }` (400/403/404/409) or ASP.NET ProblemDetails. 429 on rate-limit.
- Paged lists use `Controllers/PagedResult.cs`.

---

## 7. Authentication & authorization

- `POST /api/auth/login` → `{ accessToken, refreshToken, user{ id, fullName, email, role, avatarUrl, isFirstLogin, phoneNumber } }`. Access JWT 60 min, refresh 7 days (refresh tokens stored hashed in `refresh_tokens`, rotated on `/refresh-token`, revoked on `/logout`).
- JWT: HS256, issuer/audience `ClinicApp`, `ClockSkew` 30 s. Role claim = `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`), subject = NameIdentifier.
- **`OnTokenValidated` → `AccountGuard.IsActiveAsync`:** a deleted user or a staff account with `status = Inactive` is rejected immediately (B-32), even with an unexpired token.
- **Known lag ("JWT demote lag"):** the *role* is baked into the token. Changing someone's role does not take effect until their access token expires/refreshes (≤ 60 min). Only deactivation is immediate.
- SignalR: token accepted from `?access_token=` **only** for paths under `/hubs`.
- Lockout/password: `PasswordPolicy` (FE mirrors min length 12 via `PASSWORD_MIN_LENGTH`), `users.failed_login_count` / `locked_until` (PrivacyCompliance migration); tests: `PasswordAndLockoutTests`, `AccountSecurityTests`.
- Rate limiting: global fixed window per client IP; `AuthController` has `[EnableRateLimiting("auth")]`.
- Ownership checks use `ActorResolver` (`actor.ActsAsDoctor(doctorId)`, `actor.CanAccessPatient(patientId)`, patient forced to own `patient_id`). Never trust `patient_id`/`doctor_id` from a request body when a booking exists — the booking is the source of truth.
- `POST /api/auth/google` and `/facebook` exist as routes (social login placeholders — **unverified** what they return; checked only the route attributes).

---

## 8. API endpoints (every controller)

Legend: Auth = required roles (`any` = any signed-in user; `anon` = no auth). Paths are relative to the API base (local `http://localhost:5055`).

### Auth — `AuthController` `api/auth` (rate-limited "auth")
| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | /login | anon | Email+password → token pair + user |
| POST | /register | anon | Patient self-registration (creates user+profile+patient; accepts `acceptedPrivacyNotice`) |
| POST | /refresh-token | anon | Rotate refresh token |
| POST | /logout | anon | Revoke refresh token |
| GET | /me | any | Current user |
| POST | /forgot-password | anon | Request reset (never reveals account existence) |
| POST | /reset-password | anon | Reset with token |
| POST | /set-password | any | First-login / invited user sets password |
| POST | /change-password | any | Verify current, set new |
| POST | /resend-verification | anon | Re-send verification |
| POST | /avatar | any | Upload avatar |
| POST | /invite | Admin | Invite staff/doctor account |
| DELETE | /users/{id} | Admin | Delete/revoke a user (e.g. revoke invite) |
| POST | /google, /facebook | anon | Social login stubs (unverified) |

### Bookings — `api/bookings` (class: any)
| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | / | any (scoped) | List/filter bookings |
| GET | /{id} | any (scoped) | One booking |
| POST | / | Admin,Staff | Create booking at desk |
| PUT | /{id}/status | Admin,Staff,Doctor | Status change via `BookingStatusMachine` |
| POST | /walk-in | Admin,Staff | Walk-in booking |
| GET | /me | any (patient) | My bookings |
| PUT | /{id}/cancel | Patient | Cancel own booking (Pending → Cancelled only) |
| PUT | /{id}/reschedule | Admin,Staff | Reschedule |
| GET | /closed-dates | Patient,Staff,Admin | Dates the clinic/doctor is closed (`?days=`) |
| POST | /book | Patient | Online booking |
| GET | /doctor/today, /doctor/today-summary, /doctor/patients | Doctor | Doctor's day / summary / patients |
| GET | /staff/today, /staff/all, /staff/for-payment | Admin,Staff | Front-desk lists |

### Queue — `api/queue` (class: Admin,Staff,Doctor)
| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | / | Admin,Staff | Check in a walk-in / add to queue (assigns daily queue number) |
| GET | / | staff-like | Today's queue |
| PUT | /{bookingId}/check-in | Admin,Staff | Check in |
| PUT | /{bookingId}/cancel | Admin,Staff | Cancel |
| PUT | /{bookingId}/call · /hold · /complete · /no-show | staff-like (doctor only own) | → InProgress / OnHold / Completed / NoShow (state machine) |

### Consultations — `api/consultations` (class: any)
| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | / | any (scoped) | List (`patientId`, `doctorId`, `bookingId`) |
| GET | /{id} | any (scoped) | One |
| GET | /by-booking/{bookingId} | any (scoped) | Consultation for a booking (1:1) |
| PUT | /by-booking/{bookingId} | Doctor,Admin | **Upsert** (Draft/Completed/Amended), PF decision, fee-line choices (visit type, med cert, Senior/PWD) → recomputes booking fee |
| GET | /{id}/diagnoses | any (scoped) | Diagnoses |
| PUT | /{id}/diagnoses | Doctor,Admin | Replace-all diagnoses (2-minute window after Completed, else must Amend) |

### Clinical children
| Controller / base | Endpoints | Auth (write) |
|---|---|---|
| Vitals `api/vitals` | GET `/` · PUT `/by-booking/{bookingId}` | Staff,Doctor,Admin (records `recorded_by_user_id`) |
| FollowUps `api/follow-ups` | GET `/` · GET/PUT/DELETE `/by-consultation/{consultationId}` | Doctor,Admin |
| LabOrders `api/lab-orders` | GET `/` · GET `/by-consultation/{id}` · GET `/by-booking/{id}` · PUT `/by-consultation/{id}` (replace-all) | Doctor,Admin |
| MedicalCertificates `api/medical-certificates` | GET `/` · GET `/by-consultation/{id}` · GET `/by-booking/{id}` · PUT/DELETE `/by-consultation/{id}` | Doctor,Admin |
| Vaccinations `api/patient-vaccinations` | GET `/` · GET/PUT `/by-consultation/{id}` · POST `/` | PUT: Doctor,Admin; POST: Doctor,Staff,Admin |
| Prescriptions `api/…` | GET `prescription-groups`, GET/DELETE `prescription-groups/{id}`, PUT `prescription-groups/by-booking/{bookingId}` | Doctor,Admin |
| | GET/POST `prescription-templates`, PUT/DELETE `prescription-templates/{id}` | GET: staff-like; write Doctor,Admin |
| | GET/POST `doctor-favorite-medicines`, PUT `…/order`, PUT/DELETE `…/{id}` | Doctor,Admin |

### Doctor templates (private per doctor; GET triggers built-in copies)
| Controller | Endpoints | Auth |
|---|---|---|
| Soap `api` | GET/POST `soap-phrases`, PUT `soap-phrases/order`, PUT/DELETE `soap-phrases/{id}`; GET/POST `soap-templates`, PUT/DELETE `soap-templates/{id}` | Doctor,Admin |
| DoctorDiagnosisTemplates `api/doctor-diagnosis-templates` | GET, POST, PUT/{id}, DELETE/{id} | Doctor,Admin |
| MedicalCertificateTemplates `api/medical-certificate-templates` | GET, POST, PUT/{id}, DELETE/{id} | Doctor,Admin |
| Templates `api/templates` | POST `restore-built-ins?doctorId=&kind=soap|rx|cert|diagnosis|phrase` (favorite ⇒ 400) | Doctor,Admin |

### People & directory
| Controller | Endpoints | Auth |
|---|---|---|
| Patients `api/patients` | GET `/`, GET `/search`, POST `/` (staff-like); GET `/{id}` (scoped); GET `/me`, `/me/vaccinations`; GET `/{id}/vaccinations`, GET/PUT `/{id}/allergies`, GET `/{id}/lab-values` (staff-like); PUT `/{id}` (scoped); PUT `/{id}/consent` | mixed |
| Doctors `api/doctors` | GET `/` (public), GET `/search` (Admin), GET `/{id}`, GET `/me` (Doctor), GET `/admin` (Admin), PUT `/{id}` (Admin), GET/PUT `/{id}/schedules` (Admin,Doctor), GET/POST `/{id}/blocked-dates`, DELETE `/blocked-dates/{id}` (Admin,Doctor), GET `/{id}/day-status`, GET `/day-statuses`, PUT `/{id}/day-status` (Admin,Doctor,Staff), GET/PUT `/{id}/services` (PUT Admin) | mixed |
| StaffAccounts `api/staff-accounts` | GET `/`, GET `/search` (Admin,Staff); GET `/{id}`; GET `/me`; PUT `/{id}` | any/scoped |
| DoctorServices `api/doctor-services` | GET | public |
| Services `api/services` | GET, GET/{id}; POST, PUT/{id} (Admin) | mixed |

### Payments — `api/payments` (class: any)
| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | /{id}, /booking/{bookingId} | any (scoped) | Read |
| POST | /{id}/confirm | Admin,Staff | Unpaid → Paid (method, OR number, amount received) |
| POST | /{id}/waive | Admin,Staff,Doctor | Unpaid → Waived. **Staff only if the consultation's `pf_decision = 'Waive'`**; doctor only own bookings. Sets `amount_due = 0`. |
| POST | /{id}/refund | Admin | Paid → Refunded |
All push `PaymentUpdated` over SignalR to "staff" + "doctor:{id}".

### Files (PHI) — `PatientFilesController` `api`
GET/POST `patient-documents`, GET `patient-documents/{id}/file`; GET/POST `patient-lab-results`, GET `patient-lab-results/{id}/file`. Files are **not** static-served; streamed only through these authorized endpoints. Magic-byte check in `FileSignature`, max 10 MB.

### Privacy (Philippine Data Privacy Act features) — `PrivacyController` `api`
POST `patients/{id}/consent-decision` (Accepted/Declined/Withdrawn) · GET `patients/{id}/consents` · GET `patients/{id}/export` (data portability) · POST `privacy/data-requests` (Correction/Deletion) · GET `privacy/data-requests/mine` · GET `privacy/data-requests` (Admin) · PUT `privacy/data-requests/{id}` (Admin; Pending/Scheduled/Completed/Rejected, `retain_until`).

### Admin, reference, reporting
| Controller | Endpoints | Auth |
|---|---|---|
| Admin `api/admin` | GET `operating-hours`, PUT `operating-hours/{dayOfWeek}`, PUT `operating-hours`; GET/PUT `payment-methods` | PUT Admin |
| Settings `api/settings` | GET (public), PUT (Admin) — clinic name, fees (`fee_consultation`, `fee_follow_up`, `fee_senior_pwd`, `fee_med_cert`, `discount_pct`), consent version, privacy text, branding | |
| Announcements `api/announcements` | GET; POST, PUT/{id}, DELETE/{id} (Admin) | |
| AuditLogs `api/audit-logs` | GET (Admin,Doctor), GET `/search` (Admin), GET `/patient/{id}/access` (Admin), POST (staff-like) | |
| Reports `api/reports` | GET `doctor-ratings` (staff reports + Patient), `daily-booking-summary`, `unpaid-completed-visits`, `pending-follow-ups` (staff report roles), `doctor-earnings` (Doctor,Admin) | |
| DoctorPerformance `api/doctor/performance` | GET (Doctor,Admin) — Collected/Billed, patients, trends | |
| Reviews `api/reviews` | GET (any), POST (Patient; rules in `ReviewRulesTests`) | |
| Search `api/search` | GET global search (any, scoped) | |
| Lookups | GET `api/medicines`, `api/vital-field-templates`, `api/lab-test-catalog`, `api/icd10-codes` (search WHO catalog) | |
| Version `api/version` | GET anon — version.txt (commit count + commit time) + DB connectivity/migration | |
| Dev `api/dev/import` | POST — Development only (404 otherwise): upsert Supabase rows by PK for parity harness | |
| Minimal API | GET `/health` (no DB, no rate limit); hub `/hubs/clinic` (Admin,Staff,Doctor) | |

---

## 9. Data model (EF entities → snake_case tables)

**Identity:** `users` (email, password_hash, email_confirmed, must_set_password, failed_login_count, locked_until) · `refresh_tokens` · `profiles` (id = user id, role) · `patients` (patient_code, names, dob, sex, contact, address, emergency contact, blood type, PhilHealth/Senior/PWD/HMO numbers, is_guest, consent fields, OB fields is_pregnant/lmp_date/gravida/para/rh_factor, guardian, renal labs) · `patient_allergies` (allergen, reaction, severity, is_nkda) · `staff_accounts` (role Staff/Doctor/Admin, status Active/Inactive/Invited/OnLeave) · `doctors` (PK = staff_id; specialization, consultation_fee, license/PTR/S2 numbers, slot duration/capacity, daily limit).

**Scheduling:** `doctor_schedules` (day_of_week 0–6, start/end) · `doctor_blocked_dates` · `doctor_day_statuses` (Available/RunningLate/UnavailableToday) · `clinic_operating_hours` · `services`, `doctor_services`.

**Booking & money:** `bookings` (patient, doctor, appointment_date, slot times, status, payment_mode Online/PayAtClinic, queue_number, consultation_fee_snapshot, total_fee, amount_due, is_walk_in, visit_type New/FollowUp, discount_category Senior/PWD, discount_amount, med_cert_requested, proof fields, created_by_user_id) · `booking_services` · `payments` (1 per booking; status Unpaid/Paid/Waived/Refunded; method Cash/GCash/Maya/BankTransfer; OR number; confirm/waive/refund audit columns) · `queue_counters` (date → value) · `reviews` (1–5).

**Clinical (1 consultation per booking):** `consultations` (status Draft/Completed/Amended; chief_complaint, subjective, objective, assessment, plan, doctor_notes; **pf_decision 'Charge'|'Waive'|null**, pf_amount, pf_waive_reason; completed_at/by) · `consultation_diagnoses` (icd10_code, custom_description, type Primary/Secondary/Differential/Comorbidity) · `patient_vital_readings` (by booking + `vital_field_templates`) · `follow_ups` (1 per consultation; date, reason, instructions, reminder_enabled, status) · `medical_certificates` (1 per consultation; issue_date, patient_address_snapshot, examined_at, examination_date_from/to, diagnosis_text, recommendations, purpose_exception, come_back_on) · `lab_orders` (+ `lab_test_catalog`, 9 default pre-printed rows) · `patient_vaccinations` · `patient_documents`, `patient_lab_results` (files) · `patient_lab_values` · `icd10_codes` (code, description, level, is_leaf).

**Prescriptions:** `medicines` · `prescription_groups` (1 per booking) → `prescription_line_items` (generic_name, dosage, quantity, instruction, controlled flag, **timing** (e.g. "Breakfast, Dinner"), **meal_relation** ("Before"/"After"), duration_kind/value, indication, route, **as_needed bool?**) · `prescription_templates` → `prescription_template_items` (same structured fields) · `doctor_favorite_medicines` (sort_order).

**Doctor templates:** `soap_templates`, `soap_phrases` (field ChiefComplaint/Subjective/Objective/Assessment/Plan, sort_order), `medical_certificate_templates`, `doctor_diagnosis_templates` (label, body, icd10_code) — each has nullable **`source_builtin_key`**; `doctor_builtin_template_copies` (doctor_id, kind, builtin_key, copied_at; unique index) tracks which built-ins were copied so a deleted copy stays deleted until "restore".

**Admin/privacy:** `clinic_settings` (single row id=1; fees; consent_version; branding) · `clinic_accepted_payment_methods` · `announcements` · `audit_logs` (entity_type, entity_id, action, performed_by, details) · `patient_consents` · `data_requests`.

**Views (read-only, DDL in migrations via `migrationBuilder.Sql`):** `v_doctor_rating`, `v_daily_booking_summary`, `v_unpaid_completed_visits`, `v_doctor_earnings` (period YYYY-MM, completed visits, gross billed, collected, waived), `v_pending_follow_ups` (exact view names: see migrations; entity classes `VDoctorRating`, `VDailyBookingSummary`, `VUnpaidCompletedVisit`, `VDoctorEarnings`, `VPendingFollowUp`).

---

## 10. EF Core migrations (`src/ClinicApp.Infrastructure/Migrations`)

| # | Migration | What |
|---|---|---|
| 1 | 20260907114641_InitialCreate | Base schema (from Project01-BE port of `schema.sql`) |
| 2 | 20260909070207_RenameIcd10CodesTable | `icd10codes` → `icd10_codes` |
| 3 | 20260909091846_FixS2NumberColumnName | doctors.s2_number |
| 4 | 20260909094030_AddUserIdUniqueIndexes | unique user_id indexes |
| 5 | 20260909133513_ClinicalNavigations | nav FKs |
| 6 | 20260909135915_PatientFileBookingNav | files ↔ booking |
| 7 | 20260909144236_Phase8FeeScheduleAndVisitType | fee schedule in settings, visit_type, discount |
| 8 | 20260909144609_Phase81VitalsRecordedBy | vitals recorded_by_user_id |
| 9 | 20260909150308_Phase87DoctorEarningsView | v_doctor_earnings |
| 10 | 20260909150641_Phase85MedicalCertificates | medical_certificates table |
| 11 | 20260909151159_Phase85LabCatalogAndRxFields | lab_test_catalog, Rx structured fields |
| 12 | 20260910060305_Phase91PatientCodeSequence | patient code sequence |
| 13 | 20260910091238_Phase92DoctorDiagnosisTemplates | doctor_diagnosis_templates |
| 14 | 20260910130827_Phase93ConsultationPfDecision | pf_decision/amount/waive_reason |
| 15 | 20260911093550_Phase94MedicalCertificateTemplates | medical_certificate_templates |
| 16 | 20260911131216_Phase95FixEarningsViewCollectedAmount | view fix |
| 17 | 20260913115727_Phase96BookingCreatedBy | bookings.created_by_user_id |
| 18 | 20260918044940_Phase97QueueCounter | queue_counters |
| 19 | 20261031073746_PatientAllergiesAndRxRoute | patient_allergies, Rx route |
| 20 | 20261031130128_ReviewFixesPatientClinicalAndLabValues | patient OB/guardian/renal fields, patient_lab_values |
| 21 | **20261119103723_TemplatesRevamp** | sort_order on phrases/favorites, icd10code on diagnosis templates, structured Rx template item fields |
| 22 | 20261120021409_PrivacyCompliance | users lockout columns, patients.consent_withdrawn_at, patient_consents, data_requests |
| 23 | **20261120070800_Icd10WhoFull** | icd10_codes level/is_leaf + indexes; data loaded by `Icd10Seeder` (12,221 WHO codes) |
| 24 | **20261120071938_TemplatesBuiltInCopies** | source_builtin_key on 5 template tables + doctor_builtin_template_copies |
| 25 | **20261120074154_RxAsNeeded** | nullable as_needed on prescription line + template items |

Local Docker `ClinicAppDb` has all 25 applied (Icd10WhoFull, TemplatesBuiltInCopies, RxAsNeeded applied Nov 20).

**Hosted / non-local databases:** migrations are applied by hand-run SQL, not on startup. Only these scripts exist in `deploy/`: `migrate.sql` (early full script), `migrate-phase96-97.sql`, **`migrate-templates-revamp.sql` (must be applied on any non-local DB; the author did not apply it)**. **There is NO deploy script yet for PrivacyCompliance, Icd10WhoFull, TemplatesBuiltInCopies or RxAsNeeded** — generate one with `dotnet ef migrations script <from> --idempotent` and have Jessie review/back up/apply. The API will fail against a hosted DB missing these columns.

---

## 11. Business rules & state machines

### Booking status (`BookingStatusMachine`, docs/STATE_MACHINES.md)
| From | Allowed next |
|---|---|
| Pending | ProofSubmitted, Confirmed, CheckedIn, Cancelled, Expired, Rescheduled, NoShow |
| ProofSubmitted | Confirmed, Cancelled, Expired |
| Confirmed | CheckedIn, Cancelled, Expired, Rescheduled, Completed, NoShow |
| CheckedIn | InProgress, OnHold, Cancelled, NoShow, Confirmed (undo check-in), Completed |
| InProgress | OnHold, Completed, Cancelled, NoShow |
| OnHold | CheckedIn, InProgress, Cancelled, NoShow, Completed |
| Cancelled / Completed / Expired / NoShow / Rescheduled | terminal |
Same-status = allowed no-op. Illegal ⇒ 400 `Illegal booking status transition: X → Y.`

### Payment status (`PaymentStatusMachine`)
Unpaid → Paid | Waived; Paid → Refunded; Waived/Refunded terminal. Staff waive requires doctor's `pf_decision = 'Waive'`.

### Consultation
- 1:1 with booking; upsert key = booking_id; patient/doctor must match the booking.
- Draft → Completed → Amended (repeatable). Completed/Amended never returns to Draft (409). Re-sending identical Completed = 200 no-op; different content claiming Completed = 409 (send Amended).
- Diagnoses replace-all; allowed on Completed only within **2 minutes** of `completed_at`; Amended diagnosis changes write a "Diagnoses amended" audit row.
- **Not sealed yet:** lab orders, vaccinations, follow-up, med cert after completion; the FE's multi-call save is not one transaction.
- Audit rows record *which fields changed*, not clinical text (PV-9).

### Fees (`ClinicFees.Compute`, recomputed on every consultation save)
- subtotal = `fee_senior_pwd` if discount (Senior/PWD) else `fee_follow_up` if FollowUp else `fee_consultation`; + `fee_med_cert` if med cert requested; `discount_amount = max(0, fee_consultation - subtotal)`. Writes `consultation_fee_snapshot`, `discount_amount`, `total_fee`, `amount_due` on the booking.
- **PF decision:** `'Charge'` (stores `pf_amount`), `'Waive'` (stores `pf_waive_reason`), or `null`. The FE defaults the editor to **Charge**. **Known issue:** "Decide at desk" and "never decided" are both stored as `null` — indistinguishable in the DB.

### Dates/time
All business dates via `ClinicClock` (Asia/Manila, fixed UTC+8). Tests freeze time via `ClinicClock.Time` (`ClockTests`).

### Templates: one list per doctor with built-in copies (Nov 20 "simplify")
- On a doctor's GET of soap/phrases/rx/cert/diagnosis templates, `BuiltInTemplateCopier.EnsureCopiesAsync` copies any built-in (Admin system rows `is_system_template=true` + catalog diagnoses/phrases in `BuiltInTemplateCatalog`) not yet recorded in `doctor_builtin_template_copies` into **her own rows** (`source_builtin_key` set), adopting an existing matching row instead of duplicating.
- She can edit/delete them like her own. A deleted copy stays deleted. `POST /api/templates/restore-built-ins?kind=` re-adds deleted built-ins and never overwrites.
- No System/Mine/Source/Specialty split is exposed to the doctor any more.

### ICD-10
- WHO ICD-10 **2019** international (used by PhilHealth), **not** US ICD-10-CM. 12,221 codes, files verbatim in `Data/Icd10Who2019/` (embedded resource), loaded/upserted on every startup, idempotent (skips if count = 12,221 and J45.9 sample OK).
- `Icd10UsToWho` remapped saved US codes: J45.909→J45.9, Z00.129→Z00.1, O24.419→O24.4, O99.210→O99.2, O14.90→O14.9, O13.9→O13, A90→A97.9, A09→A09.0, O80→O80.9, O82→O82.9.
- **Licence:** © WHO. **No licence obtained. Contact licensing@who.int before production/commercial use** (see `SOURCE.md`).

---

## 12. Tests

- Project: `tests/ClinicApp.Api.Tests` (xUnit, `WebApplicationFactory<Program>` via `ApiFactory.cs`/`Fixture.cs`). Run: `dotnet test` from repo root (does **not** need the running API; check `ApiFactory.cs` for which DB it uses before running — never point it at a work DB).
- **Last known result: 361 passed** (208 `[Fact]/[Theory]` attributes; theories expand).
- Files: AccessLog, AccountSecurity, BookingFlow (one row per UI button transition), BookingRules, Clock, ConsultationFlow, DoctorPerformance, DoctorScope, FileAccess, Icd10Who, Idor, InputValidation, PasswordAndLockout, PatientCode, PaymentValidation, PrivacyRights, ProductDecisions, ReviewRules, Role, RxAsNeeded, SettingsRules, StaffToday, StateMachine, TemplateRevamp, TemplatesBuiltIn (+ TestHelpers, GlobalUsings).

---

## 13. Known issues / limitations / security notes

- README "Next steps" and "not yet" wording are stale (FE is fully on this API).
- Missing hosted-DB SQL for the 4 newest migrations (§10).
- PF "Decide at desk" vs undecided both `null`.
- Duplicate diagnosis template **"A09.0"** may exist for the doctor (built-in copy vs remapped existing). Diagnoses tab is hidden in the FE, so low impact; commit fe60e93 tried to make the remapped row be adopted instead of duplicated.
- JWT role-change lag ≤ 60 min (deactivation is immediate).
- Child clinical rows not sealed after completion; multi-call save not atomic.
- Email (SMTP) not configured ⇒ forgot-password / verification emails likely don't send (**unverified**; checked only that `Email:*` keys are empty in appsettings).
- HSTS + HTTPS redirection outside Development (PV-6). Production rejects placeholder JWT secrets (PV-8).
- Secrets only in gitignored `.env` / GitHub Secrets. `deploy-credentials.md` is local & gitignored — never read or commit it.
- The old Supabase project still exists (the FE's parity/import scripts read it). If its keys were ever exposed, rotate them in Supabase.
- PHI files under `App_Data/uploads` (gitignored, never deleted by publish.sh).

---

## 14. FE ↔ BE contract (summary; mirrored in the FE handoff)

- **Base URL:** FE `NEXT_PUBLIC_API_URL` (code default `http://localhost:5000`; Jessie's `.env.local` points at **`http://localhost:5055`**). CORS must include the FE origin (dev: localhost/127.0.0.1 on 3000 & 3001).
- **Auth header:** `Authorization: Bearer <access JWT>`. The FE keeps tokens in httpOnly cookies `clinic_at` / `clinic_rt`; browser code gets the token from same-origin `GET /api/session/token`; the Next proxy (`src/proxy.ts`) silently calls `/api/auth/refresh-token`.
- **Shared secret:** FE `JWT_SECRET` == BE `Jwt__Secret`; FE `JWT_ISSUER`/`JWT_AUDIENCE` == `ClinicApp`.
- **Casing:** data snake_case; auth camelCase; enums exact names.
- **Key calls from the doctor consult page:** `GET /api/bookings/{id}`, `GET/PUT /api/consultations/by-booking/{bookingId}`, `PUT /api/consultations/{id}/diagnoses`, `PUT /api/vitals/by-booking/{id}`, `PUT /api/prescription-groups/by-booking/{id}`, `PUT /api/lab-orders/by-consultation/{id}`, `PUT /api/patient-vaccinations/by-consultation/{id}`, `PUT/DELETE /api/follow-ups/by-consultation/{id}`, `PUT/DELETE /api/medical-certificates/by-consultation/{id}`, `GET /api/icd10-codes`, template endpoints, `POST /api/templates/restore-built-ins`.
- **Session bootstrap:** `GET /api/patients/me` (Patient) or `GET /api/staff-accounts/me` (others).
- **Realtime:** SignalR `${API}/hubs/clinic?access_token=…`; events include `PaymentUpdated` and queue/booking status pushes.
- Wire-shape source of truth: `../Project02-fe/DOTNET_FRONTEND_CONTRACT.md`.

---

## 15. Working rules for AI agents (from Jessie — mandatory)

1. **Work directly on `main`.** No new branches, no worktrees. ("please dont complicate my life")
2. Changes must land in the **live running app** (the API Jessie runs on :5055; restart it only if Jessie agrees — BE changes need a rebuild/restart, unlike the FE's hot reload).
3. **Never commit or push unless Jessie says so** ("push", "push it").
4. Stage **explicit paths only** — never `git add .` / `git add -A`. No force-push, no `--amend`, no rebase, no `git stash`, no `checkout`/`reset` of his work.
5. **Never kill processes by name** (no `pkill dotnet`, `killall node`). Only a specific PID you started, if at all.
6. **Never run tests against `:3000` / `:5055`** (his live servers). Use separate ports/instances.
7. Never commit `.tmp-screens` or other test artefacts/screenshots.
8. Mark every edit: `// JJQR <date>` … `// JJQR - END` (existing code uses forms like `//JJQR - AI (Nov 20, 2026)` / `//JJQR Nov 20, 2026` … `//JJQR - END`; SQL uses `-- JJQR …`).
9. **Every change gets a test; never weaken or delete existing tests** to make them pass.
10. **Only the local Docker DB (`clinic-mssql`/`ClinicAppDb`) may be touched**, and back it up first to `~/Documents/Allyn/db-backups/` (existing backups there: `ClinicAppDb-pre-templates-revamp-20261119-185311.bak`, `ClinicAppDb-pre-simplify-20261120-145913.bak`). **Never touch any work/hosted DB.** Hosted changes = a reviewed SQL script Jessie runs himself.
11. **No new NuGet/npm packages without asking.**
12. Never read/print secret values (`.env`, `deploy-credentials.md`); refer to variable names.
13. UI priorities (FE): phone > tablet > desktop; in-app modals, never browser `alert/confirm`; **never make the doctor type what the app already knows.**
14. Code-writing workflow Jessie uses: Cursor CLI `agent -p --force --model <model>` writes code; the supervising agent verifies (tsc/tests) and reports in plain language.

---

## 16. Where things stand (Nov 20, 2026)

- `main @ fde7164` "Merge simplify/consult into main" (pushed). Recent: RxAsNeeded (6a2fee8), A09.0 adoption fix (fe60e93), templates built-in copies, WHO ICD-10 full catalog, privacy compliance.
- Working tree: clean except untracked `.DS_Store` files (and this new `AI-HANDOFF.md`).
- Open items: hosted-DB SQL for the 4 newest migrations; WHO licence; duplicate A09.0 diagnosis template cleanup (optional); publish.sh approval was pending with Jessie.
