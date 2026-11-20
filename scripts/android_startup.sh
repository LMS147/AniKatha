#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/startup
package=co.zyma.anikatha
capture() {
  timeout 15s adb logcat -d -v threadtime > artifacts/startup/logcat.txt || true
  timeout 15s adb shell dumpsys activity activities > artifacts/startup/activities.txt || true
  timeout 15s adb logcat -d -b crash > artifacts/startup/crash.txt || true
  timeout 15s adb exec-out screencap -p > artifacts/startup/screenshot.png || true
}
trap capture EXIT
apk=$(find artifacts/apk -name '*-Signed.apk' -print -quit)
test -n "$apk"
probe() {
  local mode=$1
  adb shell am force-stop "$package"
  adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
  adb forward --remove tcp:9222 || true
  for check in $(seq 1 25); do
    local current_pid
    current_pid=$(adb shell pidof "$package" | tr -d '\r' | awk '{print $1}' || true)
    if [[ -n "$current_pid" ]]; then
      adb forward tcp:9222 "localabstract:webview_devtools_remote_$current_pid"
      if curl --silent --fail --max-time 2 http://127.0.0.1:9222/json/list | grep -q 'webSocketDebuggerUrl'; then
        node scripts/check_webview.mjs "$mode"
        return
      fi
    fi
    sleep 2
  done
  return 1
}
if [[ "${ANIKATHA_STARTUP_MODE:-fresh}" == "upgrade" ]]; then
  baseline=$(find artifacts/apk-baseline -name '*-Signed.apk' -print -quit)
  test -n "$baseline"
  adb install "$baseline"
  probe seed
  adb shell am force-stop "$package"
  adb install -r "$apk"
  probe upgrade
  exit 0
fi
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
node scripts/check_webview.mjs fresh
echo 'PASS: APK installed without fast deployment, rendered navigation, and stayed open for 45 seconds.'
