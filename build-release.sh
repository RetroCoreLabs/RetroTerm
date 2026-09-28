#!/usr/bin/env bash
# RetroTerm Build & Publish Script (Linux)
# Builds both client and test server to publish/ folder.
# Mirrors build-release.bat for Linux.

set -euo pipefail

echo "========================================================================"
echo "  RetroTerm Release Build (linux-x64)"
echo "========================================================================"
echo

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

PUBLISH_DIR="./publish"
CLIENT_PROJ="./src/RetroTerm.Desktop/RetroTerm.Desktop.csproj"
SERVER_PROJ="./tests/RetroTerm.TestServer/RetroTerm.TestServer.csproj"
RID="${RID:-linux-x64}"
CONFIG="${CONFIG:-Release}"

# Kill any running instances silently
pkill -f RetroTerm.TestServer >/dev/null 2>&1 || true
pkill -f RetroTerm.Desktop    >/dev/null 2>&1 || true

# Clean publish dir so stale files from a previous RID (e.g. Windows .dlls) don't linger.
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

echo "[1/2] Building RetroTerm.Desktop..."
echo "========================================================================"
dotnet publish "$CLIENT_PROJ" -r "$RID" -c "$CONFIG" --self-contained true -o "$PUBLISH_DIR"

# Desktop is PublishSingleFile with IncludeNativeLibrariesForSelfExtract, so the natives
# (libSkiaSharp, libHarfBuzzSharp, libAvaloniaNative) live inside the executable and are
# extracted to ~/.net/... on first run. Just verify the launcher itself exists.
if [ ! -x "$PUBLISH_DIR/RetroTerm.Desktop" ]; then
    echo "ERROR: RetroTerm.Desktop launcher missing or not executable!"
    exit 1
fi
echo
echo "[OK] RetroTerm.Desktop published successfully"
echo

echo "[2/2] Building RetroTerm.TestServer..."
echo "========================================================================"
dotnet publish "$SERVER_PROJ" -r "$RID" -c "$CONFIG" -p:PublishSingleFile=true --self-contained true -o "$PUBLISH_DIR"
echo
echo "[OK] RetroTerm.TestServer published successfully"
echo

chmod +x "$PUBLISH_DIR/RetroTerm.Desktop"    2>/dev/null || true
chmod +x "$PUBLISH_DIR/RetroTerm.TestServer" 2>/dev/null || true

echo "========================================================================"
echo "  Build Complete!"
echo "========================================================================"
echo
echo "Published to: $PUBLISH_DIR"
echo
echo "Files created:"
echo "  - RetroTerm.Desktop"
echo "  - RetroTerm.TestServer"
echo
echo "To test:"
echo "  1. Terminal 1: cd publish && ./RetroTerm.TestServer 2323"
echo "  2. Terminal 2: cd publish && ./RetroTerm.Desktop"
echo "  3. Connect to localhost:2323"
echo
echo "========================================================================"
