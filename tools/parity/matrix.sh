#!/usr/bin/env bash
# Matrix parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" matrix "$@"
