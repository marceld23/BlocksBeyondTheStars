#!/usr/bin/env bash
# Uploads a packaged WebGL ZIP to glitch.fun via the platform's deployments API — no CLI needed:
#   1. POST /titles/{id}/deployments/presigned-url  (Bearer deploy token) -> S3 upload URL
#   2. PUT the ZIP to that URL
#   3. POST /titles/{id}/deployments/confirm {file_path, version_string, entry_point}
# Glitch then unzips to its CDN (status: uploading -> processing -> ready). The ZIP must carry
# index.html at its ROOT. Used by the release pipeline (publish-glitch job) and runnable locally.
#
# Usage: GLITCH_DEPLOY_TOKEN=... ./scripts/upload-glitch-webgl.sh <zip> <version>
set -euo pipefail

ZIP="${1:?usage: upload-glitch-webgl.sh <zip> <version>}"
VERSION="${2:?usage: upload-glitch-webgl.sh <zip> <version>}"
TITLE_ID="${GLITCH_TITLE_ID:-80f5dc18-dc0f-45de-9a57-8599e08669ed}"
: "${GLITCH_DEPLOY_TOKEN:?GLITCH_DEPLOY_TOKEN is not set (create a deploy token on the glitch.fun tokens page)}"
API="https://api.glitch.fun/api"
AUTH="Authorization: Bearer $GLITCH_DEPLOY_TOKEN"

test -f "$ZIP" || { echo "ZIP not found: $ZIP" >&2; exit 1; }
unzip -l "$ZIP" | grep -q " index.html$" || { echo "index.html is not at the ZIP root — Glitch would reject the build." >&2; exit 1; }

echo "==> Requesting S3 upload slot for title $TITLE_ID ..."
presigned=$(curl -fsS -X POST "$API/titles/$TITLE_ID/deployments/presigned-url" -H "$AUTH" -H 'Accept: application/json')
# Tolerate both bare and data-wrapped response shapes.
upload_url=$(jq -r '.upload_url // .data.upload_url // empty' <<<"$presigned")
file_path=$(jq -r '.file_path // .data.file_path // empty' <<<"$presigned")
[ -n "$upload_url" ] && [ -n "$file_path" ] || { echo "Unexpected presigned-url response: $presigned" >&2; exit 1; }

echo "==> Uploading $(du -h "$ZIP" | cut -f1) to S3 ..."
curl -fsS -X PUT "$upload_url" -H 'Content-Type: application/zip' --data-binary @"$ZIP" -o /dev/null

echo "==> Confirming deployment (version $VERSION, entry index.html) ..."
confirm=$(curl -fsS -X POST "$API/titles/$TITLE_ID/deployments/confirm" -H "$AUTH" \
  -H 'Content-Type: application/json' -H 'Accept: application/json' \
  -d "{\"file_path\":\"$file_path\",\"version_string\":\"$VERSION\",\"entry_point\":\"index.html\"}")
echo "Glitch answered: $confirm"
echo "Deployment confirmed — Glitch is unzipping to its CDN (status: processing -> ready)."
