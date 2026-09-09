# ClinicApp — .NET Backend (Project02-be)

ASP.NET Core Web API (`net10.0`) that will replace the Supabase backend of the
**Project02-fe** Next.js clinic app, built per:

- `../Project02-fe/DOTNET_BACKEND_PLAN.md` — what to build
- `../Project02-fe/DOTNET_FRONTEND_CONTRACT.md` — the exact wire format (JSON keys, enum
  strings, nested embed shapes). **Source of truth. Read it before changing any DTO.**

> **Provenance:** this project was seeded from `Project01-BE`, a working .NET port of the
> *byte-identical* `schema.sql` for the Angular version of the same clinic. Only the
> frontend-specific bits were re-pointed for Next.js (CORS/port `3000`, local SQL Server
> on macOS). The domain model, DbContext, migration, auth, and controllers came across
> as-is and still need reconciling against Project02's contract **§16 amendments**
> (staff-vitals `recorded_by_user_id`, `visit_type` tag, fee schedule, `medical_certificates`
> table, `lab_test_catalog`, doctor earnings view, split-shift hours, …) which are **not**
> in the base schema and therefore **not** in this backend yet.

## Solution layout

```
ClinicApp.slnx
docker-compose.yml          SQL Server 2022 for local dev (macOS: Colima + Rosetta)
src/
  ClinicApp.Api/             ASP.NET Core host — Program.cs, appsettings*.json, Controllers/
  ClinicApp.Domain/          Entity classes + enums (Enums/, Entities/), no EF dependency
  ClinicApp.Infrastructure/  EF Core DbContext, migrations, seed data, local file storage
  ClinicApp.Auth/            User/RefreshToken entities, JWT issuance, password hashing, DTOs
```

## Prerequisites (macOS)

- .NET SDK 10  (`brew install --cask dotnet-sdk`)
- `dotnet tool install --global dotnet-ef`
- Colima + Docker CLI  (`brew install colima docker docker-compose`)

There is **no native SQL Server for macOS** — it runs as a linux/amd64 container under
Colima + Rosetta. Windows deployment still targets `PHL8DPROG004\SQLEXPRESS` (see the
connection string in `src/ClinicApp.Api/appsettings.json`).

## First run

```bash
colima start --cpu 4 --memory 6 --disk 40 --vm-type=vz --vz-rosetta
docker compose up -d          # SQL Server 2022 Express on localhost:1433
```

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
cd src
dotnet ef database update --project ClinicApp.Infrastructure --startup-project ClinicApp.Api
```

```bash
cd src/ClinicApp.Api
dotnet run
```

- Dev connection string + `Jwt:Secret` live in `appsettings.Development.json` (already set
  for the container above; the secret there is dev-only).
- Listens on `http://localhost:5000` (see `Properties/launchSettings.json`).
- Point the frontend at it later — **not yet**; Project02-fe still runs against Supabase
  until the rewire phase (plan §11).

Stop the DB when done: `docker compose stop`  ·  stop the VM: `colima stop`

### Zero-infra alternative

Set `Database:Provider` = `Sqlite` and `DefaultConnection` = `Data Source=App_Data/clinicapp.dev.db`
in `appsettings.Development.json`. The app then builds the schema via `EnsureCreated()` (not
the migration) and creates the 4 views with SQLite syntax.

## CORS

`appsettings.json` → `Cors:AllowedOrigins` = `http://localhost:3000` (Next.js `next dev`).
The plan's original port-3000 assumption is correct for this frontend.

## Verified on this machine (2026-09-09, SQL Server 2022 in Colima)

- `dotnet build` — 0 warnings, 0 errors, all 4 projects
- `dotnet ef database update` — 39 tables + 4 views created in `ClinicAppDb`
- snake_case columns confirmed incl. `philhealth_number`, `hmo_card_number`, `ptr_number`
- seed rows: operating_hours 7 · icd10_codes 5 · medicines 15 · vital_field_templates 9 ·
  clinic_accepted_payment_methods 4 · clinic_settings 1
- `dotnet run` — API up on `:5000`; `GET /api/settings` returns snake_case JSON with
  `"default_payment_mode":"PayAtClinic"` (exact enum string, not int / not snaked);
  `POST /api/auth/login` returns `401 {"message":"Invalid email or password."}`

### Local fix applied on top of the Project01-BE seed

- `Icd10Code` now maps to table `icd10_codes` (the snake_case convention produced
  `icd10codes`; contract §4 / `schema.sql` say `icd10_codes`). Migration
  `RenameIcd10CodesTable`.

## Next steps

1. Reconcile entities/controllers against Project02's `DOTNET_FRONTEND_CONTRACT.md`
   §4–§6 (the base wire contract) — the Project01-BE README's "what's implemented"
   list is a starting inventory, re-verify each controller's routes/DTOs.
2. Apply the **§16 amendments** as their own migrations + DTOs (see contract §18
   checklist groups C, E, I, L).
3. Wire real JWT auth expectations to what `Project02-fe/src/lib/supabase/*` +
   `SessionProvider.tsx` read (plan §6, §11).
4. Frontend rewire is a **separate later phase** — do not touch `Project02-fe/src`.
