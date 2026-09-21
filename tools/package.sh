#!/usr/bin/env bash
# ==============================================================================
# Angband3D Standalone Linux/Container Packaging Script
# Compiles the C engine, builds Godot client assets, and bundles standalone zip
# ==============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="${REPO_ROOT}/dist"
STAGE_DIR="${DIST_DIR}/Angband3D-Linux-x64"
PKG_NAME="Angband3D-Standalone-Linux-x64"

echo "========================================="
echo "  Angband3D Linux Packaging Tool         "
echo "========================================="

# 1. Compile C Engine
echo "[1/4] Building Angband C engine..."
mkdir -p "${REPO_ROOT}/engine/build"
pushd "${REPO_ROOT}/engine/build" > /dev/null
cmake .. -DCMAKE_BUILD_TYPE=Release
make -j"$(nproc)"
popd > /dev/null

# 2. Stage distribution files
echo "[2/4] Staging distribution..."
rm -rf "${STAGE_DIR}"
mkdir -p "${STAGE_DIR}/engine/build/game"

cp "${REPO_ROOT}/engine/build/game/angband" "${STAGE_DIR}/engine/build/game/"
cp -r "${REPO_ROOT}/engine/build/game/lib" "${STAGE_DIR}/engine/build/game/"
mkdir -p "${STAGE_DIR}/engine/build/game/lib/save"

# Copy play launcher
cat << 'EOF' > "${STAGE_DIR}/play.sh"
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}"
if [ -f "./Angband3D.x86_64" ]; then
    ./Angband3D.x86_64 "$@"
else
    echo "Starting engine directly in bridge mode..."
    ./engine/build/game/angband -mbridge "$@"
fi
EOF
chmod +x "${STAGE_DIR}/play.sh"

# 3. Compress to ZIP
echo "[3/4] Creating ZIP distribution..."
mkdir -p "${DIST_DIR}"
pushd "${DIST_DIR}" > /dev/null
rm -f "${PKG_NAME}.zip" "angband3d-standalone.zip"
zip -r -q "${PKG_NAME}.zip" "$(basename "${STAGE_DIR}")"
cp "${PKG_NAME}.zip" "angband3d-standalone.zip"
popd > /dev/null

echo "[4/4] Linux packaging complete!"
echo "Distribution archive: ${DIST_DIR}/${PKG_NAME}.zip"
echo "Canonical download:   ${DIST_DIR}/angband3d-standalone.zip"
