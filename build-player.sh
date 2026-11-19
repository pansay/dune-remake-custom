#!/bin/sh
set -eu
CRYOGENIC_FORK_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
CRYOGENIC_DOTNET=${CRYOGENIC_DOTNET:-dotnet}
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) CRYOGENIC_RUNTIME=osx-arm64 ;;
  Darwin-x86_64) CRYOGENIC_RUNTIME=osx-x64 ;;
  Linux-aarch64) CRYOGENIC_RUNTIME=linux-arm64 ;;
  Linux-x86_64) CRYOGENIC_RUNTIME=linux-x64 ;;
  *) printf '%s\n' 'Use dotnet publish with the runtime identifier for your system.'; exit 1 ;;
esac
cd "$CRYOGENIC_FORK_DIR"
"$CRYOGENIC_DOTNET" publish src/Cryogenic/Cryogenic.csproj -c Release \
  -r "$CRYOGENIC_RUNTIME" --self-contained true -o .local/app -p:DebugType=None
printf '%s\n' 'Player built in .local/app. Launch with ./Play\ Dune.command on macOS.'
