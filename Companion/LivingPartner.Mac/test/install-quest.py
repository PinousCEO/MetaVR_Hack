"""Exercise the Mac installer with a fake ADB. Never connects to a headset."""
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def verify():
    shell = sys.argv[1] if len(sys.argv) > 1 else "/bin/sh"
    installer = Path(__file__).resolve().parents[1] / "Install-Quest.command"
    with tempfile.TemporaryDirectory(prefix="bambi-quest-install-") as temp:
        kit = Path(temp) / "Yonder TV"
        quest = kit / "Quest"
        quest.mkdir(parents=True)
        shutil.copy(installer, kit)
        (quest / "LivingAIPartner.apk").write_bytes(b"test fixture")
        tools = kit / "Mock SDK" / "platform-tools"
        tools.mkdir(parents=True)
        adb = tools / "adb"
        adb.write_text("""#!/bin/sh
if [ "$*" = devices ]; then
    printf 'List of devices attached\\n'
    case "$CASE" in
        empty) ;;
        unauthorized) printf 'quest-a\\tunauthorized\\r\\n' ;;
        phone) printf 'phone-a\\tdevice\\r\\n' ;;
        multiple) printf 'quest-a\\tdevice\\nquest-b\\tdevice\\n' ;;
        *) printf 'phone-a\\tdevice\\nquest-a\\tdevice\\r\\n' ;;
    esac
    exit 0
fi
if [ "$1" = "-s" ] && [ "$3" = shell ] && [ "$4" = getprop ] && [ "$5" = ro.product.model ]; then
    case "$2" in quest-*) printf 'Quest 3\\r\\n';; *) printf 'Pixel\\r\\n';; esac
    exit 0
fi
if [ "$#" = 5 ] && [ "$1" = "-s" ] && [ "$2" = quest-a ] && [ "$3" = install ] && [ "$4" = "-r" ] && [ -f "$5" ]; then
    printf '%s\\n' "$5" > ./attempt
    [ "$CASE" != fail ]; exit $?
fi
printf 'Unexpected ADB command: %s\\n' "$*" >&2
exit 4
""", encoding="utf-8", newline="\n")
        adb.chmod(0o755)
        for case in ["success", "fail", "unauthorized", "empty", "phone", "multiple"]:
            attempt = kit / "attempt"
            attempt.unlink(missing_ok=True)
            # Resolve the fake executable inside this shell. A real ADB on the test
            # machine must never be used, even when a headset is currently attached.
            cmd = 'PATH="$PWD/Mock SDK/platform-tools:/usr/bin:/bin"; export PATH; exec sh ./Install-Quest.command'
            result = subprocess.run([shell, "-c", cmd], cwd=kit, env={**os.environ, "CASE": case},
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            assert (result.returncode == 0) == (case == "success"), (case, result.stdout)
            assert attempt.exists() == (case in {"success", "fail"}), case
            if attempt.exists():
                assert attempt.read_text().strip().endswith("/Yonder TV/Quest/LivingAIPartner.apk")
        print("PASS Quest installer selects only one authorized Quest, supplies exactly install -r, handles multiple/absent/unauthorized/non-Quest devices and installation failure")


if __name__ == "__main__":
    verify()
