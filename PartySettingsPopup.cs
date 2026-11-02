using Assets.Behaviour.Flow;
using Assets.Behaviour.UI;
using Assets.Behaviour.UI.Shared;
using Behaviour.UI.Tooltip;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using GameInput = Assets.Behaviour.Input.PlayerInput;

namespace KingdomPartyNode
{
    public sealed class PartySettingsPopup : UIPopupMenu
    {
        readonly TMP_Text[] targets = new TMP_Text[5], stored = new TMP_Text[5];
        readonly Button[] minus = new Button[5], plus = new Button[5];
        Party party;
        PartyWorld owner;
        RectTransform panel;
        TMP_Text status;
        TMP_Text budget;
        Button send;
        bool restoreInput;
        int openedFrame;
        const float Width = 500, Height = 516;
        internal bool IsOpen => gameObject.activeSelf;

        internal void Initialize(PartyWorld world, Party element, TMP_FontAsset font, Transform parent)
        {
            owner = world; party = element;
            transform.SetParent(parent, false);
            var root = (RectTransform)transform; root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var canvas = gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 100;
            gameObject.AddComponent<GraphicRaycaster>();
            var backdrop = PartyUi.Rect(root, "DismissBackdrop", 0, 0, 0, 0);
            backdrop.anchorMin = Vector2.zero; backdrop.anchorMax = Vector2.one; backdrop.offsetMin = backdrop.offsetMax = Vector2.zero;
            backdrop.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .6f);
            var dismiss = backdrop.gameObject.AddComponent<Button>(); dismiss.onClick.AddListener(ClosePopup);
            var ui = new PartyUi(font);
            panel = ui.Box(root, "PartySettingsPanel", 0, 0, Width, Height, "Tooltip").rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;
            ui.Text(panel, "Party Meetup", 26, 20, 400, 40, 25);
            ui.Button(panel, "X", 444, 23, 32, 32, ClosePopup).name = "ClosePartySettings";
            ui.Text(panel, "Choose up to five members total.", 26, 66, 448, 28, 16);
            ui.Text(panel, "Required", 272, 103, 100, 24, 15).alignment = TextAlignmentOptions.Center;
            ui.Text(panel, "Waiting", 386, 103, 88, 24, 15).alignment = TextAlignmentOptions.Center;
            for (int i = 0; i < 5; i++)
            {
                int slot = i; float y = 136 + i * 51;
                var icon = PartyUi.Rect(panel, Party.Names[i] + "Icon", 30, y, 32, 32).gameObject.AddComponent<Image>();
                icon.sprite = SpriteLibrary.Get(Party.Icons[i]); icon.preserveAspect = true;
                ui.Text(panel, Party.Names[i], 76, y, 180, 32, 18);
                minus[i] = ui.Button(panel, "−", 272, y, 32, 32, () => Change(slot, -1));
                targets[i] = ui.Text(panel, "1", 311, y, 28, 32, 20); targets[i].alignment = TextAlignmentOptions.Center;
                plus[i] = ui.Button(panel, "+", 346, y, 32, 32, () => Change(slot, 1));
                plus[i].gameObject.AddComponent<TooltipSource>().SetTooltip("Five members total", "Reduce another required count to make room when all five slots are selected.");
                stored[i] = ui.Text(panel, "0 / 1", 391, y, 78, 32, 18); stored[i].alignment = TextAlignmentOptions.Center;
                icon.gameObject.AddComponent<TooltipSource>().SetTooltip(Party.Names[i], i == 0 ? "None means an ordinary Adventurer. Conscripts are not accepted." : "Advanced classes match their base archetype.");
            }
            budget = ui.Text(panel, "", 26, 399, 448, 26, 15);
            status = ui.Text(panel, "", 26, 429, 448, 26, 16); status.alignment = TextAlignmentOptions.Center;
            send = ui.Button(panel, "Send now", 152, 472, 196, 32, () => { owner.SendNow(); Refresh(); });
            send.gameObject.AddComponent<TooltipSource>().SetTooltip("Send party", "Send everyone currently waiting, even if the required counts are incomplete. The receiving node must have room for the entire batch.");
        }
        internal void Open()
        {
            if (IsOpen) { ClosePopup(); return; }
            Refresh(); GameUI.Instance.TogglePopupMenu(this, true); transform.SetAsLastSibling();
            openedFrame = Time.frameCount; restoreInput = GameInput.Enabled; GameInput.Disable();
        }
        void Change(int slot, int delta)
        {
            party.SetRequired(slot, party.GetRequired(slot) + delta); UISounds.CraftStep();
            Refresh(); owner.Refresh();
        }
        internal void Refresh()
        {
            for (int i = 0; i < 5; i++)
            {
                int count = party.GetWaiting(i), target = party.GetRequired(i);
                targets[i].text = target.ToString(); stored[i].text = count + " / " + target;
                stored[i].color = target == 0 ? new Color(.65f, .64f, .6f) : count >= target ? new Color(.6f, 1, .65f) : PartyUi.Paper;
                minus[i].interactable = target > 0; plus[i].interactable = target < 5 && party.RemainingSlots > 0;
            }
            budget.text = party.RequiredCount + " / 5 selected. 0 disables an input.";
            status.text = party.Sending ? "Sending party · " + party.Outputs[0].BufferedItems.Count + " remaining"
                : (!party.AutoSendEnabled || party.Ready) && party.SendBlockReason != null ? party.SendBlockReason + " · party stays here"
                : party.RecoveredCount > 0 ? "Recovered reserve: " + party.RecoveredCount + " · Send now (max 5)."
                : party.Ready && !party.AutoSendEnabled ? "Party ready · click Send now"
                : party.RequiredCount == 0 ? "All inputs disabled" : "Waiting · " + party.WaitingCount + " / " + party.RequiredCount + " adventurers";
            send.interactable = party.CanSendNow;
        }
        void Update()
        {
            if (!owner || !owner.isActiveAndEnabled || !WorldFlowManager.Visible) { ClosePopup(); return; }
            if (Time.frameCount != openedFrame && ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame))) { ClosePopup(); return; }
            var parent = transform.parent as RectTransform;
            if (parent) panel.localScale = Vector3.one * Mathf.Max(.1f, Mathf.Min(1, (parent.rect.width - 32) / Width, (parent.rect.height - 32) / Height));
            Refresh();
        }
        public override void PopupClosed()
        {
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
            base.PopupClosed();
        }
        void OnDisable()
        {
            base.OnPointerExit(null);
            if (restoreInput && GameUI.Instance && !GameUI.MenuVisible) GameInput.Enable();
            restoreInput = false;
        }
    }
}
