# Study: a .NET desktop app with its own local database, syncing to the cloud

**Question:** can the clinic keep working on a day with **no internet** — the doctor logs in, registers a patient, does the consultation — and have everything sync to the cloud when the internet returns?

**Short answer:** yes, it is feasible, and this codebase gives a real head start (the whole API already runs on SQLite). It is a **multi-month project**, and the hard part is not the desktop window — it is **sync**: IDs, queue numbers, conflicts, money and security. Read section 9 first: a cheaper fix may remove most of the need.

*Everything marked "(verified)" was checked in this repository. Effort figures are rough planning ranges for one experienced developer, not quotes.*

---

## 1. What "works offline" must mean (scope)

| Capability | Needed offline? | Why it is easy / hard |
|---|---|---|
| Doctor login | **Yes** | Needs local accounts + local tokens (section 5.3) |
| Register a patient | **Yes** | Patient code (`MF-000123`) is issued by the server (hard) |
| Walk-in check-in / queue | Yes, if the front desk also works offline | Queue numbers `R-001` / `S-001` are server-counted per day and lane (hard) |
| Consultation: notes, vitals, Rx, labs, follow-up, certificate | **Yes** | Mostly one-writer data (the doctor), so easy to sync |
| Print Rx / lab forms / queue slip | **Yes** | Already generated in the app; printing is local (easy) |
| Fee decision (charge / waive) | Yes | Just data |
| **Recording a payment** | Optional | Money: needs exact rules + receipt numbers (hard, section 5.5) |
| Online bookings by patients | **No** | Patients book on the cloud; they arrive when the node syncs |
| Live updates (SignalR) | Local only | The local node runs its own hub on the LAN |

**Recommended scope for version 1:** doctor login, patient registration, consultation, printing. Payments and the front desk come in a later phase.

## 2. What exists today (verified)

- **Frontend:** Next.js 16 (React 19) with four portals. It uses **server features**: `proxy.ts`, route handlers `/api/session/login|token|register`, and `httpOnly` cookies. It cannot simply be opened from disk — it needs a running Next server.
- **Backend:** .NET 10, about 30 controllers, EF Core, 53 DbSets, SQL Server in the cloud (FTP-deployed), SignalR hub `/hubs/clinic`.
- **The API already runs on SQLite (verified):** the test suite (419 tests) runs the entire API on SQLite (`Database__Provider=Sqlite`), and `Program.cs` has an `EnsureCreated()` + `SqliteDemoViews` path. This is the biggest head start: a local node can host **the same API** unchanged.
- **IDs:** primary keys are GUIDs (good — no collisions between machines). The patient POST already accepts a client-supplied `PatientId` (verified). But bookings, payments and most other POST endpoints create their own `Guid.NewGuid()` server-side (verified in `QueueController` / `BookingsController`) — they need a "client supplies the id" option.
- **Change tracking:** about 22 entities implement `IHasUpdatedAt` (verified). There is **no row-version / ETag / idempotency key** anywhere (verified).
- **Deletes:** 23 delete operations in the API (verified), many of the "replace-all" kind (diagnoses, lab orders, Rx lines). Deletes cannot sync without tombstones.
- **Sessions:** access token 1 hour, refresh 7 days (verified earlier). Tokens are signed with a cloud secret.
- **Offline today:** none. The service worker is `NetworkOnly`, there is no local storage queue, and a failed save just says "Not saved — your entries are still here".

## 3. Architecture options

### Option A — Offline-tolerant web app (no desktop app)
Browser storage (IndexedDB) keeps an outbox of writes and replays them when online.
- **Gives:** survives short drops while the page is open; no installation; one code base.
- **Does not give:** a cold start offline (cannot log in, no pages served), a shared view between computers, or a real local database. Printing works only if the page was already loaded.
- **Effort:** about 3–5 weeks (idempotent endpoints, outbox, banners). **Good stop-gap, not the answer to "no internet all day".**

### Option B — Desktop "clinic node": local API + SQLite + the existing UI + sync *(recommended if offline days are real)*
A Windows app that bundles:
1. the **existing .NET API** (same code, SQLite provider), started in-process;
2. the **existing Next.js UI** (built as a standalone Node server, started as a child process, shown in an embedded browser window — WebView2);
3. a **sync service** talking to the cloud API.
- **Gives:** real offline: log in, register, consult, print — with no internet. The same UI the doctor already knows. One code base for logic.
- **Costs:** you ship and update **two runtimes** (.NET + Node), an installer, a local database that must be encrypted and backed up, and a sync engine.
- **Effort:** about 5–8 months total for one developer (phases in section 8).

### Option C — Rewrite the UI natively in .NET (WPF / MAUI / Blazor Hybrid / Avalonia)
- **Gives:** a single .NET stack, no Node.
- **Costs:** every screen rebuilt (doctor + staff portals alone are very large: the consultation page is about 4,000 lines), two UIs to maintain forever, and the loss of the web app's tested behavior.
- **Effort:** 9–14+ months. **Not recommended** unless there is a hard reason to drop web technology.

