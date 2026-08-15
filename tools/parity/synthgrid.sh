#!/usr/bin/env bash
# SynthGrid parity wrapper — see effect.sh.
set -uo pipefail
exec "$(dirname "$0")/effect.sh" synthgrid "$@"
