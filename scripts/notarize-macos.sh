#!/bin/bash
set -euo pipefail

if [[ $# != 1 || $1 != *-osx-arm64-unsigned.zip || ! -f $1 ]]; then
    echo "Usage: bash $0 <PracticalTools-VERSION-osx-arm64-unsigned.zip>" >&2
    exit 2
fi
[[ $(uname -s) == Darwin ]] || { echo "Run on macOS." >&2; exit 1; }
: "${MACOS_SIGN_IDENTITY:?Set the full Developer ID Application identity}"
[[ $MACOS_SIGN_IDENTITY == "Developer ID Application: "* ]] || { echo "Expected a Developer ID Application identity." >&2; exit 1; }
profile=${MACOS_NOTARY_PROFILE:-PracticalTools-notary}
archive="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
output="${archive%-unsigned.zip}-notarized.zip"
[[ ! -e $output ]] || { echo "Already exists: $output" >&2; exit 1; }

# Stage beside the archive so the verified zip can be published atomically.
work=$(mktemp -d "$(dirname "$archive")/.practicaltools-notary.XXXXXXXX")
trap 'rm -rf "$work"' EXIT
ditto -x -k "$archive" "$work"
app="$work/PracticalTools.app"
main="$app/Contents/MacOS/PracticalTools.Avalonia"
[[ -x $main ]] || { echo "Missing executable: $main" >&2; exit 1; }
sign=(codesign --force --timestamp --options runtime --sign "$MACOS_SIGN_IDENTITY")

# Include native .NET libraries and VLC plugins regardless of filename extension.
find "$app/Contents" -type f -print0 > "$work/files"
while IFS= read -r -d '' path; do
    [[ $path != "$main" ]] || continue
    format=$(file -b "$path")
    if [[ $format == Mach-O* ]]; then
        "${sign[@]}" "$path"
    fi
done < "$work/files"

# The self-contained .NET host uses the JIT under hardened runtime.
cat > "$work/entitlements.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict><key>com.apple.security.cs.allow-jit</key><true/></dict></plist>
PLIST
"${sign[@]}" --entitlements "$work/entitlements.plist" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
ditto -c -k --keepParent "$app" "$work/submission.zip"
submit_rc=0
xcrun notarytool submit "$work/submission.zip" --keychain-profile "$profile" --wait --output-format plist > "$work/result.plist" || submit_rc=$?
if [[ $submit_rc != 0 ]] && ! /usr/libexec/PlistBuddy -c 'Print :status' "$work/result.plist" >/dev/null 2>&1; then
    cat "$work/result.plist" >&2
    exit "$submit_rc"
fi
status=$(/usr/libexec/PlistBuddy -c 'Print :status' "$work/result.plist")
if [[ $status != Accepted ]]; then
    id=$(/usr/libexec/PlistBuddy -c 'Print :id' "$work/result.plist")
    echo "Notarization: $status ($id)" >&2
    xcrun notarytool log "$id" --keychain-profile "$profile" >&2
    exit 1
fi
xcrun stapler staple "$app"
ditto -c -k --keepParent "$app" "$work/final.zip"
ditto -x -k "$work/final.zip" "$work/check"
verified="$work/check/PracticalTools.app"
codesign --verify --deep --strict "$verified"
xcrun stapler validate "$verified"
spctl --assess --type execute --verbose=2 "$verified"
ln "$work/final.zip" "$output"
shasum -a 256 "$output"
