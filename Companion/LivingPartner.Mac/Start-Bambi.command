#!/bin/zsh
set -eu
cd -- "${0:A:h}"
"./Install-Quest.command" --prepare >/dev/null 2>&1 &
if [[ "$(curl --silent --max-time 1 http://127.0.0.1:47610/health || true)" == "Bambi Mac Companion" ]]; then
    /usr/bin/open http://127.0.0.1:47610
    exit 0
fi
print '밤비의 TV를 여는 중입니다. 종료하려면 이 창에서 Control-C를 누르세요.'
exec "./runtime/LivingPartner.Mac" "$@"
