#!/usr/bin/env bash
# Builds every mod and publishes them as a GitHub release, for UpdateValheimMods.bat to download.
#
#   ./release.sh            # release what's committed and pushed on main
#   ./release.sh --dry-run  # build and zip only, print what would be published
#
# The zip is always uploaded as "valheim-mods.zip", so the latest one is at
# https://github.com/Andrei-III/valheim-mods/releases/latest/download/valheim-mods.zip
set -euo pipefail

REPO="Andrei-III/valheim-mods"
ROOT="$(cd "$(dirname "$0")" && pwd)"
DRY_RUN=0
[[ "${1:-}" == "--dry-run" ]] && DRY_RUN=1
cd "$ROOT"

# Only release code that's committed and pushed, so the release matches the repo.
if [[ $DRY_RUN -eq 0 ]]; then
    if [[ -n "$(git status --porcelain -- '*.cs' '*.csproj')" ]]; then
        echo "Uncommitted mod changes; commit and push first." >&2; exit 1
    fi
    git fetch -q origin main
    if [[ "$(git rev-parse HEAD)" != "$(git rev-parse origin/main)" ]]; then
        echo "HEAD isn't the same as origin/main; push (or pull) first." >&2; exit 1
    fi
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/mods"

VERSIONS=""
for proj in */*.csproj; do
    mod="$(basename "$proj" .csproj)"
    echo "Building $mod..."
    dotnet build "$proj" -c Release -nologo -v quiet >/dev/null
    dll="$(dirname "$proj")/bin/Release/$mod.dll"
    [[ -f "$dll" ]] || { echo "Missing $dll after build" >&2; exit 1; }
    mkdir -p "$STAGE/mods/$mod"
    cp "$dll" "$STAGE/mods/$mod/"
    ver="$(grep -oP 'const string Version = "\K[^"]+' "$(dirname "$proj")/Plugin.cs")"
    VERSIONS+="$mod $ver"$'\n'
done

TAG="v$(date +%Y.%m.%d)"
if gh release view "$TAG" -R "$REPO" >/dev/null 2>&1; then
    n=2; while gh release view "$TAG-$n" -R "$REPO" >/dev/null 2>&1; do n=$((n + 1)); done
    TAG="$TAG-$n"
fi

printf '%s\n%s' "$TAG" "$VERSIONS" > "$STAGE/mods/versions.txt"
(cd "$STAGE/mods" && zip -qr "$STAGE/valheim-mods.zip" .)
cp "$ROOT/UpdateValheimMods.bat" "$STAGE/"

# Notes: mod versions, plus the commits since the previous release.
PREV="$(gh release view -R "$REPO" --json tagName -q .tagName 2>/dev/null || true)"
{
    echo "Download **UpdateValheimMods.bat** once and double-click it whenever there's a new release."
    echo "It installs or updates all mods below. Close Valheim first."
    echo
    echo "| Mod | Version |"
    echo "|---|---|"
    while read -r m v; do [[ -n "$m" ]] && echo "| $m | $v |"; done <<< "$VERSIONS"
    if [[ -n "$PREV" ]]; then
        echo
        echo "### Changes since $PREV"
        git log --format='- %s' "$PREV..HEAD" -- '*/*.cs' '*/*.csproj'
    fi
} > "$STAGE/notes.md"

if [[ $DRY_RUN -eq 1 ]]; then
    echo; echo "Would publish $TAG:"; cat "$STAGE/notes.md"; echo; unzip -l "$STAGE/valheim-mods.zip"
    exit 0
fi

gh release create "$TAG" -R "$REPO" --target "$(git rev-parse HEAD)" --title "Valheim mods $TAG" \
    --notes-file "$STAGE/notes.md" "$STAGE/valheim-mods.zip" "$STAGE/UpdateValheimMods.bat"
echo "Published $TAG: https://github.com/$REPO/releases/tag/$TAG"
