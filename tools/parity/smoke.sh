#!/usr/bin/env bash
# Smoke parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" smoke "$@"
