# Moon Sweepers mods: installer

1. Download `MoonSweepersMods-Installer.zip` from the newest **Modpack** release and unzip it anywhere.
2. Close Moon Sweepers and double-click **Install or update.bat**.

It finds the game through Steam and installs BepInEx 6 (be.791). The MoonSweepers.ModEngine patcher is only on Nexus
Mods, so the first run opens its page: download the zip into your Downloads folder, press Enter, and the installer
takes it from there. Then it installs the newest modpack from GitHub.

It also makes a desktop shortcut, **Moon Sweepers (modded)**. Start the game from that shortcut and the mods update
first, every time. Starting from Steam works too, just without the update check.

To remove the mods: close the game and double-click **Uninstall mods.bat** (in the unzipped installer, or in the game
folder's `MoonSweepersMods`). It removes the mods, then asks whether to remove BepInEx too for a plain game. Saves stay:
modded vehicles wait in `garagelib.json` next to the save and come back if you reinstall.
