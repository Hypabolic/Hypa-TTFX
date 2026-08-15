#!/usr/bin/env bash
# Bubbles parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" bubbles "$@"
