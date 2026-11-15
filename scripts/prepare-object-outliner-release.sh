#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="0.9.3"
STAGING_DIR="${OUTLINER_STAGING_DIR:-$ROOT_DIR/artifacts/yak-staging/ObjectOutliner-$VERSION}"

if [[ -e "$STAGING_DIR" ]]; then
  echo "Release staging already exists: $STAGING_DIR" >&2
  echo "Review or remove it before preparing another staging directory." >&2
  exit 1
fi

echo "== Running regression tests and Release builds =="
"$ROOT_DIR/scripts/check-objectmanager.sh" --configuration Release

mkdir -p "$STAGING_DIR/net7.0" "$STAGING_DIR/net48" "$STAGING_DIR/misc"

copy_required() {
  local source="$1"
  local destination="$2"
  if [[ ! -f "$source" ]]; then
    echo "Required release file is missing: $source" >&2
    exit 1
  fi
  cp "$source" "$destination"
}

copy_optional() {
  local source="$1"
  local destination="$2"
  if [[ -f "$source" ]]; then
    cp "$source" "$destination"
  fi
}

copy_required \
  "$ROOT_DIR/ObjectManager/bin/Release/net7.0/ObjectOutliner.rhp" \
  "$STAGING_DIR/net7.0/ObjectOutliner.rhp"
copy_optional \
  "$ROOT_DIR/ObjectManager/bin/Release/net7.0/libOOTreeGuides.dylib" \
  "$STAGING_DIR/net7.0/libOOTreeGuides.dylib"
copy_optional \
  "$ROOT_DIR/ObjectManager/bin/Release/net7.0/ObjectOutliner.deps.json" \
  "$STAGING_DIR/net7.0/ObjectOutliner.deps.json"
copy_optional \
  "$ROOT_DIR/ObjectManager/bin/Release/net7.0/ObjectOutliner.runtimeconfig.json" \
  "$STAGING_DIR/net7.0/ObjectOutliner.runtimeconfig.json"
copy_required \
  "$ROOT_DIR/ObjectManager/bin/Release/net48/ObjectOutliner.rhp" \
  "$STAGING_DIR/net48/ObjectOutliner.rhp"

copy_required "$ROOT_DIR/packaging/manifest.yml" "$STAGING_DIR/manifest.yml"
for framework in net7.0 net48; do
  copy_required "$ROOT_DIR/ObjectManager/bin/Release/$framework/ObjectOutliner.WindowsGuides.dll" "$STAGING_DIR/$framework/ObjectOutliner.WindowsGuides.dll"
done
copy_required "$ROOT_DIR/packaging/icon.png" "$STAGING_DIR/icon.png"
copy_required "$ROOT_DIR/packaging/README.txt" "$STAGING_DIR/misc/README.txt"
copy_required "$ROOT_DIR/LICENSE.txt" "$STAGING_DIR/misc/LICENSE.txt"
copy_required "$ROOT_DIR/CHANGELOG.md" "$STAGING_DIR/misc/CHANGELOG.md"

if find "$STAGING_DIR" -type f \( -name '*.pdb' -o -name '*.user' -o -name '.DS_Store' \) | grep -q .; then
  echo "Development-only files were found in release staging." >&2
  exit 1
fi

echo
echo "Release staging prepared without creating a Yak package:"
echo "$STAGING_DIR"
find "$STAGING_DIR" -type f -print | sort
