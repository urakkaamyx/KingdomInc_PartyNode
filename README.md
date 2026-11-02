# Party-Up

Party-Up adds a configurable Party Node to Kingdom Inc. Assemble a party of up to five adventurers, then send the group together when its destination has enough room.

The mod is published as **Party-Up**. Its plugin, source project, and in-game building are named **PartyNode / Party Node**.

## Features

- Five individual adventurer inputs and one output for the whole group.
- Configurable counts of Adventurers, Fighters, Rogues, Mages, and Clerics.
- Manual dispatch, including incomplete parties, with a researchable Auto Send upgrade.
- Capacity checks that keep a party together when a destination is full.
- Live adventurers walking around the meetup room while they wait.
- Persistent settings, members, research, and installed upgrades.

## Requirements

Kingdom Inc. for Windows x64 using the Mono runtime, with BepInEx 5.x installed separately. The current release has been checked with BepInEx 5.4.23.6.

No other gameplay mod is required. BepInEx, Harmony, and game assemblies are supplied by your local installation.

## Installation

1. Close Kingdom Inc.
2. Download the player ZIP from [GitHub Releases](https://github.com/urakkaamyx/KingdomInc_PartyNode/releases) or the [Party-Up Nexus page](https://www.nexusmods.com/kingdominc/mods/5).
3. Extract the ZIP into the folder containing `KingdomInc.exe`.
4. Confirm the plugin is at `BepInEx/plugins/PartyNode/PartyNode.dll`.
5. Start the game, research **Party Node** beside **Adventurers' Guild**, and build it.

To update, close the game and replace the existing `PartyNode.dll`. Avoid installing duplicate copies. Manual extraction is supported; Vortex deployment has not been verified.

## Configure a party

Click the node's **gear** and use the minus/plus controls to choose a lineup of at most five members total. Settings apply immediately.

The five **green inputs** are individual party slots, filled in Adventurer, Fighter, Rogue, Mage, then Cleric order. For **3 Fighters, 1 Mage, and 1 Cleric**, they accept Fighter / Fighter / Fighter / Mage / Cleric. Class indicators show the expected member; unused slots accept nothing. The **blue output** sends the group to its connected receiver.

A count of zero removes that class from the lineup. **None / Adventurer** means an ordinary Adventurer, not any class. Advanced classes match their base archetype. Conscripts and unrelated items are rejected.

## Send a party

Manual sending is the default. Click **Send now** to dispatch waiting members, even if the selected lineup is incomplete. The destination must have room for every member being sent.

For automatic dispatch, research **Auto Send**, then click the native **+ upgrade slot** at the bottom of a Party Node to buy and install it. Research alone leaves a node manual. Remove the upgrade through the same slot to return to manual dispatch.

Auto Send waits until every selected slot is filled and the receiver can accept the entire group. A full party stays at the node if the receiver has insufficient space. An all-zero lineup sends nothing automatically.

Stockpiles, inns, quest boards, and training grounds count both occupied spaces and queued arrivals. Dungeon and Party Node receivers use compatible free adventurer slots, excluding slots connected to other sources. Other destinations need enough room in the connected input itself.

The mod sends the original adventurers together. It does not create a new party item or change dungeon party-size limits. Lowering a count does not delete waiting members; use **Send now** to clear a previous lineup when necessary.

## Saves and removal

Saves retain the lineup, waiting adventurers, pending output, and upgrades. Copies preserve settings and installed upgrades without duplicating adventurers.

Older enabled Auto Send settings migrate to an installed upgrade. Legacy buffers containing more than five members are preserved in a recovery reserve and can be sent in batches of up to five.

Keep this mod installed while loading a kingdom containing Party Nodes. Before uninstalling, send all waiting members, remove the Party Nodes, save the kingdom, and close the game. Then remove `BepInEx/plugins/PartyNode/PartyNode.dll`.

## Building from source

Requires Git and a .NET SDK with .NET Standard 2.1 support. Clone the repository into `_Mods/PartyNode` inside the game installation. From the game directory:

```powershell
git clone https://github.com/urakkaamyx/KingdomInc_PartyNode.git _Mods/PartyNode
dotnet build _Mods/PartyNode/PartyNode.csproj -c Release
```

The project references the local game's `KingdomInc_Data/Managed` and `BepInEx/core` assemblies.

With the game closed, build and install using:

```powershell
& _Mods/PartyNode/install.ps1
```

Run the established isolated diagnostic after building:

```powershell
& _Mods/PartyNode/verify-isolated.ps1
```

The diagnostic uses a separate runtime with a disposable kingdom, blocks saves and achievement triggers, and leaves a normal game session running. Results and screenshots remain local under `Verification/`.

## Credits

Mod code and custom generated artwork by Urakka. Native game UI and class sprites are read from the installed game. BepInEx and Harmony retain their respective authorship and are not bundled.
