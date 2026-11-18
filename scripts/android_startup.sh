#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/startup
package=co.zyma.anikatha
capture() {
  adb logcat -d -v threadtime > artifacts/startup/logcat.txt || true
  adb shell dumpsys activity activities > artifacts/startup/activities.txt || true
  adb logcat -d -b crash > artifacts/startup/crash.txt || true
  adb exec-out screencap -p > artifacts/startup/screenshot.png || true
}
trap capture EXIT
apk=$(find artifacts/apk -name '*-Signed.apk' -print -quit)
test -n "$apk"
adb install -r "$apk"
adb logcat -c
adb shell am force-stop "$package"
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
# Cover native startup and the initial catalogue request's timeout/error path.
for check in $(seq 1 9); do
  sleep 5
  adb shell pidof "$package" >/dev/null || { echo 'FAIL: app exited after launch'; exit 1; }
done
adb shell dumpsys activity activities > artifacts/startup/activities.txt
python scripts/check_android_foreground.py artifacts/startup/activities.txt "$package"
adb logcat -d -b crash > artifacts/startup/crash.txt
if grep -E 'Process: co\.zyma\.anikatha|>>> co\.zyma\.anikatha <<<' artifacts/startup/crash.txt; then
  echo 'FAIL: Android recorded an AniKatha crash'; exit 1
fi
# The WebView must render the root UI, not merely keep a native process alive.
# Inspect the rendered DOM through the Debug APK's WebView DevTools endpoint.
# UIAutomator is unnecessary here and can crash during accessibility traversal.
app_pid=$(adb shell pidof "$package" | tr -d '\r' | awk '{print $1}')
adb forward tcp:9222 "localabstract:webview_devtools_remote_$app_pid"
node scripts/check_webview.mjs
echo 'PASS: APK installed without fast deployment, rendered navigation, and stayed open for 45 seconds.'
