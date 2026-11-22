#!/usr/bin/env bash
# deploy/migrate-db.sh — bring the LIVE database up to date, automatically, BEFORE the new code is uploaded.
#
#JJQR - AI (Nov 22, 2026) - owner: "one push changes everything". The deploy used to copy files only; every database change was a hand-run SQL script.
# What it does:
#   1. asks the live database which migration it is on (the newest row of __EFMigrationsHistory)
#   2. builds ONE idempotent SQL script with only the migrations after that one (dotnet ef migrations script <last> --idempotent)
#   3. if there is nothing new: says so and stops (the normal case)
#   4. REFUSES a script that drops tables or columns unless ALLOW_DESTRUCTIVE=true (a person must decide that on purpose)
#   5. runs it with sqlcmd; any error stops the whole deploy, so the site keeps running the OLD code on the OLD database
# Needs: sqlcmd (go-sqlcmd), dotnet + dotnet-ef. The password comes from the DB_PASS secret and is passed through SQLCMDPASSWORD (never on a command line).
# Test without a database: MIGRATE_DRY_RUN=1 MIGRATE_FROM=<migration id> bash deploy/migrate-db.sh   (prints what WOULD run)
set -euo pipefail

DB_SERVER="${DB_SERVER:-sql5111.site4now.net}"
DB_NAME="${DB_NAME:-db_ace1d6_gavinodb}"
DB_USER="${DB_USER:-db_ace1d6_gavinodb_admin}"
OUT="$(mktemp -t migrate-db.XXXXXX).sql"

if [[ "${MIGRATE_DRY_RUN:-}" != "1" && -z "${DB_PASS:-}" ]]; then
  echo "::warning::DB_PASS secret is not set: skipping the automatic database update. Add it in GitHub > Settings > Secrets and variables > Actions."
  exit 0
fi
export SQLCMDPASSWORD="${DB_PASS:-}"

if [[ -n "${MIGRATE_FROM:-}" ]]; then
  LAST="$MIGRATE_FROM"
else
  echo "==> Asking ${DB_SERVER}/${DB_NAME} which migration it is on..."
  LAST="$(sqlcmd -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -C -b -h -1 -W \
          -Q "SET NOCOUNT ON; SELECT TOP 1 migration_id FROM __EFMigrationsHistory ORDER BY migration_id DESC" | tr -d '\r' | head -1)"
fi
if [[ -z "$LAST" ]]; then echo "ERROR: could not read the database's current migration." >&2; exit 1; fi
echo "    database is on: $LAST"

echo "==> Building the update script (only what is missing)..."
dotnet tool restore >/dev/null 2>&1 || true   # the pinned dotnet-ef from dotnet-tools.json
dotnet ef migrations script "$LAST" --idempotent \
  --project src/ClinicApp.Infrastructure --startup-project src/ClinicApp.Api --output "$OUT" >/dev/null
sed -i.bak '1s/^\xEF\xBB\xBF//' "$OUT" && rm -f "$OUT.bak"   # byte-order mark off

if ! grep -q "INSERT INTO \[__EFMigrationsHistory\]" "$OUT"; then
  echo "==> Database is already up to date. Nothing to do."
  exit 0
fi
echo "    new migrations to apply:"
grep -o "N'20[0-9]\{12\}_[A-Za-z0-9]*'" "$OUT" | sort -u | sed "s/^/      /"

if grep -Eqi "DROP[[:space:]]+(TABLE|COLUMN|SCHEMA|DATABASE)" "$OUT" && [[ "${ALLOW_DESTRUCTIVE:-}" != "true" ]]; then
  echo "ERROR: this update deletes tables or columns. It will NOT run by itself." >&2
  echo "       Take a backup, read deploy script, then re-run the workflow with ALLOW_DESTRUCTIVE=true." >&2
  exit 1
fi

if [[ "${MIGRATE_DRY_RUN:-}" == "1" ]]; then
  echo "==> DRY RUN: would run $(wc -l < "$OUT" | tr -d ' ') lines of SQL. Nothing was changed."
  exit 0
fi

echo "==> Applying to ${DB_NAME}..."
sqlcmd -S "$DB_SERVER" -d "$DB_NAME" -U "$DB_USER" -C -I -b -i "$OUT"
echo "==> Database updated."
#JJQR - END
