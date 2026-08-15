#!/usr/bin/env bash
# Highlight parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" highlight "$@"
