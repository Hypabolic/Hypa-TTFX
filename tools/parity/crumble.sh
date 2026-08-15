#!/usr/bin/env bash
# Crumble parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" crumble "$@"
