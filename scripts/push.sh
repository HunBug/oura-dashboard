#!/usr/bin/env bash
# push.sh — Build, push, and deploy oura-dashboard
#
# Usage:
#   LARA_API_KEY=<key> ./scripts/push.sh [flags]
#
# Flags:
#   --force        Overwrite existing image tag in registry (skip version check)
#   --skip-tests   Skip dotnet test (build + push only)
#   --dry-run      Print what would happen without executing anything
#   --help         Show this message
#
# Configurable env vars (all have defaults):
#   REGISTRY            Docker registry host:port        (default: lara:5000)
#   LARA_URL            lara API base URL                (default: http://lara:1234)
#   LARA_DEPLOY_PATH    lara deploy API path             (default: /api/deploy/oura-dashboard)
#   WEB_URL             URL to health-check after deploy (default: http://lara:8085)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# ─── Defaults ─────────────────────────────────────────────────────────────────
REGISTRY="${REGISTRY:-lara:5000}"
IMAGE_NAME="oura-dashboard"
LARA_URL="${LARA_URL:-http://lara:1234}"
LARA_DEPLOY_PATH="${LARA_DEPLOY_PATH:-/api/deploy/oura-dashboard}"
WEB_URL="${WEB_URL:-http://lara:8085}"
VERSION_FILE="${REPO_ROOT}/Directory.Build.props"

# ─── Colours ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

info()  { echo -e "${GREEN}  ✓${NC} $*"; }
step()  { echo -e "\n${BOLD}${CYAN}──${NC} ${BOLD}$*${NC}"; }
warn()  { echo -e "${YELLOW}  ⚠${NC} $*"; }
abort() { echo -e "\n${RED}  ✗ ERROR:${NC} $*" >&2; exit 1; }

# ─── Flag parsing ──────────────────────────────────────────────────────────────
FORCE=false
SKIP_TESTS=false
DRY_RUN=false

for arg in "$@"; do
  case $arg in
    --force)       FORCE=true ;;
    --skip-tests)  SKIP_TESTS=true ;;
    --dry-run)     DRY_RUN=true ;;
    --help|-h)
      sed -n '2,16p' "$0" | sed 's/^# *//'
      exit 0
      ;;
    *)
      warn "Unknown argument: $arg (ignored)"
      ;;
  esac
done

# Wraps a command: runs it normally, or just prints it in dry-run mode.
run() {
  if [[ "$DRY_RUN" == true ]]; then
    echo -e "${YELLOW}  [dry-run]${NC} $*"
  else
    "$@"
  fi
}

# ─── Banner ───────────────────────────────────────────────────────────────────
echo -e "\n${BOLD}oura-dashboard deploy script${NC}"
[[ "$DRY_RUN" == true ]] && echo -e "${YELLOW}  DRY-RUN mode — nothing will be executed${NC}"
[[ "$FORCE" == true ]]   && warn "--force: existing image tag will be overwritten"
[[ "$SKIP_TESTS" == true ]] && warn "--skip-tests: test run is skipped"

# ─── 1. Required env vars ─────────────────────────────────────────────────────
step "Pre-flight checks"

[[ -z "${LARA_API_KEY:-}" ]] && abort \
  "LARA_API_KEY is not set.\n  Usage: LARA_API_KEY=<key> $0 [--force] [--skip-tests]"

# ─── 2. Read version from Directory.Build.props ───────────────────────────────
VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$VERSION_FILE" 2>/dev/null || true)
[[ -z "$VERSION" ]] && abort \
  "No <Version> element found in Directory.Build.props.\n  Add <Version>x.y.z</Version> inside a <PropertyGroup>."

FULL_IMAGE="${REGISTRY}/${IMAGE_NAME}"
VERSION_TAG="${FULL_IMAGE}:${VERSION}"
LATEST_TAG="${FULL_IMAGE}:latest"

echo ""
echo "  Version  : ${BOLD}${VERSION}${NC}"
echo "  Image    : ${VERSION_TAG}"
echo "  Registry : ${REGISTRY}"
echo "  Lara API : ${LARA_URL}${LARA_DEPLOY_PATH}"
echo "  Web URL  : ${WEB_URL}"

# ─── 3. Docker daemon ─────────────────────────────────────────────────────────
docker info > /dev/null 2>&1 || abort "Docker daemon is not running."
info "Docker daemon is running"

# ─── 4. Registry reachable? ───────────────────────────────────────────────────
if [[ "$DRY_RUN" == false ]]; then
  REGISTRY_HTTP=$(curl -sk -o /dev/null -w "%{http_code}" --max-time 5 \
    "https://${REGISTRY}/v2/" 2>/dev/null || echo "000")
  # 200 = anonymous OK, 401 = auth required (still reachable)
  [[ "$REGISTRY_HTTP" == "200" || "$REGISTRY_HTTP" == "401" ]] || \
    abort "Registry https://${REGISTRY}/v2/ returned HTTP ${REGISTRY_HTTP}.\n  Is the registry running on lara?"
  info "Registry reachable (HTTP ${REGISTRY_HTTP})"
else
  warn "[dry-run] Skipping registry reachability check"
fi

# ─── 5. Version tag already exists? ──────────────────────────────────────────
if [[ "$DRY_RUN" == false ]]; then
  MANIFEST_HTTP=$(curl -sk -o /dev/null -w "%{http_code}" --max-time 5 \
    "https://${REGISTRY}/v2/${IMAGE_NAME}/manifests/${VERSION}" 2>/dev/null || echo "000")

  if [[ "$MANIFEST_HTTP" == "200" ]]; then
    if [[ "$FORCE" == true ]]; then
      warn "Tag ${VERSION} already in registry — overwriting (--force)"
    else
      abort "Image ${VERSION_TAG} already exists in the registry.\n  Bump <Version> in Directory.Build.props, or re-run with --force to overwrite."
    fi
  else
    info "Version ${VERSION} not yet in registry — new tag will be created"
  fi
