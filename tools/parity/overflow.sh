#!/usr/bin/env bash
# Overflow parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" overflow "$@"
