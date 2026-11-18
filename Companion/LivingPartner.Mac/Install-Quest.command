#!/bin/sh
set -u

kit_dir=$(CDPATH= cd "$(dirname "$0")" && pwd) || exit 1
apk="$kit_dir/Quest/LivingAIPartner.apk"
if [ ! -s "$apk" ]; then
    printf '%s\n' 'Missing Quest/LivingAIPartner.apk. Extract the full Mac kit and try again.' >&2
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
    printf '%s\n' 'Download Android SDK Platform-Tools for Mac, unzip it in Downloads and try again.' \
        'https://developer.android.com/tools/releases/platform-tools#downloads' >&2
    exit 1
fi

printf '%s\n' 'Checking for Quest. If asked, allow USB debugging in your headset.'
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
        printf '%s\n' 'Multiple headsets connected. Keep the Quest you want to update connected and retry.' >&2
        exit 1
    fi
    selected=$serial
done <<DEVICES
$(printf '%s\n' "$devices" | tr -d '\r')
DEVICES

if [ -z "$selected" ]; then
    if [ "$pending" -eq 1 ]; then
        printf '%s\n' 'Allow USB debugging in your headset. If it is offline, reconnect the cable and try again.' >&2
    else
        printf '%s\n' 'No Quest found. Enable developer mode and connect the headset with a USB data cable.' >&2
    fi
    exit 1
fi

pkg=com.livingaipartner.quest
remote=$("$adb_path" -s "$selected" shell pm path "$pkg" | tr -d '\r' | sed -n 's/^package:\(.*\/base\.apk\)$/\1/p' | head -n 1)
if [ -n "$remote" ]; then
    installed=$("$adb_path" -s "$selected" shell sha256sum "$remote" | awk '{print $1}')
    bundled=$(shasum -a 256 "$apk" | awk '{print $1}')
    if [ "$installed" = "$bundled" ]; then
        printf '%s\n' 'Yonder is up to date.'
        exit 0
    fi
    if [ "${1:-}" = "--prepare" ] && "$adb_path" -s "$selected" shell pidof "$pkg" >/dev/null 2>&1; then exit 0; fi
fi

printf '%s\n' 'Installing Yonder on Quest…'
if "$adb_path" -s "$selected" install -r "$apk"; then
    printf '%s\n' 'Installed. Open Yonder under Unknown Sources in your Quest library.'
else
    printf '%s\n' 'Installation failed. Check the ADB message above for details.' >&2
    exit 1
fi
