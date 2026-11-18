#!/bin/zsh
set -eu
cd -- "${0:A:h}"
if [[ "$(curl --silent --max-time 1 http://127.0.0.1:47610/health || true)" == "Yonder Mac Companion" ]]; then
    /usr/bin/open http://127.0.0.1:47610
    exit 0
fi
print 'Opening Yonder on your display. Press Control-C here to quit.'
exec "./runtime/LivingPartner.Mac" "$@"
