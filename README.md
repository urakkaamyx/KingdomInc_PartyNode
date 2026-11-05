# Party-Up

Party-Up adds a configurable Party Node to Kingdom .Inc. Assemble a party of up to five adventurers, then send the group together when its destination has enough room.

The mod is published as **Party-Up**. Its plugin, source project, and in-game building are named **PartyNode / Party Node**.

## Features

- Five individual adventurer inputs and one output for the whole group.
- Configurable counts of Adventurers, Fighters, Rogues, Mages, and Clerics.
- Manual dispatch, including incomplete parties, with a researchable Auto Send upgrade.
- Capacity checks that keep a party together when a destination is full.
- Live adventurers walking around the meetup room while they wait.
- Persistent settings, members, research, and installed upgrades.

## Requirements

Kingdom .Inc for Windows x64, BepInEx 5.x Windows x64, and Foundation 0.5.0 or later, installed separately.

## Installation

1. Close Kingdom .Inc and install the requirements above.
2. Download the player ZIP from [GitHub Releases](https://github.com/urakkaamyx/KingdomInc_PartyNode/releases) or the [Party-Up Nexus page](https://www.nexusmods.com/kingdominc/mods/5).
3. Extract the ZIP into the folder containing `KingdomInc.exe`.
4. Confirm the plugin is at `BepInEx/plugins/PartyNode/PartyNode.dll`.
5. Start the game and open **Tech Tree → Mods → Party-Up**. Unlock **Adventurers' Guild**, research **Party Node**, and build it from **Utility**.

To update, close the game and replace the existing `PartyNode.dll`. Avoid installing duplicate copies.

## Configure a party

Click the node's **gear** and use the minus/plus controls to choose a lineup of at most five members total. Settings apply immediately.

The five **green inputs** are individual party slots, filled in Adventurer, Fighter, Rogue, Mage, then Cleric order. For **3 Fighters, 1 Mage, and 1 Cleric**, they accept Fighter / Fighter / Fighter / Mage / Cleric. Class indicators show the expected member; unused slots accept nothing. The **blue output** sends the group to its connected receiver.

A count of zero removes that class from the lineup. **None / Adventurer** means an ordinary Adventurer, not any class. Advanced classes match their base archetype. Conscripts and unrelated items are rejected.

## Send a party

Manual sending is the default. Click **Send now** to dispatch waiting members, even if the selected lineup is incomplete. The destination must have room for every member being sent.

For automatic dispatch, open the **Party Node** research branch and research **Auto Send**, then click the **+ upgrade slot** at the bottom of a Party Node to buy and install it. Research alone leaves a node manual. Remove the upgrade through the same slot to return to manual dispatch.

Auto Send waits until every selected slot is filled and the receiver can accept the entire group. A full party stays at the node if the receiver has insufficient space. An all-zero lineup sends nothing automatically.

Stockpiles, inns, quest boards, and training grounds count both occupied spaces and queued arrivals. Dungeon and Party Node receivers use compatible free adventurer slots, excluding slots connected to other sources. Other destinations need enough room in the connected input itself.

The mod sends the original adventurers together. It does not create a new party item or change dungeon party-size limits. Lowering a count does not delete waiting members; use **Send now** to clear a previous lineup when necessary.

## Saves and removal

Saves retain the lineup, waiting adventurers, pending output, and upgrades. Copies preserve settings and installed upgrades without duplicating adventurers.

Older enabled Auto Send settings migrate to an installed upgrade. Legacy buffers containing more than five members are preserved in a recovery reserve and can be sent in batches of up to five.

Keep this mod installed while loading a kingdom containing Party Nodes. Before uninstalling, send all waiting members, remove the Party Nodes, save the kingdom, and close the game. Then remove `BepInEx/plugins/PartyNode/PartyNode.dll`.

## Credits

Mod code and custom generated artwork by Urakka. Native game UI and class sprites are read from the installed game. BepInEx and Harmony retain their respective authorship and are not bundled.
