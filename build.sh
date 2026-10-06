#!/usr/bin/env bash
# Builds the plugin and packages a Thunderstore zip in dist/.
# If the build fails, the log is pushed so Claude can read it.
set -o pipefail
VERSION=$(grep -oP 'BepInPlugin\(Guid, "[^"]*", "\K[0-9.]+' src/Plugin.cs)
MANAGED="$HOME/peak/PEAK_Data/Managed"
[ -d "$MANAGED" ] || { echo "Run ./setup-game.sh first"; exit 1; }
mkdir -p logs dist
if ! dotnet build src/OlMacMask.csproj -c Release -p:ManagedDir="$MANAGED" 2>&1 | tee logs/build.log; then
  git add logs/build.log && git commit -qm "Build failed log" && git push -q
  echo; echo "Build failed. Log pushed, tell Claude."; exit 1
fi
STAGE=$(mktemp -d)
cp src/bin/Release/netstandard2.1/OlMacMask.dll assets/olmac-face.png assets/icon.png README.md "$STAGE"/
cat > "$STAGE/manifest.json" <<JSON
{
  "name": "OlMacMask",
  "version_number": "$VERSION",
  "website_url": "https://github.com/FreddieColes/peak-olmac-mask",
  "description": "Wear Ol' Mac's face as a cardboard mask, held on with a pink rubber band.",
  "dependencies": ["BepInEx-BepInExPack_PEAK-5.4.2403"]
}
JSON
rm -f dist/*.zip
(cd "$STAGE" && zip -qr "$OLDPWD/dist/OlMacMask-$VERSION.zip" .)
echo; echo "Built dist/OlMacMask-$VERSION.zip"
echo "Right-click it in the file list on the left and choose Download."
