#!/usr/bin/env bash
# Decrypt parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" decrypt "$@"
