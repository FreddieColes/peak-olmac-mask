#!/usr/bin/env bash
# Downloads PEAK (Windows build) into ~/peak so we can compile against the real game files.
# The game files stay in the Codespace and are never committed.
set -e
STEAMCMD="$HOME/steamcmd"
GAME="$HOME/peak"
if [ ! -x "$STEAMCMD/steamcmd.sh" ]; then
  mkdir -p "$STEAMCMD"
  curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz -C "$STEAMCMD"
fi
read -rp "Steam username: " STEAM_USER
echo "Enter your Steam password when asked, then approve the login in the Steam app on your phone."
"$STEAMCMD/steamcmd.sh" +@sSteamCmdForcePlatformType windows +force_install_dir "$GAME" \
  +login "$STEAM_USER" +app_update 3527290 validate +quit
if [ -d "$GAME/PEAK_Data/Managed" ]; then
  echo; echo "PEAK downloaded. Managed DLLs: $(ls "$GAME/PEAK_Data/Managed" | wc -l)"
  echo "Next: ./report.sh"
else
  echo; echo "Couldn't find PEAK_Data/Managed. Copy the output above and send it to Claude."; exit 1
fi
