using System;
using Assets.Source.Flow;
using Assets.Source.Player;
using HarmonyLib;
using LightJson;

namespace KingdomPartyNode
{
    public sealed class PartyAutoSend : FlowUpgrade
    {
        public const string TypeId = "PartyAutoSend";
        public override string UpgradeType => TypeId;
        public override string DisplayName => "Auto Send";
        public override string IconName => "Icons_17";
        public override string GetDescription(FlowElement element, int nthUpgrade) =>
            "Automatically sends the party when every selected lineup slot is filled. Send now can still release an incomplete party. Remove this upgrade to return to manual sending.";
        public override bool IsAvailableFor(FlowElement element) =>
            element is Party party && party.AutoSendUnlocked && !party.HasUpgrade<PartyAutoSend>();
        protected override void DataToJson(JsonObject data) { }
        protected override void DataFromJson(JsonObject data) { }

        internal static void Unlock(GamePlayer player)
        {
            player.SetUpgradeSlotCount(Party.TypeId, 1);
            player.AddAvailableFlowUpgrade(Party.TypeId, TypeId);
        }

        [HarmonyPatch(typeof(FlowUpgrade), nameof(FlowUpgrade.Create))]
        static class Factory
        {
            static bool Prefix(string name, ref FlowUpgrade __result)
            {
                if (name == TypeId) { __result = new PartyAutoSend(); return false; }
                var native = typeof(FlowUpgrade).Assembly.GetType("Assets.Source.Flow.Upgrades." + name);
                if (native == null) return true;
                __result = (FlowUpgrade)Activator.CreateInstance(native); return false;
            }
        }

        // Older saves have the research but lack the native slot/upgrade registry.
        [HarmonyPatch(typeof(GamePlayer), nameof(GamePlayer.FromJson))]
        static class UpgradeRegistryMigration
        {
            static void Postfix(GamePlayer __result)
            {
                if (__result.HasTech(Party.AutoSendTechId)) Unlock(__result);
            }
        }
    }
}
