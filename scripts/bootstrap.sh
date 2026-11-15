#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_VERSION="8.0.422"
DOTNET_DIR="$ROOT_DIR/.dotnet"
INSTALL_SCRIPT="$ROOT_DIR/.setup/dotnet-install.sh"

if [[ -x "$DOTNET_DIR/dotnet" ]] &&
   [[ "$($DOTNET_DIR/dotnet --version)" == "$SDK_VERSION" ]]; then
  echo ".NET SDK $SDK_VERSION is already installed."
  exit 0
fi

mkdir -p "$ROOT_DIR/.setup" "$DOTNET_DIR"
if [[ ! -f "$INSTALL_SCRIPT" ]]; then
  curl --fail --location --silent --show-error \
    https://dot.net/v1/dotnet-install.sh \
    --output "$INSTALL_SCRIPT"
  chmod +x "$INSTALL_SCRIPT"
fi

"$INSTALL_SCRIPT" \
  --version "$SDK_VERSION" \
  --install-dir "$DOTNET_DIR" \
  --no-path

echo "Development SDK installed at $DOTNET_DIR"
