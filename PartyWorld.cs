using System.Collections.Generic;
using System.Linq;
using Assets.Behaviour.Flow;
using Assets.Behaviour.UI;
using Assets.Source.Items;
using Behaviour.UI.Tooltip;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomPartyNode
{
    public sealed class PartyWorld : MonoBehaviour
    {
        sealed class Walker
        {
            internal InventoryItem Item;
            internal RectTransform Root;
            internal Image Image;
            internal float Distance, Speed, Phase, Age;
            internal int Direction;
            internal Vector2 Ground;
        }
        // A floor loop around the map table, avoiding the table and wall props.
        static readonly Vector2[] Path = {
            new Vector2(.16f, .62f), new Vector2(.20f, .45f), new Vector2(.39f, .40f),
            new Vector2(.68f, .40f), new Vector2(.81f, .53f), new Vector2(.82f, .80f),
            new Vector2(.72f, .95f), new Vector2(.29f, .95f), new Vector2(.16f, .82f)
        };
        readonly Dictionary<InventoryItem, Walker> walkers = new Dictionary<InventoryItem, Walker>();
        readonly List<InventoryItem> present = new List<InventoryItem>(25);
        RectTransform members;
        Vector2[] route;
        float[] lengths;
        float perimeter;
        int nextWalker;
        TMP_Text status;
        Button send;
        readonly Image[] inputBadges = new Image[5];
        readonly TMP_Text[] inactiveBadges = new TMP_Text[5];
        readonly int[] lastArchetypes = { -2, -2, -2, -2, -2 };
        const float RoomX = 68, RoomY = 36, RoomWidth = 268, RoomHeight = 134;
        const float SendWidth = 116, SendX = Party.Width - 16 - SendWidth;
        Party party;
        PartyUi ui;
        internal PartySettingsPopup Settings { get; private set; }
        internal int VisibleMemberCount => walkers.Count;
        internal IEnumerable<InventoryItem> VisibleMembers => walkers.Keys;
        internal Vector2 MemberPosition(InventoryItem item) => walkers[item].Root.anchoredPosition;

        void Start()
        {
            var world = GetComponent<WorldFlowElement>();
            party = world.Element as Party;
            if (party == null) { enabled = false; return; }
            ui = new PartyUi(PartyNodePlugin.Field<TMP_Text>(world, "_label").font);
            var body = PartyUi.Rect(transform, "PartyMeetup", 0, 0, Party.Width, Party.Height);
            var room = PartyUi.Rect(body, "EmptyMeetupRoom", RoomX, RoomY, RoomWidth, RoomHeight).gameObject.AddComponent<Image>();
            room.sprite = PartyArtwork.RoomSprite; room.raycastTarget = false;
            members = PartyUi.Rect(body, "ActualPartyMembers", RoomX, RoomY, RoomWidth, RoomHeight);
            route = Path.Select(p => new Vector2(p.x * RoomWidth, -p.y * RoomHeight)).ToArray();
            lengths = new float[route.Length];
            for (int i = 0; i < route.Length; i++) perimeter += lengths[i] = Vector2.Distance(route[i], route[(i + 1) % route.Length]);
            var gear = ui.Button(body, "", Party.Width - 42, 0, 30, 30, OpenSettings); gear.name = "PartySettingsGear";
            var cog = PartyUi.Rect(gear.transform, "Cog", 5, 5, 20, 20).gameObject.AddComponent<PartyGearGraphic>();
            cog.color = PartyUi.Paper; cog.raycastTarget = false;
            gear.gameObject.AddComponent<TooltipSource>().SetTooltip("Configure party", "Choose up to five members total across all archetypes.");
            for (int port = 0; port < 5; port++)
            {
                inputBadges[port] = PartyUi.Rect(body, "InputClass_" + (port + 1), 8, 28 + 36 * port, 20, 24).gameObject.AddComponent<Image>();
                inputBadges[port].preserveAspect = true;
                inputBadges[port].gameObject.AddComponent<TooltipSource>();
                inactiveBadges[port] = ui.Text(body, "–", 8, 28 + 36 * port, 20, 24, 16);
                inactiveBadges[port].alignment = TextAlignmentOptions.Center;
                inactiveBadges[port].color = new Color(.5f, .48f, .45f);
            }
            status = ui.Text(body, "Waiting", 82, 184, SendX - 82 - 12, 26, 14);
            send = ui.Button(body, "Send now", SendX, 186, SendWidth, 26, SendNow);
            send.GetComponentInChildren<TMP_Text>().fontSize = 14;
            send.gameObject.AddComponent<TooltipSource>().SetTooltip("Send party", "Release the adventurers currently waiting, even if required counts are not met. The connected receiver must have room for everyone being sent.");
            // Native upgrade choices must draw above the room and its actors.
            PartyNodePlugin.Field<RectTransform>(world, "_upgradeSlots").SetAsLastSibling();
            Refresh();
        }
        void Update()
        {
            if (party == null) return;
            for (int port = 0; port < 5; port++)
            {
                int archetype = party.GetInputArchetype(port);
                if (lastArchetypes[port] == archetype) continue;
                lastArchetypes[port] = archetype;
                inputBadges[port].sprite = archetype >= 0 ? SpriteLibrary.Get(Party.Icons[archetype]) : null;
                inputBadges[port].enabled = archetype >= 0;
                inactiveBadges[port].gameObject.SetActive(archetype < 0);
                inputBadges[port].GetComponent<TooltipSource>().SetTooltip(archetype >= 0 ? "Input " + (port + 1) + ": " + Party.Names[archetype] : "Unused party slot",
                    archetype >= 0 ? "Accepts one matching adventurer for this party slot. Advanced classes match their base archetype." : "Select more members with the gear to use this input.");
            }
            Refresh(); TickWalkers(Time.deltaTime);
        }
        internal void Refresh()
        {
            if (party == null) return;
            string message = party.Sending ? "Sending · " + party.Outputs[0].BufferedItems.Count
                : (!party.AutoSendEnabled || party.Ready) && party.SendBlockReason != null ? party.SendBlockReason
                : party.RecoveredCount > 0 ? "Recovered · " + party.RecoveredCount
                : party.Ready && !party.AutoSendEnabled ? "Ready · " + party.WaitingCount + "/" + party.RequiredCount
                : party.AutoSendEnabled ? "Auto · " + party.WaitingCount + "/" + party.RequiredCount
                : party.RequiredCount == 0 ? "No lineup" : "Waiting · " + party.WaitingCount + "/" + party.RequiredCount;
            if (status.text != message) status.text = message;
            send.interactable = party.CanSendNow;
            present.Clear();
            foreach (var input in party.Inputs) present.AddRange(input.BufferedItems);
            present.AddRange(party.Outputs[0].BufferedItems);
            foreach (var item in walkers.Keys.Where(item => !present.Contains(item)).ToArray())
            {
                walkers[item].Root.gameObject.SetActive(false);
                Destroy(walkers[item].Root.gameObject); walkers.Remove(item);
            }
            foreach (var item in present)
            {
                if (walkers.ContainsKey(item)) continue;
                int index = nextWalker++;
                var root = PartyUi.Rect(members, "Member_" + item.Type.Identifier, 0, 0, 24, 30);
                root.pivot = new Vector2(.5f, 0);
                var image = root.gameObject.AddComponent<Image>(); image.sprite = item.Icon;
                image.preserveAspect = true; image.raycastTarget = false;
                var walker = new Walker { Item = item, Root = root, Image = image,
                    Distance = Mathf.Repeat(index * 83.7f, perimeter), Speed = 13 + index % 5 * 1.5f,
                    Phase = index * 1.37f, Direction = index % 2 == 0 ? 1 : -1 };
                walkers.Add(item, walker); Position(walker, 0);
            }
            if (Settings && Settings.IsOpen) Settings.Refresh();
        }
        internal void TickWalkers(float delta)
        {
            foreach (var walker in walkers.Values) Position(walker, delta);
            foreach (var walker in walkers.Values.OrderByDescending(w => w.Ground.y)) walker.Root.SetAsLastSibling();
        }
        void Position(Walker walker, float delta)
        {
            walker.Age += delta;
            walker.Distance = Mathf.Repeat(walker.Distance + delta * walker.Speed * walker.Direction, perimeter);
            float distance = walker.Distance;
            int segment = 0;
            while (segment < lengths.Length - 1 && distance >= lengths[segment]) distance -= lengths[segment++];
            var from = route[segment]; var to = route[(segment + 1) % route.Length];
            walker.Ground = Vector2.Lerp(from, to, distance / lengths[segment]);
            float step = Mathf.Abs(Mathf.Sin(walker.Age * 9 + walker.Phase)) * 1.3f;
            walker.Root.anchoredPosition = new Vector2(Mathf.Round(walker.Ground.x), Mathf.Round(walker.Ground.y + step));
            float facing = (to.x - from.x) * walker.Direction;
            if (Mathf.Abs(facing) > .1f) walker.Root.localScale = new Vector3(facing < 0 ? -1 : 1, 1, 1);
            walker.Image.color = new Color(1, 1, 1, Mathf.Clamp01(walker.Age * 4));
        }
        internal void SendNow()
        {
            if (party.SendNow()) UISounds.CraftFinished();
            Refresh();
        }
        internal void OpenSettings()
        {
            if (!Settings)
            {
                var toolbar = AccessTools.Field(typeof(GameUI), "_editHotbar").GetValue(GameUI.Instance) as RectTransform;
                var canvas = toolbar.GetComponentInParent<Canvas>().rootCanvas;
                var root = new GameObject("PartyConfiguration", typeof(RectTransform)); root.SetActive(false);
                Settings = root.AddComponent<PartySettingsPopup>();
                Settings.Initialize(this, party, ui.Font, canvas.transform);
            }
            Settings.Open();
        }
        void OnDisable()
        {
            if (Settings && Settings.IsOpen && GameUI.Instance) Settings.ClosePopup();
        }
        void OnDestroy()
        {
            if (!Settings) return;
            if (Settings.IsOpen && GameUI.Instance) Settings.ClosePopup();
            Destroy(Settings.gameObject);
        }
    }
}
