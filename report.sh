#!/usr/bin/env bash
# Maps PEAK's hat/cosmetic code (names and signatures only, no game code) and pushes the report.
set -e
GAME="$HOME/peak"
MANAGED="$GAME/PEAK_Data/Managed"
[ -d "$MANAGED" ] || { echo "Run ./setup-game.sh first"; exit 1; }
mkdir -p reports
{
  echo "# Unity version"
  strings "$GAME/PEAK_Data/globalgamemanagers" 2>/dev/null | grep -m1 -E '^20[0-9]{2}\.[0-9]+\.[0-9]+|^6000\.' || echo "not found"
} > reports/unity-version.txt
dotnet run --project tools/ApiDump -c Release -- "$MANAGED" reports/api.txt
git add reports
git commit -qm "API report" && git push -q
echo "Report pushed. Tell Claude it's done."
