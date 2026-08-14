#!/usr/bin/env bash
# Errorcorrect parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" errorcorrect "$@"
