using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Source.Adventure;
using Assets.Source.Flow;
using Assets.Source.Items;
using Assets.Source.Items.Type;
using Assets.Source.Player;
using LightJson;
using UnityEngine;

namespace KingdomPartyNode
{
    public sealed class Party : FlowElement
    {
        public const string TypeId = "Party", TechId = "party_node", AutoSendTechId = "party_node_auto_send";
        public const int SlotCount = 5, MaxPerArchetype = 5;
        public const int MaxPartySize = 5;
        public const int Width = 384, Height = 224;
        public static readonly string[] Names = { "Adventurer", "Fighter", "Rogue", "Mage", "Cleric" };
        public static readonly string[] Icons = { "Icons_139", "Icons_170", "Icons_169", "Icons_171", "Icons_172" };
        static readonly string[] ItemIds = { "adventurer", "fighter", "rogue", "mage", "cleric" };
        readonly int[] required = { 1, 1, 1, 1, 1 };
        // Preserve members from earlier versions that allowed oversized buffers.
        readonly List<InventoryItem> recovered = new List<InventoryItem>();
        bool legacyAutoSend;

        public override string ElementType => TypeId;
        public override string Label => "Party Node";
        public override string IconName => PartyArtwork.RoomIconId;
        public override ElementCategory Category => ElementCategory.Utility;
        public override bool HasCraftButton => false;
        public override int UpgradeSlots => AutoSendUnlocked ? 1 : 0;
        public int WaitingCount => Inputs.Sum(input => input.BufferedItems.Count);
        public int RequiredCount => required.Sum();
        public int RemainingSlots => MaxPartySize - RequiredCount;
        public int RecoveredCount => recovered.Count;
        public bool Sending => Outputs[0].HasBuffer;
        public bool AutoSendUnlocked => GamePlayer.Current?.HasTech(AutoSendTechId) == true;
        public bool AutoSendEnabled => AutoSendUnlocked && HasUpgrade<PartyAutoSend>();
        public bool CanSendNow => !Sending && PartyDelivery.Plan(Outputs[0], ReleaseBatch(true), out _, out _);
        public string SendBlockReason
        {
            get
            {
                if (Sending) return null;
                PartyDelivery.Plan(Outputs[0], ReleaseBatch(true), out _, out var reason);
                return reason;
            }
        }

        public Party()
        {
            RequiredTech = TechId;
            Description = "Assemble a party of up to 5 adventurers total. Set each archetype's required count; 0 disables it. " +
                "Press Send now to release the party. Research Auto Send, then install it in the node's + upgrade slot to automate complete lineups.";
            CostCurrency = CurrencyCollection.GoldMedal;
            CostMultiplier = 0.5;
            Size = new Vector2Int(Width, Height);
            Outputs.Add(new FlowOutput(this, 0) {
                BufferSize = MaxPartySize, Position = 10,
                Filter = item => item?.Type is AdventurerItemType, FilterPreview = true
            });
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = i;
                Inputs.Add(new FlowInput(this, i) {
                    Position = -10 - 36 * i, BufferSize = 1, FilterPreview = true,
                    // Preview describes the archetype even when disabled or sending.
                    Filter = item => Matches(GetInputArchetype(slot), item)
                });
            }
            RebuildLineup();
        }

        public static bool Matches(int slot, InventoryItem item) =>
            item?.Type is AdventurerItemType type && (int)type.Archetype == slot;

