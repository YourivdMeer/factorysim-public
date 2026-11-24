#!/usr/bin/env bash
# Windows export (plan 32): exports the app headless, adds the text files for testers and
# makes a zip. The export itself is not in git (/export/ is in .gitignore).
#
#   bash tools/export.sh            (Git Bash, from anywhere)
#
# Needs the Godot 4.7.2 .NET export templates (Editor -> Manage Export Templates); without them
# Godot stops with "No export template found". Set GODOT to use another Godot console exe.
set -euo pipefail
cd "$(dirname "$0")/.."
G="${GODOT:-../tools/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}"
VERSION="$(sed -n 's/^config\/version="\(.*\)"/\1/p' src/FactorySim.App/project.godot)"
OUT="export/FactorySim"
ZIP="export/FactorySim-$VERSION-windows-x86_64.zip"

echo "== dotnet build"
dotnet build FactorySim.sln -c Release 2>&1 | grep -E "rror\(s\)"

echo "== THIRD-PARTY-NOTICES.txt"
py tools/notices/build_notices.py --godot "$G"

echo "== export $VERSION"
rm -rf "$OUT" "$ZIP"
mkdir -p "$OUT"
# The preset's file_version must be the numeric form of config/version (0.1.0-beta.1 -> 0.1.0.1).
NUMERIC="$(echo "$VERSION" | sed -E 's/^([0-9]+\.[0-9]+\.[0-9]+)(-[a-z]+\.([0-9]+))?$/\1.\3/; s/\.$/.0/')"
grep -q "^application/file_version=\"$NUMERIC\"" src/FactorySim.App/export_presets.cfg \
  || echo "WARNING: set application/file_version and product_version in export_presets.cfg to $NUMERIC"
LOG="export/godot-export.log"
"$G" --headless --path src/FactorySim.App --export-release "Windows Desktop" "../../$OUT/FactorySim.exe" 2>&1 | tee "$LOG" | grep -E "ERROR|WARNING" || true
# Godot can leave an exe behind when the C# publish fails, so check the .NET output too.
DATA="$(ls -d "$OUT"/data_*_windows_x86_64 2>/dev/null | head -1)"; DATA="${DATA:-$OUT/data_missing}"
for f in "$OUT/FactorySim.exe" "$OUT/FactorySim.pck" "$DATA/FactorySim.App.dll" "$DATA/FactorySim.Core.dll" "$DATA/hostfxr.dll"; do
  [ -f "$f" ] || { echo "EXPORT FAILED: $f is missing (see $LOG)"; exit 1; }
done
if grep -q "^ERROR" "$LOG"; then echo "EXPORT FAILED: Godot reported errors (see $LOG)"; exit 1; fi

echo "== text files"
cp HANDLEIDING.txt WIJZIGINGEN.txt THIRD-PARTY-NOTICES.txt "$OUT/"
[ -f BETA-VOORWAARDEN.txt ] && cp BETA-VOORWAARDEN.txt "$OUT/" || echo "(no BETA-VOORWAARDEN.txt: exported without it)"
# Licence files of the .NET runtime pack, if the export carries them (plan 27, SECURITY.md).
find "$OUT" -mindepth 2 \( -iname "LICENSE.TXT" -o -iname "THIRD-PARTY-NOTICES.TXT" \) -print | while read -r f; do
  cp "$f" "$OUT/dotnet-$(basename "$f")"
done
printf '@echo off\r\nrem Start FactorySim with OpenGL, for graphics cards or drivers where the normal start fails.\r\nstart "" "%%~dp0FactorySim.exe" --rendering-method gl_compatibility --rendering-driver opengl3 %%*\r\n' > "$OUT/FactorySim (compatibiliteit).bat"

echo "== zip"
# Right after the export the virus scanner can still hold FactorySim.exe; try a few times.
for try in 1 2 3 4 5 6; do
  ( cd export && powershell.exe -NoProfile -Command "\$ErrorActionPreference='Stop'; Compress-Archive -Path 'FactorySim' -DestinationPath '$(basename "$ZIP")' -Force" ) && break
  echo "(zip attempt $try failed, retrying in 10 s)"; rm -f "$ZIP"; sleep 10
done
[ -f "$ZIP" ] || { echo "EXPORT FAILED: could not write $ZIP"; exit 1; }
du -sh "$OUT" "$ZIP"
echo "EXPORT OK: $ZIP"