### Shell choices for Option B
| Shell | Pros | Cons |
|---|---|---|
| **.NET host (WinForms/WPF) + WebView2** | It *is* a .NET desktop app; small; uses the Edge runtime already on Windows 10/11 | Windows only; must spawn the Node server |
| Electron + .NET API as a sidecar | Mature, silent printing, auto-update tooling, Node already included | ~100 MB+, not .NET |
| Tauri + sidecars | Very small | Rust toolchain; same sidecar work |

For "a .NET desktop app", the first row fits best. Silent printing and thermal-printer control are possible from a .NET host.

## 4. One computer or the whole clinic?

This is the most important design decision.

- **Scope 1 — doctor's PC only.** The node is standalone. The doctor works offline; the front desk (still in the browser) cannot reach it. During an outage the doctor cannot see who the front desk checked in, unless the desk has paper slips. Simple, and it matches "the doctor can still login, register patients, consult".
- **Scope 2 — clinic LAN node.** One PC hosts the node; every other computer opens it over the clinic wifi/LAN (no internet needed). The front desk and the doctor share one live queue. This is what a "no internet day" really needs. It adds: a fixed local address, firewall setup, one machine that must stay on, and queue-number ownership (5.2).

**Recommendation:** build Scope 1 first, designed so it can grow into Scope 2.

## 5. The hard problems (and the proposed answers)

### 5.1 Identity and idempotency
- GUIDs prevent collisions. Make **every create endpoint accept a client-supplied id** (patients already do) and treat a repeat of the same id as "already done".
- Add an **operation id** per queued write so a retried upload cannot double-apply.

### 5.2 Server-issued numbers
| Number | Today | Offline answer |
|---|---|---|
| Queue number `R-001` / `S-001` | Counter per day **and lane** in the cloud (`QueueCounter`) | The node is the **only issuer** while the clinic is open. Cloud online bookings made that day are numbered on sync, after the node's numbers (rule to be agreed with the clinic). |
| Patient code `MF-000123` | Cloud allocator, monotonic | **Pre-reserve a block** (for example 100 codes per node) while online; use them offline. If the block runs out, issue a temporary code `MF-T-0001` and replace it on sync (patient cards would need reprinting — keep the block big). |
| Receipt / OR number | Frontend derives it from the booking id | Already offline-safe (derived locally); confirm the clinic's legal OR-numbering rules before offline payments. |

### 5.3 Login offline
- Sync **staff/doctor accounts and password hashes** down to the node. The node issues **its own** short tokens with **its own** secret (cloud secrets never leave the cloud).
- Limits: an account deactivated in the cloud stays able to log in on the node until the next sync. Mitigate with a **maximum offline period** (for example 7 days, then the node locks until it syncs) and a visible "last synced" time.
- Password changes and lockouts follow the same sync delay.

### 5.4 Conflicts
Design the sync as **per-aggregate, with an owner**, not as generic last-write-wins:

| Data | Who writes | Rule |
|---|---|---|
| Consultation (notes, vitals, Rx, labs, follow-up, certificate) | The doctor, on the node | Node wins; a **finished** consultation is immutable (existing rule), amendments are append-only |
| Patient demographics | Front desk / doctor / patient (online) | Field-level merge, later `UpdatedAt` wins; show a conflict list for the rest |
| Booking status | Staff / doctor / patient | The **state machine decides**: keep the furthest-progressed valid state; reject impossible transitions into a review list |
| Payment | Front desk only | One writer per payment; never merged — the node's recorded payment wins, cloud edits to the same payment are flagged |
| Audit logs, vitals readings, access logs | Append-only | Plain union |
| Settings, templates, medicines | Admin (cloud) | Cloud wins; pulled down read-only |

Needed in the schema: a **row version** and **tombstones** (soft deletes) for the data that syncs; the existing "replace-all" endpoints must sync as whole aggregates.

### 5.5 Money offline
Money is the riskiest data. Rules that keep it safe:
- Only the front-desk node records payments; one writer per payment.
- The **exact-amount rule** (already enforced) means received always equals due, so reports stay correct after sync.
- A payment recorded offline is marked **pending sync** on screen and on any printed receipt note.
- Do not allow refunds offline (admin online only).

### 5.6 What syncs down, and how much
- **Always:** accounts, doctors, clinic settings, templates, medicines, lab catalog, vital fields.
- **Patients:** all, or only those seen in the last N months — a **Data Privacy Act (RA 10173)** data-minimisation decision for the clinic.
- **ICD-10:** nothing to sync; the WHO catalog is embedded in the app.
- **Files** (uploaded patient documents): kept on the node, uploaded when online.

### 5.7 Schema versions
The desktop node and the cloud must run **compatible schemas**. Every release needs: node auto-update, a version handshake before syncing, and migrations that run on the local SQLite database safely (with a backup first). A node older than the cloud must update before it can sync.

### 5.8 Security and privacy (patient data on a local PC)
- Encrypt the local database (SQLCipher or an equivalent) and store the key with Windows DPAPI; require full-disk encryption (BitLocker).
- Local accounts and tokens as in 5.3; automatic lock after idle; no data in plain exports.
- A lost or stolen computer is a **data breach risk**: plan remote revocation (the node refuses to sync after revocation) and a written incident procedure. Confirm obligations with a Data Privacy Act adviser.
- Backups of the local database, encrypted, to an external drive.