fi

# ─── 6. Lara server reachable? ────────────────────────────────────────────────
if [[ "$DRY_RUN" == false ]]; then
  LARA_HTTP=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 \
    "${LARA_URL}/health" 2>/dev/null || echo "000")
  if [[ "$LARA_HTTP" == "200" ]]; then
    info "Lara server reachable"
  else
    warn "Lara /health returned ${LARA_HTTP} — deploy trigger may still succeed, but server may be busy or slow"
  fi
fi

# ─── 7. dotnet build ──────────────────────────────────────────────────────────
step "dotnet build (Release)"
cd "$REPO_ROOT"

if [[ "$DRY_RUN" == false ]]; then
  BUILD_OUTPUT=$(dotnet build --configuration Release 2>&1)
  echo "$BUILD_OUTPUT" | grep -E "^Build |error" | tail -5 || true
  echo "$BUILD_OUTPUT" | grep -q "Build succeeded" || abort "dotnet build failed.\n$(echo "$BUILD_OUTPUT" | grep -i error | head -10)"
  info "Build succeeded"
else
  run dotnet build --configuration Release
fi

# ─── 8. dotnet test ───────────────────────────────────────────────────────────
if [[ "$SKIP_TESTS" == true ]]; then
  warn "Skipping tests (--skip-tests flag)"
else
  step "dotnet test"
  if [[ "$DRY_RUN" == false ]]; then
    TEST_OUTPUT=$(dotnet test --configuration Release --no-build 2>&1)
    echo "$TEST_OUTPUT" | grep -E "passed|failed|Passed|Failed" | tail -5 || true
    echo "$TEST_OUTPUT" | grep -qiE "failed|Error" && abort "Tests failed.\n$(echo "$TEST_OUTPUT" | grep -iE "failed|Error" | head -10)"
    info "All tests passed"
  else
    run dotnet test --configuration Release --no-build
  fi
fi

# ─── 9. Docker build ──────────────────────────────────────────────────────────
step "docker build"
run docker build \
  --file docker/oura-dashboard/Dockerfile \
  --build-arg VERSION="${VERSION}" \
  --tag "${VERSION_TAG}" \
  --tag "${LATEST_TAG}" \
  .
[[ "$DRY_RUN" == false ]] && info "Image built: ${VERSION_TAG}"

# ─── 10. Docker push ──────────────────────────────────────────────────────────
step "docker push"
run docker push "${VERSION_TAG}"
run docker push "${LATEST_TAG}"
[[ "$DRY_RUN" == false ]] && info "Pushed ${VERSION_TAG} and :latest"

# ─── 11. Trigger deploy via lara HTTP API ────────────────────────────────────
step "Deploy trigger (lara API)"
if [[ "$DRY_RUN" == false ]]; then
  DEPLOY_RESP=$(curl -s -w "\n%{http_code}" \
    --max-time 30 \
    -X POST "${LARA_URL}${LARA_DEPLOY_PATH}" \
    -H "Authorization: Bearer ${LARA_API_KEY}" \
    -H "Content-Type: application/json" \
    -d "{\"image\": \"${VERSION_TAG}\"}" 2>/dev/null)

  DEPLOY_HTTP=$(echo "$DEPLOY_RESP" | tail -1)
  DEPLOY_BODY=$(echo "$DEPLOY_RESP" | head -n -1)

  case "$DEPLOY_HTTP" in
    200|202|204)
      info "Deploy triggered (HTTP ${DEPLOY_HTTP})"
      [[ -n "$DEPLOY_BODY" ]] && echo "    ${DEPLOY_BODY}"
      ;;
    *)
      abort "Deploy API returned HTTP ${DEPLOY_HTTP}.\n  Body: ${DEPLOY_BODY}\n  Check LARA_URL (${LARA_URL}) and LARA_DEPLOY_PATH (${LARA_DEPLOY_PATH})."
      ;;
  esac
else
  run curl -X POST "${LARA_URL}${LARA_DEPLOY_PATH}" \
    -H "Authorization: Bearer \$LARA_API_KEY" \
    -d "{\"image\": \"${VERSION_TAG}\"}"
fi

# ─── 12. Health check ─────────────────────────────────────────────────────────
step "Health check (waiting for web UI)"
if [[ "$DRY_RUN" == false ]]; then
  MAX_ATTEMPTS=24  # 2 min max at 5 s intervals
  printf "  Polling ${WEB_URL} "
  for i in $(seq 1 $MAX_ATTEMPTS); do
    HTTP=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "${WEB_URL}/" 2>/dev/null || echo "000")
    if [[ "$HTTP" == "200" ]]; then
      echo ""
      info "App is healthy (attempt ${i}, ~$((i * 5))s)"
      break
    fi
    if [[ $i -eq $MAX_ATTEMPTS ]]; then
      echo ""
      warn "App did not return 200 after $((MAX_ATTEMPTS * 5))s (last HTTP: ${HTTP})"
      warn "Check logs on lara: docker logs oura-dashboard-web-1"
    fi
    printf "."
    sleep 5
  done
fi

# ─── Done ─────────────────────────────────────────────────────────────────────
echo -e "\n${BOLD}${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${BOLD}${GREEN}  Deployed oura-dashboard ${VERSION}${NC}"
echo -e "${BOLD}${GREEN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo "  Image : ${VERSION_TAG}"
echo "  Web   : ${WEB_URL}"
echo ""
