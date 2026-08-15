#!/usr/bin/env bash
set -uo pipefail
exec "$(dirname "$0")/effect.sh" slice "$@"
