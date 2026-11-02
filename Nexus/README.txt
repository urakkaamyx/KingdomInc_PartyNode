Party-Up 0.6.0 for Kingdom .Inc
================================

Assemble an exact party of up to five adventurers, then send everyone together.

REQUIRES
Kingdom .Inc for Windows x64 (Unity Mono) and BepInEx 5.x, installed separately.
Tested with BepInEx 5.4.23.6. Loader releases:
https://github.com/BepInEx/BepInEx/releases

INSTALL
1. Close the game. Install BepInEx 5.x Windows x64 if it is not already installed.
2. Extract this ZIP into the folder containing KingdomInc.exe, merging BepInEx.
3. The mod DLL belongs at BepInEx/plugins/PartyNode/PartyNode.dll.
4. Start the game. Research Party Node beside Adventurers' Guild, then build it.

USE
Use the gear to configure a lineup of at most five members total. The five green
inputs are individual slots, in Adventurer, Fighter, Rogue, Mage, Cleric order.
For 3 Fighters, 1 Mage and 1 Cleric, they accept Fighter/Fighter/Fighter/Mage/Cleric.
Zero disables a class. None/Adventurer means the ordinary Adventurer, not any class.
Advanced classes match their base archetype. Connect the blue output to a receiver.

Manual Send now is the default, even for a complete lineup. You may send a partial
party, but the receiver must have room for every member actually being sent.
Research Auto Send inside the Party Node tech tree, then use the bottom + slot to
purchase/install Auto Send on an individual node. Remove it to return to manual.
Auto Send waits for every selected member AND enough receiver capacity.

The whole party transfers together. Internal occupancy and queued arrivals count
for stockpiles, inns, quest boards and training grounds. Dungeon/Party destinations
fill compatible free adventurer slots; slots wired to other sources are excluded.
Other node types need room in their connected input. A blocked party stays here.
No new party item is created, and dungeon party-size limits are unchanged.

Actual buffered members appear walking in the meetup room. Saves preserve their
equipment, quality and original identities, as well as settings and upgrades.

UPDATING / REMOVAL
To update, close the game and replace PartyNode.dll with this version.
Earlier enabled Auto Send popup settings migrate to an installed native upgrade.
Before uninstalling, send all waiting members, delete Party Nodes, save and close
the game. Keep the mod installed when loading a kingdom that contains its nodes.
Back up your kingdom before adding or removing mods.

MANUAL INSTALLATION
Vortex deployment has not been verified. Do not install BepInEx 6/IL2CPP for this DLL.
No other gameplay mod is required. Broad third-party mod compatibility is untested.

CREDITS
Mod code and generated custom artwork by Urakka. Game UI and class sprites are
loaded from the installed game and belong to its creators; they are not bundled.
Custom room and thumbnail artwork was made with the built-in image generation tool.
Thumbnail uses the shared Kingdom .Inc mod template. BepInEx and Harmony belong to
their respective authors and are not included. This is an unofficial mod.
