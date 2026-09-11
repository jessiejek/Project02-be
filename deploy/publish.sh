#!/usr/bin/env bash
# deploy/publish.sh — build + FTP-upload the ClinicApp.Api backend.
#
# Framework-dependent, win-x64 (the host has the .NET 10 ASP.NET Core runtime
# already; this is NOT self-contained — see Task 2/4 discussion, a
# self-contained build would waste a large share of the 1 GB disk quota).
#
# Required environment variables — set these in your OWN shell before running
# this script; NEVER paste them as literals here or anywhere in the repo:
#   FTP_HOST        e.g. ftp.sql5111.site4now.net (check the panel's FTP page)
#   FTP_USER
#   FTP_PASS
# Optional:
#   FTP_PORT        default 21
#   FTP_REMOTE_DIR  default "/" — the site's web root on the FTP server
#   BACKEND_URL     if set, curls "$BACKEND_URL/health" after upload to confirm it's live
#
# Example:
#   export FTP_HOST=ftp.example.net FTP_USER=myuser FTP_PASS='...' FTP_REMOTE_DIR=/site1
#   ./deploy/publish.sh
#
# Safe to re-run: `dotnet publish` always overwrites ./publish in place, and
# the upload is a per-file curl PUT with NO delete/mirror step — it can only
# ever ADD or overwrite files on the server. It will never touch
# App_Data/uploads, which holds real patient files that don't exist in the
# local build output and must never be wiped by a redeploy.

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/publish"
API_CSPROJ="$REPO_ROOT/src/ClinicApp.Api/ClinicApp.Api.csproj"

: "${FTP_HOST:?Set FTP_HOST in your shell, e.g. export FTP_HOST=ftp.yourhost.net}"
: "${FTP_USER:?Set FTP_USER in your shell}"
: "${FTP_PASS:?Set FTP_PASS in your shell}"
FTP_PORT="${FTP_PORT:-21}"
FTP_REMOTE_DIR="${FTP_REMOTE_DIR:-/}"
FTP_REMOTE_DIR="${FTP_REMOTE_DIR%/}"   # strip a trailing slash for clean joins

echo "==> Publishing (framework-dependent, win-x64, Release)..."
rm -rf "$PUBLISH_DIR"
dotnet publish "$API_CSPROJ" -c Release -r win-x64 --self-contained false -o "$PUBLISH_DIR"

# Never ship the local Development config to a shared host.
rm -f "$PUBLISH_DIR/appsettings.Development.json"

# `dotnet publish` regenerates web.config from scratch every time, which wipes
# any hand-edit made straight to a previous deploy's copy. This host's IIS
# defaults to Basic Authentication on the site, which 401s every request
# before it ever reaches the app — anonymousAuthentication must be forced on
# every single publish, not just the first one, or the next redeploy silently
# re-breaks the whole site (this happened once; don't let it happen again).
WEB_CONFIG="$PUBLISH_DIR/web.config"
if [[ -f "$WEB_CONFIG" ]] && ! grep -q "anonymousAuthentication" "$WEB_CONFIG"; then
  echo "==> Patching web.config: force anonymousAuthentication on (this host defaults to Basic Auth, which 401s every request)"
  python3 - "$WEB_CONFIG" <<'PYEOF'
import sys
path = sys.argv[1]
with open(path) as f:
    content = f.read()
marker = "</system.webServer>"
patch = """      <security>
        <authentication>
          <anonymousAuthentication enabled="true" />
          <basicAuthentication enabled="false" />
        </authentication>
      </security>
    </system.webServer>"""
content = content.replace(marker, patch, 1)
with open(path, "w") as f:
    f.write(content)
PYEOF
fi

echo "==> Published output size:"
SIZE_BYTES=$(find "$PUBLISH_DIR" -type f -exec stat -f%z {} + | awk '{s+=$1} END{print s+0}')
SIZE_HUMAN=$(du -sh "$PUBLISH_DIR" | cut -f1)
FILE_COUNT=$(find "$PUBLISH_DIR" -type f | wc -l | tr -d ' ')
PCT=$(awk -v b="$SIZE_BYTES" 'BEGIN{printf "%.2f", (b/1073741824)*100}')
echo "    $SIZE_HUMAN across $FILE_COUNT files  (~${PCT}% of your 1 GB site disk quota)"
echo "    NB: this does not include App_Data/uploads, which grows separately at runtime."

echo "==> Dropping app_offline.htm (releases IIS's file locks before the overwrite)..."
TMP_OFFLINE="$(mktemp)"
echo "<html><body>Deploying, back in a moment&hellip;</body></html>" > "$TMP_OFFLINE"
curl -sS --connect-timeout 15 --max-time 120 --ftp-create-dirs -T "$TMP_OFFLINE" \
  "ftp://${FTP_HOST}:${FTP_PORT}${FTP_REMOTE_DIR}/app_offline.htm" \
  --user "${FTP_USER}:${FTP_PASS}"
rm -f "$TMP_OFFLINE"

echo "==> Uploading to ftp://${FTP_HOST}:${FTP_PORT}${FTP_REMOTE_DIR}/ ..."
cd "$PUBLISH_DIR"
while IFS= read -r rel; do
  echo "    $rel"
  curl -sS --connect-timeout 15 --max-time 120 --ftp-create-dirs -T "$rel" \
    "ftp://${FTP_HOST}:${FTP_PORT}${FTP_REMOTE_DIR}/${rel}" \
    --user "${FTP_USER}:${FTP_PASS}"
done < <(find . -type f | sed 's|^\./||')
cd "$REPO_ROOT"

echo "==> Removing app_offline.htm (bringing the site back)..."
curl -sS --connect-timeout 15 --max-time 30 --user "${FTP_USER}:${FTP_PASS}" \
  -Q "DELE ${FTP_REMOTE_DIR}/app_offline.htm" \
  "ftp://${FTP_HOST}:${FTP_PORT}/" \
  || echo "    (cleanup failed — delete app_offline.htm by hand if the site still shows 'offline')"

echo "==> Deploy complete."
if [[ -n "${BACKEND_URL:-}" ]]; then
  echo "==> Verifying ${BACKEND_URL}/health ..."
  curl -sS -w "\nHTTP %{http_code}\n" "${BACKEND_URL%/}/health" || true
else
  echo "    Set BACKEND_URL to auto-verify next time, or check manually:"
  echo "    curl -s https://<your-backend-url>/health"
fi
