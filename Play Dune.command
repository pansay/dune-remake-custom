#!/bin/zsh
set -eu
CRYOGENIC_FORK_DIR="${0:A:h}"
export CRYOGENIC_PLAYER_HOME="${CRYOGENIC_PLAYER_HOME:-$CRYOGENIC_FORK_DIR/.local/user}"
export DUNE_AUDIO_PREBUFFER_MS="${DUNE_AUDIO_PREBUFFER_MS:-120}"
if [[ ! -x "$CRYOGENIC_FORK_DIR/.local/app/Cryogenic" ]]; then
  print 'Build the player first with ./build-player.sh (requires the .NET 10 SDK).'
  exit 1
fi
cd "$CRYOGENIC_FORK_DIR"
exec "$CRYOGENIC_FORK_DIR/.local/app/Cryogenic" --player
