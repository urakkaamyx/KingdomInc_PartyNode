using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assets.Behaviour.Flow;
using Assets.Behaviour.Flow.Elements;
using Assets.Source.Flow;
using Assets.Source.Items;
using Assets.Source.Player;
using Behaviour.UI.Tooltip;
using BepInEx;
using HarmonyLib;
using KingdomFoundation.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomPartyNode
{
    [BepInPlugin(Id, "Party-Up", Version)]
    [BepInDependency("urakka.kingdominc.foundation", "0.5.0")]
    public sealed partial class PartyNodePlugin : BaseUnityPlugin
    {
        public const string Id = "urakka.kingdominc.partynode", Version = "0.7.0";
        Harmony patches;
        ITechNode partyResearch, autoSendResearch;
        IWorkspaceNodeType partyType;
        internal static bool Diagnostic => Environment.GetCommandLineArgs().Contains("-party-node-smoke");
        void Awake()
        {
            try
            {
                patches = new Harmony(Id);
                patches.PatchAll(Assembly.GetExecutingAssembly());
                RegisterFoundation();
            }
            catch { DisposeRegistrations(); patches?.UnpatchSelf(); PartyArtwork.Dispose(); throw; }
            Logger.LogInfo("Party Node ready: five archetype inputs, adjustable counts, one party output.");
            if (Diagnostic) { Application.runInBackground = true; StartCoroutine(RuntimeCheck()); }
        }
        void OnDestroy() { DisposeRegistrations(); patches?.UnpatchSelf(); PartyArtwork.Dispose(); }

        void DisposeRegistrations()
        {
            partyType?.Dispose(); autoSendResearch?.Dispose(); partyResearch?.Dispose();
        }

        void RegisterFoundation()
        {
            Assets.Source.Util.Translation.Init();
            var guild = TechNode.Get("adventurers_guild");
            partyResearch = FoundationApi.CreateTechNode(new TechNodeDefinition(
                Party.TechId, "Party Node", Id, "Party-Up", new Vector2(0, 0),
                "Assemble up to 5 adventurers. Press Send now to release.", PartyArtwork.Sprite,
                new TechNodeOptions(costTier: guild.CostTier, costMultiplier: 0.5,
                    requirements: new[] { guild.Identifier })));
            autoSendResearch = FoundationApi.CreateTechNode(new TechNodeDefinition(
                Party.AutoSendTechId, "Auto Send", Id, "Party-Up", new Vector2(270, 0),
                "Unlocks a Party Node upgrade slot. Use the node's + slot to buy Auto Send, " +
                "which sends only complete selected lineups. Remove the upgrade to return to manual sending.",
                research: new TechNodeOptions(nodeType: TechNodeType.UpgradeSlot,
                    costTier: guild.CostTier, costMultiplier: 0.5,
                    rootId: Party.TechId, previousId: Party.TechId, iconName: "Icons_17")));
            autoSendResearch.Clicked += node =>
            {
                PartyAutoSend.Unlock(GamePlayer.Current);
                if (WorldFlowManager.Instance) WorldFlowManager.Instance.UpdateFlowElements(Party.TypeId);
            };
            partyType = FoundationApi.RegisterWorkspaceNode(new WorkspaceNodeDefinition(
                Party.TypeId, "Party Node", () => new Party(), Party.TechId,
                new Vector2Int(Party.Width, Party.Height),
                "Assemble a party of up to 5 adventurers total. Set each archetype's required count; 0 disables it. " +
                "Press Send now to release the party. Research Auto Send, then install it in the node's + upgrade slot to automate complete lineups.",
                icon: PartyArtwork.RoomSprite, costMultiplier: 0.5));
        }

        // Older saves store the previous taller node's port coordinates.
        [HarmonyPatch(typeof(FlowElement), nameof(FlowElement.FromJson))]
        static class CompactSavedPorts
        {
            static void Postfix(FlowElement __result)
            {
                if (__result is Party party) { party.NormalizePorts(); party.NormalizeLoadedMembers(); party.MigrateLegacyUpgrade(); }
            }
        }

        // FlowInput.InputItem checks buffer capacity itself; this guard also stops
        // arrivals while a released party is still in the output.
        [HarmonyPatch(typeof(FlowIO), nameof(FlowIO.CanAcceptItem))]
        static class PartyInputFilter
        {
            static bool Prefix(FlowIO __instance, InventoryItem item, ref bool __result)
            {
                if (!(__instance is FlowInput) || !(__instance.Parent is Party party)) return true;
                __result = party.CanReceive(__instance.ParentIndex, item); return false;
            }
        }

        // Native ports retain their prefab's green image until the first timer
        // update. Initialize the role sprite immediately when ports are created
        // or rebuilt by upgrades, so an output never starts out green.
        [HarmonyPatch(typeof(WorldFlowIO), nameof(WorldFlowIO.SetIO))]
        static class PartyPortColors
        {
            static readonly FieldInfo imageField = AccessTools.Field(typeof(WorldFlowIO), "_sprite");
            static readonly FieldInfo inputField = AccessTools.Field(typeof(WorldFlowIO), "_inputSprites");
            static readonly FieldInfo outputField = AccessTools.Field(typeof(WorldFlowIO), "_outputSprites");
            static readonly FieldInfo errorField = AccessTools.Field(typeof(WorldFlowIO), "_errorSprites");
            static void Postfix(WorldFlowIO __instance, FlowIO io)
            {
                if (!(io.Parent is Party)) return;
                var field = io.ConnectedPipe?.ItemRejected == true ? errorField : io is FlowOutput ? outputField : inputField;
                var sprites = (Sprite[])field.GetValue(__instance);
                ((Image)imageField.GetValue(__instance)).sprite = sprites[io.HasBuffer ? 0 : 2];
            }
        }

        [HarmonyPatch(typeof(FlowPrefabManager), nameof(FlowPrefabManager.Get))]
        static class PartyPrefab
        {
            static FlowPrefabManager owner;
            static FlowPrefabSet custom;
            // Retain the custom meetup view after Foundation resolves its default prefab.
            static void Postfix(FlowPrefabManager __instance, string id, ref FlowPrefabSet __result)
            {
                if (id != Party.TypeId) return;
                if (!custom || owner != __instance)
                {
                    var original = __instance.Get("Collector");
                    if (!original) throw new InvalidOperationException("Collector prefab is unavailable.");
                    var container = new GameObject("PartyNodeAssets");
                    container.transform.SetParent(__instance.transform, false); container.SetActive(false);
                    custom = UnityEngine.Object.Instantiate(original, container.transform, false);
                    custom.Identifier = Party.TypeId; custom.name = Party.TypeId;
                    var world = UnityEngine.Object.Instantiate(original.Prefab, container.transform, false);
                    world.name = "PartyNodePrefab";
                    ((RectTransform)world.transform).sizeDelta = new Vector2(Party.Width, Party.Height);
                    var collector = world.GetComponent<WorldFlowCollector>();
                    if (collector) UnityEngine.Object.DestroyImmediate(collector);
                    var background = Field<Image>(world, "_background");
                    var label = Field<TMP_Text>(world, "_label");
                    var labelContainer = Field<Image>(world, "_labelContainer");
                    foreach (var graphic in world.GetComponentsInChildren<Graphic>(true))
                        if (graphic != background && graphic != label && graphic != labelContainer) graphic.enabled = false;
                    foreach (var button in world.GetComponentsInChildren<Button>(true)) button.enabled = false;
                    foreach (var tooltip in world.GetComponentsInChildren<TooltipSource>(true)) tooltip.enabled = false;
                    // Collector has no upgrade controls. Reuse Forge's native
                    // bottom slot layout and option popup/purchase behavior.
                    var upgradeSource = __instance.Get("Forge").Prefab;
                    var sourceSlots = Field<RectTransform>(upgradeSource, "_upgradeSlots");
                    var oldSlots = Field<RectTransform>(world, "_upgradeSlots");
                    if (oldSlots) UnityEngine.Object.DestroyImmediate(oldSlots.gameObject);
                    var slots = UnityEngine.Object.Instantiate(sourceSlots, world.transform, false);
                    slots.anchorMin = slots.anchorMax = slots.pivot = new Vector2(0, 1);
                    slots.anchoredPosition = new Vector2(36, -180);
                    slots.sizeDelta = new Vector2(36, 36);
                    AccessTools.Field(typeof(WorldFlowElement), "_upgradeSlots").SetValue(world, slots);
                    AccessTools.Field(typeof(WorldFlowElement), "_upgradePrefab").SetValue(world,
                        Field<WorldFlowUpgrade>(upgradeSource, "_upgradePrefab"));
                    world.gameObject.AddComponent<PartyWorld>();
                    AccessTools.Field(typeof(FlowPrefabSet), "<Prefab>k__BackingField").SetValue(custom, world);
                    AccessTools.Field(typeof(FlowPrefabSet), "<DetailSprite>k__BackingField").SetValue(custom, PartyArtwork.Sprite);
                    owner = __instance;
                }
                __result = custom;
            }
        }

        internal static T Field<T>(WorldFlowElement world, string name) where T : class =>
            AccessTools.Field(typeof(WorldFlowElement), name).GetValue(world) as T;
    }
}
