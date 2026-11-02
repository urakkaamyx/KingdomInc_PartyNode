using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Behaviour.Flow;
using Assets.Behaviour.UI;
using Assets.Behaviour.UI.TechTree;
using Assets.Source.Flow;
using Assets.Source.Flow.Elements;
using Assets.Source.Items;
using Assets.Source.Items.Data;
using Assets.Source.Player;
using Assets.Source.Util;
using BepInEx;
using HarmonyLib;
using LightJson;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KingdomPartyNode
{
    public sealed partial class PartyNodePlugin
    {
        readonly List<string> checks = new List<string>();
        void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        static Party Counts(params int[] counts)
        {
            var party = new Party();
            for (int i = 0; i < 5; i++) party.SetRequired(i, 0);
            for (int i = 0; i < counts.Length; i++) party.SetRequired(i, counts[i]);
            // Throughput scenarios explicitly install the upgrade when researched.
            if (party.AutoSendUnlocked) party.SetUpgrade(0, new PartyAutoSend());
            ConnectReceiver(party);
            return party;
        }
        static Stockpile ConnectReceiver(Party party, int capacity = 10)
        {
            var receiver = new Stockpile { Capacity = capacity };
            new FlowPipe().Connect(party.Outputs[0], receiver.Inputs[0]);
            return receiver;
        }
        static InventoryItem Member(string type) => new InventoryItem(type);
        static void Arrive(Party party, int archetype, InventoryItem item)
        {
            int slot = Enumerable.Range(0, 5).Where(i => party.GetInputArchetype(i) == archetype && party.Inputs[i].CanBuffer).DefaultIfEmpty(-1).First();
            if (slot < 0 || !party.Inputs[slot].CanAcceptItem(item) || !party.Inputs[slot].InputItem(item))
                throw new InvalidOperationException("Rejected test arrival: " + item.Type.Identifier);
        }
        void ModelChecks()
        {
            var fixture = new GamePlayer(); GamePlayer.Current = fixture;
            fixture.Flows.Add(new FlowDiagram { Label = "Party diagnostic" });
            var tech = TechNode.Get(Party.TechId);
            Check(tech != null && tech.Previous.Identifier == "adventurers_guild" && tech.IsLocked, "Research requires Adventurers' Guild");
            fixture.AddTech("adventurers_guild");
            Check(!tech.IsLocked && tech.IsAvailable, "Research becomes available at guild unlock");
            fixture.AddTech(tech);
            Check(fixture.HasTech(tech) && new Party().IsPurchaseable(fixture), "Research unlocks construction");
            Check(FlowElement.Create(Party.TypeId) is Party, "Native factory creates custom node");
            Check(FlowElement.Create("Stockpile") is Stockpile && FlowUpgrade.Create("Worker") is Assets.Source.Flow.Upgrades.Worker,
                "Native node and upgrade factories still resolve base-game types with custom factories installed");
            var automation = TechNode.Get(Party.AutoSendTechId);
            Check(automation != null && automation.Root == tech && automation.Previous == tech && automation.IsAvailable,
                "Auto Send upgrade is available under Party Node research");
            var manual = Counts(0, 3, 0, 1, 1);
            manual.SetUpgrade(0, new PartyAutoSend());
            Check(!manual.AutoSendEnabled && manual.GetUpgrade(0) == null && manual.UpgradeSlots == 0,
                "New Party Node defaults to manual and has no upgrade slot before research");
            for (int i = 0; i < 3; i++) Arrive(manual, 1, Member("fighter"));
            Arrive(manual, 3, Member("mage")); Arrive(manual, 4, Member("cleric")); manual.Update(1);
            Check(manual.Ready && !manual.Sending && manual.WaitingCount == 5, "Complete five-member lineup waits for Send now by default");
            var oldManualData = manual.ToJson(); oldManualData.AsJsonObject.Remove("AutoSend");
            var oldManual = (Party)FlowElement.FromJson(oldManualData, fixture.Flows[0]); oldManual.Update(1);
            Check(oldManual.Ready && !oldManual.Sending && !oldManual.AutoSendEnabled, "Old saves without mode setting load in manual mode");
            var lockedData = manual.ToJson(); lockedData.AsJsonObject["AutoSend"] = true;
            lockedData.AsJsonObject["Upgrades"] = new JsonArray { new PartyAutoSend().ToJson() };
            var locked = (Party)FlowElement.FromJson(lockedData, fixture.Flows[0]); locked.Update(1);
            Check(locked.Ready && !locked.Sending && !locked.AutoSendEnabled, "Saved upgrade cannot bypass research requirement");
            Check(manual.SendNow() && manual.Outputs[0].BufferedItems.Count == 5, "Default manual button sends completed party");
            fixture.AddTech(automation);
            Check(oldManual.AutoSendUnlocked && !oldManual.AutoSendEnabled && !new Party().AutoSendEnabled,
                "Research unlocks upgrade slot while existing and new nodes stay manual");
            Check(automation.NodeType == TechNodeType.UpgradeSlot && fixture.GetUpgradeSlotCount(Party.TypeId) == 1
                && oldManual.UpgradeSlots == 1 && oldManual.GetAvailableUpgrades().Single() is PartyAutoSend,
                "Research registers Auto Send with native upgrade availability and slot system");
            Check(FlowUpgrade.Create(PartyAutoSend.TypeId) is PartyAutoSend,
                "Native upgrade factory creates Auto Send for copies and saves");
            oldManual.Update(1);
            Check(oldManual.Ready && !oldManual.Sending, "Research alone does not send a waiting manual party");
            oldManual.SetUpgrade(0, new PartyAutoSend());
            Check(oldManual.AutoSendEnabled && !oldManual.GetAvailableUpgrades().Any(), "Installing upgrade enables Auto Send and prevents duplicates");
            ConnectReceiver(oldManual); oldManual.Update(1);
            Check(oldManual.Sending && oldManual.Outputs[0].BufferedItems.Count == 5, "Enabled Auto Send releases complete selected lineup");
            var autoReload = (Party)FlowElement.FromJson(oldManual.ToJson(), fixture.Flows[0]);
            Check(autoReload.AutoSendEnabled && ((Party)oldManual.CreateCopy()).AutoSendEnabled,
                "Installed Auto Send upgrade survives native save/load and copy");
            var disabled = (Party)FlowElement.FromJson(oldManualData, fixture.Flows[0]);
            disabled.SetUpgrade(0, new PartyAutoSend()); disabled.SetUpgrade(0, null); disabled.Update(1);
            Check(disabled.Ready && !disabled.Sending && !disabled.AutoSendEnabled && disabled.GetAvailableUpgrades().Single() is PartyAutoSend,
                "Removing Auto Send returns to manual and makes upgrade available again");
            var legacyAutoData = oldManual.ToJson(); legacyAutoData.AsJsonObject.Remove("Upgrades"); legacyAutoData.AsJsonObject["AutoSend"] = true;
            var legacyAuto = (Party)FlowElement.FromJson(legacyAutoData, fixture.Flows[0]);
            Check(legacyAuto.GetUpgrade(0) is PartyAutoSend && legacyAuto.AutoSendEnabled && !legacyAuto.ToJson().AsJsonObject.ContainsKey("AutoSend"),
                "Earlier enabled popup mode migrates once to an installed native upgrade");

            var party = Counts(0, 2, 0, 1, 1);
            Check(party.Inputs.Count == 5 && party.Outputs.Count == 1, "Exactly five inputs and one output");
            Check(party.GetInputArchetype(4) == -1 && !party.Inputs[4].CanAcceptItem(Member("adventurer")), "Unused party slot is disabled");
            Check(!party.Inputs[1].CanAcceptItem(Member("cleric")) && !party.Inputs[1].CanAcceptItem(Member("conscript")), "Input rejects wrong archetype and non-adventurers");
            Check(Party.Matches(0, Member("adventurer")) && !Party.Matches(0, Member("fighter")) && !Party.Matches(0, Member("conscript")), "None means default Adventurer only");
            var fighter = Member("fighter");
            ((HumanItemData)fighter.Data).Equipment.Add(new InventoryItem("iron_sword"));
            fighter.SetQuality(1.2f);
            var guardian = Member("guardian");
            Check(party.Inputs[1].CanAcceptItem(guardian), "Advanced classes match their base archetype");
            Arrive(party, 1, fighter); Arrive(party, 3, Member("mage")); Arrive(party, 4, Member("cleric"));
            party.Update(1);
            Check(!party.Sending && party.WaitingCount == 3, "Incomplete party cannot auto-send");
            var waitingReload = (Party)FlowElement.FromJson(party.ToJson(), fixture.Flows[0]);
            Check(waitingReload.WaitingCount == 3 && waitingReload.GetRequired(1) == 2 && !waitingReload.Ready, "Waiting party and counts survive save/load");
            var oldLayout = party.ToJson();
            for (int i = 0; i < 5; i++) oldLayout["Inputs"][i].AsJsonObject["Position"] = -10 - 64 * i;
            var migrated = (Party)FlowElement.FromJson(oldLayout, fixture.Flows[0]);
            Check(migrated.Inputs[4].Position == -154 && migrated.WaitingCount == 3 && migrated.GetRequired(1) == 2,
                "Older tall-node saves migrate port positions without losing members or settings");
            Check(((HumanItemData)waitingReload.Inputs[0].NextItem.Data).Equipment.Count == 1 &&
                Math.Abs(waitingReload.Inputs[0].NextItem.Quality - 1.2f) < .001, "Equipment and quality survive save/load");
            Arrive(party, 1, guardian); party.Update(1);
            Check(party.Sending && party.Outputs[0].BufferedItems.Count == 4 && party.WaitingCount == 0, "Complete configured party releases as one batch");
            Check(ReferenceEquals(party.Outputs[0].BufferedItems[0], fighter), "Release preserves original adventurer objects");
            Check(!party.Inputs[1].CanAcceptItem(Member("fighter")) && !party.SendNow(), "Pending party blocks next batch and duplicate manual send");
            var outputReload = (Party)FlowElement.FromJson(party.ToJson(), fixture.Flows[0]);
            Check(outputReload.Sending && outputReload.Outputs[0].BufferedItems.Count == 4 && !outputReload.Inputs[1].CanAcceptItem(Member("fighter")), "Pending output survives save/load without early refill");
            var sink = new Stockpile(); var pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], sink.Inputs[0]);
            pipe.Update(0);
            Check(party.Outputs[0].BufferedItems.Count == 0 && sink.Inputs[0].BufferedItems.Count == 4, "Party pipe admits all members together into receiver queue");
            var received = new List<InventoryItem>();
            while (sink.Inputs[0].HasBuffer) received.Add(sink.Inputs[0].ConsumeItem());
            Check(received.Count == 4 && received.Distinct().Count() == 4 && received.Contains(guardian) && party.Inputs[1].CanAcceptItem(Member("fighter")), "All members leave once and next party can refill");

            party = Counts(0, 5, 0, 0, 0); Arrive(party, 1, fighter);
            Check(!party.Ready && party.SendNow() && party.Outputs[0].BufferedItems.Count == 1 && !party.SendNow(), "Manual Send now releases a partial party exactly once");
            party = Counts(0, 5, 0, 0, 0); Arrive(party, 1, fighter); Arrive(party, 1, guardian);
            party.SetRequired(1, 0); party.Update(1);
            Check(party.WaitingCount + party.RecoveredCount == 2 && !party.Sending && party.SendNow(), "Lowering to zero keeps members recoverable by manual send");
            party = Counts(0, 5, 0, 0, 0); Arrive(party, 1, fighter); Arrive(party, 1, guardian);
            party.SetRequired(1, 1); party.Update(1);
            Check(party.RecoveredCount == 1 && party.Outputs[0].BufferedItems.Count == 1, "Lowering count sends only new requirement and preserves extras");
            party = Counts(0, 0, 0, 0, 0); party.Update(1);
            Check(!party.Sending && !party.SendNow(), "All-zero configuration and empty manual send are inert");
            party.SetRequired(0, -20); party.SetRequired(1, 20);
            Check(party.GetRequired(0) == 0 && party.GetRequired(1) == 5, "Counts clamp to zero through five");
            party = Counts(0, 3, 0, 1, 1);
            Check(Enumerable.Range(0, 5).Select(party.GetInputArchetype).SequenceEqual(new[] { 1, 1, 1, 3, 4 }),
                "Selected lineup maps inputs to Fighter Fighter Fighter Mage Cleric");
            Check(Enumerable.Range(0, 5).All(i => party.Inputs[i].BufferSize == 1), "Every party input holds one member");
            Check(Enumerable.Range(0, 3).All(i => party.Inputs[i].CanAcceptItem(Member("fighter")) && !party.Inputs[i].CanAcceptItem(Member("mage")))
                && party.Inputs[3].CanAcceptItem(Member("mage")) && !party.Inputs[3].CanAcceptItem(Member("fighter"))
                && party.Inputs[4].CanAcceptItem(Member("cleric")) && !party.Inputs[4].CanAcceptItem(Member("mage")),
                "Each lineup arrow accepts only its selected class");
            party.SetRequired(3, 5);
            Check(party.RequiredCount == 5 && party.GetRequired(3) == 1, "Combined required counts cannot exceed five");
            for (int n = 0; n < 3; n++) Arrive(party, 1, Member("fighter"));
            Arrive(party, 3, Member("mage")); party.Update(0);
            Check(!party.Sending && party.WaitingCount == 4, "Three fighters and a mage wait for the selected cleric");
            Arrive(party, 4, Member("cleric")); party.Update(0);
            Check(party.Sending && party.Outputs[0].BufferedItems.Count == 5 && party.WaitingCount == 0
                && party.Outputs[0].BufferedItems.Count(item => Party.Matches(1, item)) == 3,
                "Exact three-fighter one-mage one-cleric party sends only when complete");
            party = Counts(0, 5, 0, 0, 0);
            for (int n = 0; n < 5; n++) Arrive(party, 1, Member("fighter"));
            party.SetRequired(1, 0); party.SetRequired(3, 5);
            Check(!party.Inputs[3].CanAcceptItem(Member("mage")) && party.WaitingCount + party.RecoveredCount == 5,
                "Changing composition cannot accumulate more than five active members");
            Check(party.SendNow() && party.Outputs[0].BufferedItems.Count == 5, "Manual sending clears preserved members after composition changes");
            party = Counts(1, 1, 1, 1, 1);
            var ids = new[] { "adventurer", "fighter", "rogue", "mage", "cleric" };
            for (int i = 0; i < 5; i++) Arrive(party, i, Member(ids[i]));
            party.Update(0);
            Check(party.Outputs[0].BufferedItems.Count == 5 && party.WaitingCount == 0, "Maximum party is five members total");
            Check(party.Outputs[0].BufferedItems.Take(5).Select(x => x.Type.Identifier).SequenceEqual(ids), "Batch output follows top-to-bottom lineup order");
            var copy = (Party)party.CreateCopy();
            Check(Enumerable.Range(0, 5).All(i => copy.GetRequired(i) == 1) && copy.WaitingCount == 0 && !copy.Sending, "Copy carries settings without duplicating members");
            fixture.Flows[0].Elements.Add(party);
            var restored = GamePlayer.FromJson(fixture.ToJson());
            Check(restored.HasTech(tech) && restored.Flows[0].Elements[0] is Party saved && saved.Sending && saved.Outputs[0].BufferedItems.Count == 5, "Full kingdom JSON preserves research and custom pending party");
            var oldResearchData = fixture.ToJson();
            oldResearchData["UpgradeCounts"].AsJsonObject.Remove(Party.TypeId);
            oldResearchData["UnlockedFlowUpgrades"].AsJsonObject.Remove(Party.TypeId);
            var oldResearchPlayer = GamePlayer.FromJson(oldResearchData);
            Check(oldResearchPlayer.GetUpgradeSlotCount(Party.TypeId) == 1
                && oldResearchPlayer.GetAvailableFlowUpgrades(Party.TypeId).Contains(PartyAutoSend.TypeId),
                "Previously researched saves gain native slot and upgrade registry on load");
            var legacyJson = Counts(1, 1, 1, 1, 1).ToJson();
            legacyJson.AsJsonObject["PartyRequirements"] = new JsonArray { 5, 5, 5, 5, 5 };
            for (int i = 0; i < 5; i++)
            {
                var members = new JsonArray(); for (int n = 0; n < 5; n++) members.Add(Member(ids[i]).ToJson());
                legacyJson["Inputs"][i].AsJsonObject["BufferedItems"] = members;
            }
            var legacy = (Party)FlowElement.FromJson(legacyJson, fixture.Flows[0]);
            ConnectReceiver(legacy);
            Check(legacy.RequiredCount == 5 && legacy.WaitingCount == 5 && legacy.RecoveredCount == 20,
                "Old oversized configurations preserve surplus members outside active party");
            Check(legacy.SendNow() && legacy.Outputs[0].BufferedItems.Count == 5, "Legacy recovery sends batches of at most five");
            legacy = (Party)FlowElement.FromJson(legacy.ToJson(), fixture.Flows[0]);
            ConnectReceiver(legacy);
            Check(legacy.RecoveredCount == 20 && legacy.Outputs[0].BufferedItems.Count == 5, "Legacy recovery reserve survives save/load");
            int delivered = 0;
            do
            {
                Check(legacy.Outputs[0].BufferedItems.Count <= 5, "Recovered batch stays within five-member limit");
                delivered += legacy.Outputs[0].BufferedItems.Count; legacy.Outputs[0].BufferedItems.Clear();
            } while (legacy.SendNow());
            Check(delivered == 25 && legacy.RecoveredCount == 0, "All members from old oversized save are recoverable without loss");
            DeliveryChecks();
        }

        void DeliveryChecks()
        {
            var party = Counts(0, 3, 0, 1, 1);
            for (int i = 0; i < 3; i++) Arrive(party, 1, Member("fighter"));
            Arrive(party, 3, Member("mage")); Arrive(party, 4, Member("cleric"));
            var originals = party.Inputs.SelectMany(i => i.BufferedItems).ToArray();
            var receiver = ConnectReceiver(party, 5);
            receiver.Contents.Add(Member("fighter")); party.Update(0);
            Check(party.Ready && !party.Sending && !party.SendNow() && !party.CanSendNow && party.WaitingCount == 5,
                "Auto and manual sending both hold five members when receiver has only four spaces");
            receiver.Contents.Clear(); receiver.Inputs[0].BufferedItems.Add(Member("fighter")); party.Update(0);
            Check(!party.Sending && party.WaitingCount == 5, "Receiver input queue counts against available internal capacity");
            receiver.Inputs[0].BufferedItems.Clear(); party.Update(0);
            Check(party.Sending && party.Outputs[0].BufferedItems.SequenceEqual(originals), "Auto Send resumes when room for the whole party opens");
            receiver.Contents.Add(Member("fighter")); party.Outputs[0].ConnectedPipe.Update(0);
            Check(party.Outputs[0].BufferedItems.Count == 5 && receiver.Inputs[0].BufferedItems.Count == 0,
                "Capacity is rechecked at transfer and a newly blocked receiver gets no partial party");
            receiver.Contents.Clear(); party.Outputs[0].ConnectedPipe.Update(0);
            Check(!party.Sending && receiver.Inputs[0].BufferedItems.SequenceEqual(originals),
                "Full party transfers once in original lineup order when all spaces are free");
            for (int i = 0; i < 5; i++) receiver.Update(0);
            Check(receiver.Contents.Count + receiver.Outputs[0].BufferedItems.Count == 5 && !receiver.Inputs[0].HasBuffer,
                "Native storage consumes admitted queue without losing party members");
            party = Counts(0, 3, 0, 1, 1); Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
            receiver = ConnectReceiver(party, 2);
            Check(party.SendNow() && party.Outputs[0].BufferedItems.Count == 2, "Manual partial party only needs room for the actual two members sent");
            party = Counts(0, 1, 0, 0, 0); Arrive(party, 1, Member("fighter"));
            var connection = party.Outputs[0].ConnectedPipe; connection.ConnectedInput.ConnectedPipe = null; party.Outputs[0].ConnectedPipe = null;
            party.Update(0);
            Check(!party.SendNow() && !party.Sending && party.WaitingCount == 1 && party.SendBlockReason == "Connect output",
                "Disconnected output cannot manually or automatically release adventurers");
            receiver = ConnectReceiver(party, 0); receiver.Contents.Add(Member("fighter")); party.Update(0);
            Check(!party.Sending && !party.CanSendNow, "Overfilled or zero-capacity receiver remains blocked");

            party = Counts(0, 3, 0, 1, 1);
            for (int i = 0; i < 3; i++) Arrive(party, 1, Member("fighter"));
            Arrive(party, 3, Member("mage")); Arrive(party, 4, Member("cleric"));
            var dungeon = new Dungeon(); var pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], dungeon.Inputs[0]);
            dungeon.Inputs[4].BufferedItems.Add(Member("cleric")); party.Update(0);
            Check(party.WaitingCount == 5 && !party.Sending, "Dungeon must have five free adventurer slots before five-member release");
            dungeon.Inputs[4].BufferedItems.Clear(); party.Update(0); pipe.Update(0);
            Check(!party.Sending && dungeon.Inputs.All(i => i.BufferedItems.Count == 1)
                && dungeon.Inputs.Select(i => i.NextItem.Type.Identifier).SequenceEqual(new[] { "fighter", "fighter", "fighter", "mage", "cleric" }),
                "One party connection fills dungeon's five free slots together in lineup order");

            party = Counts(0, 2, 0, 1, 0); Arrive(party, 1, Member("fighter")); Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
            var targetParty = Counts(0, 2, 0, 1, 0);
            pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], targetParty.Inputs[0]); party.Update(0); pipe.Update(0);
            Check(!party.Sending && targetParty.WaitingCount == 3 && targetParty.Ready,
                "Party receiver uses compatible individual lineup slots without exceeding five members");
            party = Counts(0, 2, 0, 0, 0); Arrive(party, 1, Member("fighter")); Arrive(party, 1, Member("fighter"));
            dungeon = new Dungeon();
            for (int i = 2; i < 5; i++) dungeon.Inputs[i].BufferedItems.Add(Member("cleric"));
            var other = new Stockpile(); new FlowPipe().Connect(other.Outputs[0], dungeon.Inputs[1]);
            pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], dungeon.Inputs[0]); party.Update(0);
            Check(!party.Sending && party.WaitingCount == 2, "Receiver slots wired to other sources are not borrowed by a party");

            party = Counts(0, 2, 0, 1, 0); Arrive(party, 1, Member("fighter")); Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
            var inn = new Inn();
            for (int i = 0; i < 3; i++) inn.Contents[i] = new InnSlot(inn) { Human = Member("fighter") };
            pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], inn.Inputs[0]); party.Update(0);
            Check(!party.Sending && party.WaitingCount == 3, "Inn checks occupied beds rather than just its five-item input buffer");
            inn.Contents[2] = null; party.Update(0); pipe.Update(0);
            Check(inn.Inputs[0].BufferedItems.Count == 3 && !party.Sending, "Inn accepts whole party once enough beds are empty");
            party = Counts(0, 3, 0, 0, 0); for (int i = 0; i < 3; i++) Arrive(party, 1, Member("fighter"));
            var quest = new QuestBoard(); quest.Contents.Add((HumanItemData)Member("fighter").Data); quest.Contents.Add((HumanItemData)Member("mage").Data);
            pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], quest.Inputs[0]); party.Update(0); pipe.Update(0);
            Check(quest.Inputs[0].BufferedItems.Count == 3 && quest.Contents.Count == 2 && !party.Sending,
                "Quest Board's roster and queued arrivals share its five-member capacity");
            party = Counts(0, 4, 0, 0, 0); for (int i = 0; i < 4; i++) Arrive(party, 1, Member("fighter"));
            var training = new TrainingGrounds(); pipe = new FlowPipe(); pipe.Connect(party.Outputs[0], training.Inputs[0]); party.Update(0);
            Check(training.Capacity == 3 && !party.Sending && party.WaitingCount == 4,
                "Training Grounds respects researched capacity rather than its maximum five-slot array");

            party = Counts(0, 1, 0, 1, 0); Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
            receiver = ConnectReceiver(party, 5); receiver.Inputs[0].Filter = item => item.Type.Identifier == "fighter"; party.Update(0);
            Check(!party.SendNow() && !party.Sending && party.WaitingCount == 2,
                "Every member must pass receiver filter before anyone is released");
            receiver.Inputs[0].Filter = item => true; party.Update(0);
            var pending = (Party)FlowElement.FromJson(party.ToJson(), GamePlayer.Current.Flows[0]); receiver = ConnectReceiver(pending, 1);
            pending.Outputs[0].ConnectedPipe.Update(0);
            Check(pending.Sending && pending.Outputs[0].BufferedItems.Count == 2 && !receiver.Inputs[0].HasBuffer,
                "Saved pending parties remain whole when loaded onto a receiver without enough room");
            receiver.Capacity = 2; pending.Outputs[0].ConnectedPipe.Update(0);
            var receiverReload = (Stockpile)FlowElement.FromJson(receiver.ToJson(), GamePlayer.Current.Flows[0]);
            Check(receiverReload.Capacity == 2 && receiverReload.Inputs[0].BufferedItems.Count == 2
                && receiverReload.Inputs[0].BufferedItems.Select(m => m.Type.Identifier).SequenceEqual(new[] { "fighter", "mage" }),
                "An admitted whole-party input queue survives receiver save/load");
            var firstParty = Counts(0, 3, 0, 0, 0); var secondParty = Counts(0, 3, 0, 0, 0);
            for (int i = 0; i < 3; i++) { Arrive(firstParty, 1, Member("fighter")); Arrive(secondParty, 1, Member("fighter")); }
            var shared = new Dungeon();
            var firstPipe = new FlowPipe(); firstPipe.Connect(firstParty.Outputs[0], shared.Inputs[0]);
            var secondPipe = new FlowPipe(); secondPipe.Connect(secondParty.Outputs[0], shared.Inputs[1]);
            firstParty.Update(0); secondParty.Update(0); firstPipe.Update(0); secondPipe.Update(0);
            Check(shared.Inputs.Sum(i => i.BufferedItems.Count) == 3 && !firstParty.Sending && secondParty.Outputs[0].BufferedItems.Count == 3,
                "Competing parties cannot overbook receiver slots or transfer only part of the second batch");
        }

        IEnumerator RuntimeCheck()
        {
            yield return new WaitForSecondsRealtime(5);
            string directory = Path.Combine(Paths.GameRootPath, "_Mods", "PartyNode", "Verification");
            Directory.CreateDirectory(directory);
            string error = null;
            try
            {
                if (GameUI.Instance || GamePlayer.Current != null) throw new InvalidOperationException("Diagnostic requires main menu with no loaded kingdom");
                ModelChecks();
                var fixture = GamePlayer.CreateNewPlayer();
                fixture.Flows[0].Elements.Clear(); fixture.Flows[0].Pipes.Clear();
                fixture.Flows[0].Label = "Party Node verification";
                fixture.AddTech("adventurers_guild"); fixture.AddTech(Party.TechId);
                var party = Counts(0, 3, 0, 1, 1);
                party.Position = new Vector2Int(-Party.Width / 2, Party.Height / 2);
                Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
                fixture.Flows[0].Elements.Add(party);
                SceneManager.LoadScene("Game");
            }
            catch (Exception ex) { error = ex.ToString(); }
            if (error == null)
            {
                yield return new WaitForSecondsRealtime(4);
                try
                {
                    var party = (Party)GamePlayer.Current.Flows[0].Elements.Single();
                    WorldFlowCamera.Instance.SetZoomLevel(3);
                    Check(party.World && party.World.GetComponent<PartyWorld>(), "Custom prefab renders in actual game scene");
                    Check(party.World.GetComponentsInChildren<WorldFlowIO>().Length == 6, "World node renders all six ports");
                    party.World.UpdateIO();
                    var portImage = AccessTools.Field(typeof(WorldFlowIO), "_sprite");
                    Check(party.Inputs.All(input => ((Image)portImage.GetValue(input.World)).sprite.name == "IOGreen_" + (input.HasBuffer ? "0" : "2"))
                        && ((Image)portImage.GetValue(party.Outputs[0].World)).sprite.name == "IOBlue_2",
                        "Freshly rebuilt inputs are green and empty output is blue before first update");
                    var badges = party.World.GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("InputClass_"))
                        .OrderBy(image => image.name).ToArray();
                    Check(badges.Length == 5 && badges.Select(b => b.sprite).SequenceEqual(new[] {
                        SpriteLibrary.Get(Party.Icons[1]), SpriteLibrary.Get(Party.Icons[1]), SpriteLibrary.Get(Party.Icons[1]),
                        SpriteLibrary.Get(Party.Icons[3]), SpriteLibrary.Get(Party.Icons[4]) }), "Input class indicators match selected lineup");
                    Check(FlowElement.GetBuildableTypes().Count(e => e.ElementType == Party.TypeId) == 1, "Build catalog includes exactly one unlocked Party Node");
                    var view = party.World.GetComponent<PartyWorld>(); view.Refresh();
                    Check(party.World.Size == new Vector2(384, 224), "Workspace footprint leaves room for upgrade, status and Send controls");
                    Check(TechNode.Get(Party.TechId).Icon == PartyArtwork.Sprite && party.Icon == PartyArtwork.RoomSprite
                        && PartyArtwork.Sprite != PartyArtwork.RoomSprite, "Research keeps populated art while workspace uses empty art");
                    Check(view.VisibleMemberCount == 2 && view.VisibleMembers.All(item => party.Inputs.Any(input => input.BufferedItems.Contains(item))),
                        "Only actual buffered members appear in room");
                    var first = view.VisibleMembers.First(); var before = view.MemberPosition(first); view.TickWalkers(.5f);
                    Check(view.MemberPosition(first) != before, "Available members walk around the map table");
                    var buttons = party.World.GetComponentsInChildren<Button>().Where(b => b.enabled).ToArray();
                    Check(buttons.Length == 2 && buttons.Any(b => b.name == "PartySettingsGear"), "Workspace has gear and Send now without count controls");
                    var receiver = (Stockpile)party.Outputs[0].ConnectedPipe.ConnectedInput.Parent;
                    receiver.Capacity = 1; view.Refresh();
                    Check(!buttons.Single(b => b.name == "Send nowButton").interactable,
                        "Workspace Send button is disabled while receiver cannot fit the whole waiting party");
                    Capture(Path.Combine(directory, "party-destination-blocked.png"));
                    bool inputWasEnabled = Assets.Behaviour.Input.PlayerInput.Enabled;
                    buttons.Single(b => b.name == "PartySettingsGear").onClick.Invoke();
                    Check(view.Settings && view.Settings.IsOpen && !Assets.Behaviour.Input.PlayerInput.Enabled, "Gear opens popup and suspends workshop shortcuts");
                    var controls = view.Settings.GetComponentsInChildren<Button>();
                    Check(!controls.Single(b => b.name == "Send nowButton").interactable,
                        "Configuration Send button also respects whole-party receiver capacity");
                    receiver.Capacity = 10; view.Refresh(); view.Settings.Refresh();
                    Check(buttons.Single(b => b.name == "Send nowButton").interactable && controls.Single(b => b.name == "Send nowButton").interactable,
                        "Both Send buttons become available again when receiver space opens");
                    Check(!controls.Any(b => b.name == "AutoSendMode") && !party.AutoSendEnabled,
                        "Configuration popup contains lineup controls without automation toggle");
                    Capture(Path.Combine(directory, "party-settings-locked.png"));
                    GamePlayer.Current.AddTech(Party.AutoSendTechId); view.Settings.Refresh();
                    var slot = party.World.GetComponentsInChildren<WorldFlowUpgrade>().Single();
                    Check(slot.Slot == 0 && slot.Upgrade == null && slot.Parent == party.World && !party.AutoSendEnabled
                        && slot.GetComponentsInChildren<Graphic>().Any(g => g.isActiveAndEnabled && g.raycastTarget),
                        "Research creates an interactive native plus upgrade slot on Party Node");
                    var add = controls.Where(b => b.name == "+Button").ToArray();
                    var remove = controls.Where(b => b.name == "−Button").ToArray();
                    Check(add.All(button => !button.interactable), "Popup disables additions when five total slots are selected");
                    remove[1].onClick.Invoke();
                    var plus = add[0]; var minus = remove[0];
                    plus.onClick.Invoke(); Check(party.GetRequired(0) == 1, "Popup plus button updates required count");
                    minus.onClick.Invoke(); Check(party.GetRequired(0) == 0, "Popup minus button updates required count");
                    add[1].onClick.Invoke();
                    controls.Single(b => b.name == "Send nowButton").onClick.Invoke();
                    Check(party.Sending && party.Outputs[0].BufferedItems.Count == 2 && view.VisibleMemberCount == 2,
                        "Popup manual sending keeps pending output members visible");
                    party.World.UpdateIO();
                    Check(party.Inputs.All(input => ((Image)portImage.GetValue(input.World)).sprite.name == "IOGreen_2")
                        && ((Image)portImage.GetValue(party.Outputs[0].World)).sprite.name == "IOBlue_0",
                        "Sending party keeps empty inputs green and buffered output blue on rebuild");
                    GameUI.Instance.ProcessEscape();
                    Check(!view.Settings.IsOpen && Assets.Behaviour.Input.PlayerInput.Enabled == inputWasEnabled, "Escape closes settings and restores workshop shortcuts");
                    party.Outputs[0].BufferedItems.Clear();
                    view.Refresh(); Check(view.VisibleMemberCount == 0, "Departed members disappear and empty room contains no actors");
                    Capture(Path.Combine(directory, "party-node-empty.png"));
                    Arrive(party, 1, Member("fighter")); Arrive(party, 3, Member("mage"));
                    view.Refresh(); view.TickWalkers(.5f);
                    Check(view.VisibleMemberCount == 2, "New arrivals appear again without phantom members");
                    GameUI.Instance.CloseAllExcept(null);
                    WorldFlowCamera.Instance.SetZoomLevel(3);
                    slot.GetComponentInChildren<Button>().onClick.Invoke();
                }
                catch (Exception ex) { error = ex.ToString(); }
            }
            if (error == null)
            {
                yield return new WaitForSecondsRealtime(1);
                try
                {
                    Capture(Path.Combine(directory, "party-upgrade-menu.png"));
                    var party = (Party)GamePlayer.Current.Flows[0].Elements.Single();
                    var slot = party.World.GetComponentsInChildren<WorldFlowUpgrade>().Single();
                    var options = slot.GetComponentsInChildren<WorldFlowUpgradeOption>();
                    Check(options.Length == 2, "Native plus menu shows Remove and Auto Send options");
                    var option = options.Single(o => AccessTools.Field(typeof(WorldFlowUpgradeOption), "_upgrade").GetValue(o) is PartyAutoSend);
                    var upgrade = new PartyAutoSend();
                    GamePlayer.Current.AddCurrency(upgrade.GetPurchaseCost(party), isIncome: false);
                    option.SelectUpgrade();
                    Check(party.GetUpgrade(0) is PartyAutoSend && party.AutoSendEnabled, "Native upgrade option purchases and installs Auto Send");
                    GameUI.Instance.CloseAllExcept(null);
                    Capture(Path.Combine(directory, "party-node-upgraded.png"));
                    slot = party.World.GetComponentsInChildren<WorldFlowUpgrade>().Single(s => s.Upgrade is PartyAutoSend);
                    slot.ToggleOptionSelect(false);
                    options = slot.GetComponentsInChildren<WorldFlowUpgradeOption>();
                    var remove = options.Single(o => AccessTools.Field(typeof(WorldFlowUpgradeOption), "_upgrade").GetValue(o) == null);
                    remove.SelectUpgrade();
                    Check(party.GetUpgrade(0) == null && !party.AutoSendEnabled && party.WaitingCount == 2,
                        "Native Remove option restores manual mode without losing waiting members");
                    GameUI.Instance.CloseAllExcept(null);
                    Capture(Path.Combine(directory, "party-node.png"));
                    party.World.GetComponent<PartyWorld>().OpenSettings();
                }
                catch (Exception ex) { error = ex.ToString(); }
            }
            if (error == null)
            {
                yield return new WaitForSecondsRealtime(.5f);
                try
                {
                    Capture(Path.Combine(directory, "party-settings.png"));
                    GameUI.Instance.ProcessCancel();
                    var party = (Party)GamePlayer.Current.Flows[0].Elements.Single();
                    Check(!party.World.GetComponent<PartyWorld>().Settings.IsOpen, "Right-click cancel closes configuration popup");
                    GameUI.Instance.ToggleTechMenu(true);
                    TechTreeMenu.Instance.ShowTechTree(null, true);
                    Check(TechTreeMenu.Instance.Nodes.Any(n => n.Tech.Identifier == Party.TechId), "Party research renders in actual tech tree");
                }
                catch (Exception ex) { error = ex.ToString(); }
            }
            if (error == null)
            {
                yield return new WaitForSecondsRealtime(1);
                // The menu's first Start restores its remembered camera position.
                // Focus only after that frame has completed.
                TechTreeMenu.Instance.FocusNode(TechNode.Get(Party.TechId));
                yield return new WaitForSecondsRealtime(.5f);
                try
                {
                    var node = TechTreeMenu.Instance.Nodes.Single(n => n.Tech.Identifier == Party.TechId);
                    var camera = AccessTools.Field(typeof(TechTreeMenu), "_camera").GetValue(TechTreeMenu.Instance) as Camera;
                    var position = camera.WorldToViewportPoint(node.transform.position);
                    Check(position.x > 0 && position.x < 1 && position.y > 0 && position.y < 1, "Party research is visible at its focused camera position");
                    Capture(Path.Combine(directory, "party-research.png"));
                    TechTreeMenu.Instance.ShowTechTree(TechNode.Get(Party.TechId), true);
                    Check(TechTreeMenu.Instance.Nodes.Any(n => n.Tech.Identifier == Party.AutoSendTechId), "Auto Send upgrade renders in Party Node research subtree");
                }
                catch (Exception ex) { error = ex.ToString(); }
            }
            if (error == null)
            {
                yield return new WaitForSecondsRealtime(.5f);
                try { Capture(Path.Combine(directory, "party-auto-send-research.png")); }
                catch (Exception ex) { error = ex.ToString(); }
            }
            var result = new JsonObject { { "passed", error == null }, { "savedKingdomLoaded", false }, { "saveWritesBlocked", true }, { "testedAtUtc", DateTime.UtcNow.ToString("O") } };
            var names = new JsonArray(); foreach (string check in checks) names.Add(check); result["checks"] = names;
            if (error != null) result["error"] = error;
            File.WriteAllText(Path.Combine(directory, "runtime-tests.json"), result.ToString(true));
            if (error == null) Logger.LogInfo("PARTY_NODE_RUNTIME_PASS: " + checks.Count + " checks");
            else Logger.LogError("PARTY_NODE_RUNTIME_FAIL: " + error);
            yield return new WaitForSecondsRealtime(1);
            Application.Quit();
        }
        static void Capture(string path)
        {
            // Hidden Windows players can suspend presentation and never resume
            // WaitForEndOfFrame. Render cameras explicitly to an offscreen target.
            Canvas.ForceUpdateCanvases();
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            // Render only this popup as a temporary overlay root. Moving all UI
            // roots would also move native camera/mask objects out of their layers.
            var popup = UnityEngine.Object.FindObjectsByType<PartySettingsPopup>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOpen);
            var popupRoot = popup ? (RectTransform)popup.transform : null;
            var parent = popupRoot ? popupRoot.parent : null;
            var rootCanvas = parent ? parent.GetComponentInParent<Canvas>().rootCanvas : null;
            var popupCanvas = popup ? popup.GetComponent<Canvas>() : null;
            var oldMode = popupCanvas ? popupCanvas.renderMode : RenderMode.ScreenSpaceOverlay;
            var oldCamera = popupCanvas ? popupCanvas.worldCamera : null;
            float oldDistance = popupCanvas ? popupCanvas.planeDistance : 0;
            float oldScaleFactor = popupCanvas ? popupCanvas.scaleFactor : 1;
            var oldAnchorMin = popupRoot ? popupRoot.anchorMin : Vector2.zero;
            var oldAnchorMax = popupRoot ? popupRoot.anchorMax : Vector2.one;
            var oldPivot = popupRoot ? popupRoot.pivot : Vector2.zero;
            var oldSize = popupRoot ? popupRoot.sizeDelta : Vector2.zero;
            var oldPosition = popupRoot ? popupRoot.anchoredPosition : Vector2.zero;
            var oldScale = popupRoot ? popupRoot.localScale : Vector3.one;
            var layers = popup ? popup.GetComponentsInChildren<Transform>(true)
                .Select(t => (transform: t, layer: t.gameObject.layer)).ToArray() : new (Transform transform, int layer)[0];
            var overlayCameraObject = new GameObject("PartyDiagnosticOverlayCapture");
            var overlayCamera = overlayCameraObject.AddComponent<Camera>();
            overlayCamera.clearFlags = CameraClearFlags.Depth; overlayCamera.orthographic = true;
            overlayCamera.orthographicSize = Screen.height / 2f; overlayCamera.depth = 10000;
            overlayCamera.cullingMask = 1 << 31; overlayCamera.targetTexture = target;
            try
            {
                foreach (var camera in Camera.allCameras.Where(c => c != overlayCamera).OrderBy(c => c.depth))
                {
                    var oldTarget = camera.targetTexture;
                    try { camera.targetTexture = target; camera.Render(); }
                    finally { camera.targetTexture = oldTarget; }
                }
                if (popup)
                {
                    foreach (var entry in layers) entry.transform.gameObject.layer = 31;
                    float scale = rootCanvas.scaleFactor;
                    popupRoot.SetParent(null, false);
                    popupCanvas.renderMode = RenderMode.ScreenSpaceCamera; popupCanvas.worldCamera = overlayCamera;
                    popupCanvas.planeDistance = 10; popupCanvas.scaleFactor = scale;
                    Canvas.ForceUpdateCanvases(); overlayCamera.Render();
                }
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                if (popup)
                {
                    popupCanvas.renderMode = oldMode; popupCanvas.worldCamera = oldCamera;
                    popupCanvas.planeDistance = oldDistance; popupCanvas.scaleFactor = oldScaleFactor;
                    popupRoot.SetParent(parent, false); popupRoot.anchorMin = oldAnchorMin; popupRoot.anchorMax = oldAnchorMax;
                    popupRoot.pivot = oldPivot; popupRoot.sizeDelta = oldSize; popupRoot.anchoredPosition = oldPosition; popupRoot.localScale = oldScale;
                }
                foreach (var entry in layers) if (entry.transform) entry.transform.gameObject.layer = entry.layer;
                UnityEngine.Object.DestroyImmediate(overlayCameraObject);
                UnityEngine.Object.Destroy(texture);
            }
        }

        // These guards only activate for the explicit diagnostic command line.
        // No user kingdom is loaded, saved or awarded achievements by verification.
        [HarmonyPatch(typeof(SaveGame), nameof(SaveGame.Store))]
        static class BlockDiagnosticSaves { static bool Prefix() => !Diagnostic; }
        [HarmonyPatch(typeof(SaveGame), nameof(SaveGame.StoreAutosaveState))]
        static class BlockDiagnosticAutosaves { static bool Prefix() => !Diagnostic; }
        [HarmonyPatch(typeof(SaveGame), nameof(SaveGame.LoadLatestSave))]
        static class BlockDiagnosticLoads { static bool Prefix(ref bool __result) { if (!Diagnostic) return true; __result = false; return false; } }
        [HarmonyPatch(typeof(SteamAchievement), nameof(SteamAchievement.Trigger))]
        static class BlockDiagnosticAchievements { static bool Prefix() => !Diagnostic; }
        [HarmonyPatch(typeof(SteamManager), nameof(SteamManager.Initialized), MethodType.Getter)]
        static class OfflineDiagnostic
        {
            static bool Prefix(ref bool __result) { if (!Diagnostic) return true; __result = false; return false; }
        }
    }
}