        public int GetRequired(int slot) => required[slot];
        public int GetInputArchetype(int port)
        {
            if (port < 0 || port >= SlotCount) return -1;
            for (int archetype = 0; archetype < SlotCount; archetype++)
            {
                if (port < required[archetype]) return archetype;
                port -= required[archetype];
            }
            return -1;
        }
        public int GetWaiting(int archetype) => Inputs.Sum(input => input.BufferedItems.Count(item => Matches(archetype, item)))
            + recovered.Count(item => Matches(archetype, item));
        void RebuildLineup()
        {
            var members = Inputs.SelectMany(input => input.BufferedItems).Concat(recovered).ToList();
            foreach (var input in Inputs) input.BufferedItems.Clear();
            recovered.Clear();
            for (int port = 0; port < SlotCount; port++)
            {
                int archetype = GetInputArchetype(port);
                Inputs[port].BufferSize = archetype >= 0 ? 1 : 0;
                Inputs[port].Item = archetype >= 0 ? ItemType.Get(ItemIds[archetype]) : null;
                Inputs[port].FilterPreview = archetype >= 0;
            }
            int budget = Math.Max(0, MaxPartySize - Outputs[0].BufferedItems.Count);
            foreach (var member in members)
            {
                var input = Inputs.FirstOrDefault(slot => slot.CanBuffer && Matches(GetInputArchetype(slot.ParentIndex), member));
                if (input != null && budget > 0) { input.BufferedItems.Add(member); budget--; }
                else recovered.Add(member);
            }
        }
        internal void NormalizePorts()
        {
            for (int i = 0; i < SlotCount; i++) Inputs[i].Position = -10 - 36 * i;
            Outputs[0].Position = 10;
        }
        internal void NormalizeLoadedMembers()
        {
            while (Outputs[0].BufferedItems.Count > MaxPartySize)
            {
                recovered.Add(Outputs[0].BufferedItems[MaxPartySize]);
                Outputs[0].BufferedItems.RemoveAt(MaxPartySize);
            }
            int budget = MaxPartySize - Outputs[0].BufferedItems.Count;
            foreach (var input in Inputs)
            {
                int keep = Math.Min(budget, input.BufferedItems.Count); budget -= keep;
                while (input.BufferedItems.Count > keep)
                {
                    recovered.Add(input.BufferedItems[keep]); input.BufferedItems.RemoveAt(keep);
                }
            }
            RebuildLineup();
        }
        internal void MigrateLegacyUpgrade()
        {
            // Preserve already-enabled automation from the earlier popup version.
            if (legacyAutoSend && AutoSendUnlocked && GetUpgrade(0) == null)
                SetUpgrade(0, new PartyAutoSend());
            legacyAutoSend = false;
        }
        public void SetRequired(int slot, int count)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            int otherSlots = RequiredCount - required[slot];
            required[slot] = Math.Max(0, Math.Min(MaxPartySize - otherSlots, count));
            RebuildLineup();
            // Existing adventurers stay buffered if the limit is lowered.
        }

        public bool CanReceive(int slot, InventoryItem item) =>
            !Sending && recovered.Count == 0 && WaitingCount < MaxPartySize && Matches(GetInputArchetype(slot), item);

        public bool Ready => !Sending && RequiredCount > 0 &&
            Enumerable.Range(0, RequiredCount).All(port => Inputs[port].HasBuffer && Matches(GetInputArchetype(port), Inputs[port].NextItem));

        public override void Update(float delta)
        {
            if (!Sending && recovered.Count > 0) RebuildLineup();
            if (AutoSendEnabled && Ready) Release(false);
        }

        public bool SendNow() => Release(true);
        List<InventoryItem> ReleaseBatch(bool manual)
        {
            var batch = Inputs.Take(manual ? SlotCount : RequiredCount).SelectMany(input => input.BufferedItems);
            if (manual) batch = batch.Concat(recovered);
            return batch.Take(MaxPartySize).ToList();
        }
        bool Release(bool manual)
        {
            if (Sending || (WaitingCount == 0 && recovered.Count == 0) || (!manual && !Ready)) return false;
            if (!PartyDelivery.Plan(Outputs[0], ReleaseBatch(manual), out _, out _)) return false;
            // Freeze one complete batch in the output. A blocked destination cannot
            // mix in the next batch, and pipe transfers retain the original items.
            for (int port = 0; port < SlotCount && Outputs[0].BufferedItems.Count < MaxPartySize; port++)
                if (Inputs[port].HasBuffer && (manual || port < RequiredCount))
                    Outputs[0].BufferedItems.Add(Inputs[port].ConsumeItem());
            if (manual)
                while (recovered.Count > 0 && Outputs[0].BufferedItems.Count < MaxPartySize)
                {
                    Outputs[0].BufferedItems.Add(recovered[0]); recovered.RemoveAt(0);
                }
            return Outputs[0].HasBuffer;
        }

        protected override void CopySettingsTo(FlowElement copy)
        {
            var party = (Party)copy;
            Array.Clear(party.required, 0, SlotCount);
            for (int i = 0; i < SlotCount; i++) party.SetRequired(i, required[i]);
        }
        protected override void DataToJson(JsonObject data)
        {
            var counts = new JsonArray();
            foreach (int count in required) counts.Add(count);
            data["PartyRequirements"] = counts;
            if (recovered.Count > 0) data["RecoveredMembers"] = recovered.ToJsonArray();
        }
        protected override void DataFromJson(JsonObject data, FlowDiagram parent)
        {
            legacyAutoSend = !data["Upgrades"].IsJsonArray && data["AutoSend"].AsBoolean;
            var counts = data["PartyRequirements"];
            if (counts.IsJsonArray)
            {
                Array.Clear(required, 0, SlotCount);
                for (int i = 0; i < SlotCount; i++)
                    SetRequired(i, i < counts.AsJsonArray.Count && counts[i].IsInteger ? counts[i].AsInteger : 0);
            }
            if (data["RecoveredMembers"].IsJsonArray)
                recovered.FromJsonArray(data["RecoveredMembers"], InventoryItem.FromJson);
        }
    }
}
