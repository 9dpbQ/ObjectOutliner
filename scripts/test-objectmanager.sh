#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

export DOTNET_CLI_HOME="$ROOT_DIR/.dotnet-home"
export NUGET_PACKAGES="$ROOT_DIR/.nuget/packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

if [[ -x "$ROOT_DIR/.dotnet/dotnet" ]]; then
  DOTNET_BIN="$ROOT_DIR/.dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
  DOTNET_BIN="$(command -v dotnet)"
else
  echo "dotnet was not found. Run: sh scripts/bootstrap.sh" >&2
  exit 1
fi

"$DOTNET_BIN" run \
  --project "$ROOT_DIR/ObjectManager.RegressionTests/ObjectManager.RegressionTests.csproj"