## 6. What the code needs (gap list)

| Area | Change | Size |
|---|---|---|
| Host the API in a desktop process (Kestrel on localhost, SQLite) | Reuse existing SQLite mode; make it a proper local provider, apply migrations safely | S–M |
| Sync columns | `row_version`, `deleted_at` (tombstones), `sync_state`, node id on synced tables | M |
| Outbox / change log | Local table of operations; written in the same transaction as the change | M |
| Sync API (cloud) | `POST /api/sync/push` (batch, idempotent) and `GET /api/sync/pull?since=` | M–L |
| Sync client (node) | Push/pull loop, retry/backoff, conflict list, status UI | L |
| Client-supplied ids on all create endpoints | Bookings, payments, consultations children, etc. | M |
| Number allocation | Patient-code blocks, queue ownership, reconciliation rules | M |
| Local auth | Account sync, node tokens, max offline period | M |
| Frontend in "local mode" | `NEXT_PUBLIC_API_URL` to localhost, sync status banner, "pending sync" markers, no cloud-only features | M |
| Packaging | Installer, WebView2 check, Node standalone bundle, service/auto-start, auto-update | M–L |
| Security | Encryption, DPAPI, idle lock, revocation | M |
| Tests | Sync round-trip, conflict, crash-mid-sync, clock skew, long-offline | L |

(S = days, M = 1–3 weeks, L = 3+ weeks.)

## 7. Risks

1. **Data duplication or loss during sync** — the main risk; mitigated by idempotent operations, an operation log, and extensive tests of "pull the plug at every step".
2. **Two offline nodes editing the same record** (only if more than one node exists) — avoid by having **one node per clinic** (Scope 1/2).
3. **Clock errors** on a PC that has been off the network — use server-issued time at each sync and store both local and sync times.
4. **Support burden** — a desktop install is a second product: updates, crashes, Windows versions, printer drivers, backups.
5. **Long offline periods** — the longer the gap, the bigger the merge; enforce the maximum offline period.
6. **Security** — see 5.8; patient data now lives on a laptop.
7. **Two runtimes to ship and patch** (.NET + Node) in Option B.

## 8. Phased plan (Option B, Scope 1 first)

| Phase | Deliverable | Rough time |
|---|---|---|
| 0. Spike | Node app boots: local API + SQLite + the existing UI in a WebView2 window; doctor can log in with a locally seeded account; one consultation saved locally. **Goal: prove the packaging.** | 2–3 weeks |
| 1. Doctor offline | Local accounts, register patient, consultation, printing, encryption at rest, installer | 6–8 weeks |
| 2. Sync engine | Sync columns, outbox, push/pull API, conflict rules for the doctor's data, status UI, tests | 6–10 weeks |
| 3. Whole-clinic (LAN) | Front desk + queue on the LAN node, queue-number ownership, payments with pending-sync rules | 6–8 weeks |
| 4. Hardening | Auto-update, backups, revocation, load of long-offline merges, field pilot | 4–6 weeks |

**Total: about 5–8 months for one developer** (less with two, but sync design is hard to parallelise). A **go / no-go review after Phase 0** is built in.

## 9. A cheaper alternative to consider first

If the real problem is **the internet dropping now and then**, the cheapest "offline" is a **second connection**: a 4G/5G failover router (or a mobile hotspot on a second carrier). The clinic stays online through most outages for a one-time device cost plus a data plan. It needs **no software work** and no sync risk.

Build the desktop node only if:
- outages last **hours to days** and happen **often**, or
- the clinic location has **unreliable connections even with backup**, or
- there is a requirement to run with **no cloud dependency**.

## 10. Recommendation

1. **Now:** get a failover connection (section 9) and a printed **downtime procedure** (paper queue slip, Rx pad, paper consent) — both cost almost nothing.
2. **Next (3–5 weeks, optional):** Option A's offline-safe drafts, so a dropped connection never loses a consultation in progress.
3. **If outages are frequent:** approve **Phase 0** of Option B (2–3 weeks) as a paid spike, then decide on Phases 1–4 with real evidence about packaging, performance and the clinic's tolerance for the support burden.

## 11. Questions the owner must answer

1. How often does the internet fail, and for how long (last 12 months)?
2. How many computers must keep working during an outage — just the doctor's, or the front desk too?
3. Windows only? Which Windows versions and which printers (including any thermal slip printer)?
4. Who will maintain installs, updates and backups on the clinic computers?
5. Which patient data may be stored on the local PC (all patients, or recent only)? (Data Privacy Act decision.)
6. How should a patient's online booking be numbered when it arrives during an outage?
7. May payments be recorded offline, and what must the receipt say?
8. What is the maximum time a computer may stay offline before it locks?

*Prepared from the repository state of 21 Nov 2026. Sources are the code and tests in this repository, plus earlier findings (session lengths, queue-number design, payment rules).*
