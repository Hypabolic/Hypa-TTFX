#!/usr/bin/env bash
# Wipe parity wrapper — see effect.sh.
set -uo pipefail
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
exec "$ROOT/tools/parity/effect.sh" wipe
