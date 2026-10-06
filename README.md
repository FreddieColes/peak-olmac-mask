# Ol' Mac Mask for PEAK

Adds a cardboard Ol' Mac chicken face mask, strapped round your head, to the hats in your passport.

Mates without the mod see you bald, but you can still play together.

## Tweaking the fit
Edit the config (BepInEx/config/com.freddiecoles.olmacmask.cfg) while the game is running and press F9 to re-place the mask.

## Building (Codespace)
1. `./setup-game.sh` downloads PEAK so we can compile against it (never committed)
2. `./report.sh` maps the game's hat code
3. `./build.sh` makes the Thunderstore zip in dist/
