#!/usr/bin/env bash
set -euo pipefail
mkdir -p artifacts/startup
package=co.zyma.anikatha
capture() {
  adb logcat -d -v threadtime > artifacts/startup/logcat.txt || true
  adb shell dumpsys activity activities > artifacts/startup/activities.txt || true
  adb exec-out screencap -p > artifacts/startup/screenshot.png || true
  adb shell uiautomator dump /sdcard/anikatha-window.xml >/dev/null 2>&1 || true
  adb pull /sdcard/anikatha-window.xml artifacts/startup/window.xml >/dev/null 2>&1 || true
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
grep -E 'mResumedActivity.*co\.zyma\.anikatha' artifacts/startup/activities.txt >/dev/null || { echo 'FAIL: AniKatha is not the foreground activity'; exit 1; }
adb logcat -d -b crash > artifacts/startup/crash.txt
if grep -E 'Process: co\.zyma\.anikatha|>>> co\.zyma\.anikatha <<<' artifacts/startup/crash.txt; then
  echo 'FAIL: Android recorded an AniKatha crash'; exit 1
fi
# The WebView must render the root UI, not merely keep a native process alive.
adb shell uiautomator dump /sdcard/anikatha-window.xml
adb pull /sdcard/anikatha-window.xml artifacts/startup/window.xml
grep -E 'text="(Home|Anime|Manga|Library|Profile)"|content-desc="(Home|Anime|Manga|Library|Profile)"' artifacts/startup/window.xml >/dev/null || { echo 'FAIL: app navigation did not render'; exit 1; }
echo 'PASS: APK installed without fast deployment, rendered navigation, and stayed open for 45 seconds.'
