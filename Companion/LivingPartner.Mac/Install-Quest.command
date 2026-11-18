#!/bin/sh
set -u

kit_dir=$(CDPATH= cd "$(dirname "$0")" && pwd) || exit 1
apk="$kit_dir/Quest/LivingAIPartner.apk"
if [ ! -s "$apk" ]; then
    printf '%s\n' 'Quest/LivingAIPartner.apk 파일을 찾을 수 없습니다. APK가 포함된 Mac 배포본의 압축을 풀고 다시 실행하세요.' >&2
    exit 1
fi

adb_path=$(command -v adb 2>/dev/null || true)
if [ ! -x "$adb_path" ]; then
    for candidate in "${ANDROID_HOME:-/nonexistent}/platform-tools/adb" "${ANDROID_SDK_ROOT:-/nonexistent}/platform-tools/adb" \
        "$HOME/Library/Android/sdk/platform-tools/adb" "$HOME/Downloads/platform-tools/adb" /opt/homebrew/bin/adb /usr/local/bin/adb; do
        if [ -x "$candidate" ]; then adb_path=$candidate; break; fi
    done
fi
if [ ! -x "$adb_path" ]; then
    printf '%s\n' 'Mac용 Android SDK Platform-Tools를 받아 다운로드 폴더에 압축을 풀고 다시 실행하세요.' \
        'https://developer.android.com/tools/releases/platform-tools#downloads' >&2
    exit 1
fi

printf '%s\n' '연결된 Quest를 확인합니다. 헤드셋에 USB 디버깅 허용 창이 뜨면 이 Mac을 허용하세요.'
devices=$("$adb_path" devices) || exit 1
selected=
pending=0
while read -r serial status rest; do
    case "$status" in
        unauthorized|offline) pending=1; continue ;;
        device) ;;
        *) continue ;;
    esac
    model=$("$adb_path" -s "$serial" shell getprop ro.product.model </dev/null | tr -d '\r')
    case "$model" in
        Quest|"Quest "*|"Meta Quest"*|"Oculus Quest"*) ;;
        *) continue ;;
    esac
    if [ -n "$selected" ]; then
        printf '%s\n' 'Quest가 둘 이상 연결되어 있습니다. 설치할 헤드셋 하나만 연결하고 다시 실행하세요.' >&2
        exit 1
    fi
    selected=$serial
done <<DEVICES
$(printf '%s\n' "$devices" | tr -d '\r')
DEVICES

if [ -z "$selected" ]; then
    if [ "$pending" -eq 1 ]; then
        printf '%s\n' '헤드셋을 착용하고 USB 디버깅을 허용하세요. 연결이 오프라인이면 케이블을 다시 연결하고 이 파일을 여세요.' >&2
    else
        printf '%s\n' 'Quest를 찾지 못했습니다. 개발자 모드를 켠 뒤 데이터 전송용 USB 케이블로 Mac에 연결하고 다시 실행하세요.' >&2
    fi
    exit 1
fi

pkg=com.livingaipartner.quest
remote=$("$adb_path" -s "$selected" shell pm path "$pkg" | tr -d '\r' | sed -n 's/^package:\(.*\/base\.apk\)$/\1/p' | head -n 1)
if [ -n "$remote" ]; then
    installed=$("$adb_path" -s "$selected" shell sha256sum "$remote" | awk '{print $1}')
    bundled=$(shasum -a 256 "$apk" | awk '{print $1}')
    if [ "$installed" = "$bundled" ]; then
        printf '%s\n' '최신 버전이 설치되어 있습니다.'
        exit 0
    fi
    if [ "${1:-}" = "--prepare" ] && "$adb_path" -s "$selected" shell pidof "$pkg" >/dev/null 2>&1; then exit 0; fi
fi

printf '%s\n' 'Living AI Partner를 설치하거나 기존 앱을 업데이트합니다.'
if "$adb_path" -s "$selected" install -r "$apk"; then
    printf '%s\n' '설치했습니다. Quest 앱 라이브러리의 알 수 없는 출처에서 Living AI Partner를 여세요.'
else
    printf '%s\n' '설치에 실패했습니다. 위의 ADB 오류를 확인하세요. 버전이나 서명이 다르면 기존 앱과 호환되는 APK가 필요합니다.' >&2
    exit 1
fi
