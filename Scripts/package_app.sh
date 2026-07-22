#!/bin/zsh
set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
app_path="$project_root/outputs/Codex Task Monitor.app"

cd "$project_root"
swift build -c release --product CodexTaskMonitor
binary_dir="$(swift build -c release --show-bin-path)"

rm -rf "$app_path"
mkdir -p "$app_path/Contents/MacOS"
install -m 755 "$binary_dir/CodexTaskMonitor" "$app_path/Contents/MacOS/CodexTaskMonitor"
install -m 644 "$project_root/Resources/Info.plist" "$app_path/Contents/Info.plist"

plutil -lint "$app_path/Contents/Info.plist"
codesign --force --sign - "$app_path"
codesign --verify --strict --verbose=2 "$app_path"

echo "$app_path"
