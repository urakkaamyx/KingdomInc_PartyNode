using System;
using System.IO;
using System.Reflection;
using Assets.Behaviour.UI.BuyMenu;
using Assets.Behaviour.UI.TechTree;
using Assets.Source.Flow;
using Assets.Source.Player;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomPartyNode
{
    // Keep the generated original intact; the sprite rect excludes transparent
    // padding and Unity scales it to each native UI slot using point sampling.
    internal static class PartyArtwork
    {
        internal const string IconId = "PartyMeetup_Custom";
        internal const string RoomIconId = "PartyMeetupRoom_Custom";
        static Texture2D texture, roomTexture;
        static Sprite sprite, roomSprite;
        internal static Sprite Sprite => sprite ? sprite : (sprite = Load("party-meetup-v1.png", IconId, out texture));
        internal static Sprite RoomSprite => roomSprite ? roomSprite : (roomSprite = Load("party-meetup-empty-v2.png", RoomIconId, out roomTexture));
        static Sprite Load(string resource, string name, out Texture2D loadedTexture)
        {
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                using (var bytes = new MemoryStream())
                {
                    if (input == null) throw new InvalidOperationException("Missing Party Meetup artwork.");
                    input.CopyTo(bytes);
                    loadedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(loadedTexture, bytes.ToArray())) throw new InvalidDataException("Invalid Party Meetup artwork.");
                }
                loadedTexture.name = name; loadedTexture.filterMode = FilterMode.Point; loadedTexture.wrapMode = TextureWrapMode.Clamp;
                var pixels = loadedTexture.GetPixels32();
                int left = loadedTexture.width, bottom = loadedTexture.height, right = -1, top = -1;
                for (int y = 0; y < loadedTexture.height; y++)
                    for (int x = 0; x < loadedTexture.width; x++)
                        if (pixels[y * loadedTexture.width + x].a >= 16)
                        {
                            left = Math.Min(left, x); right = Math.Max(right, x);
                            bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                        }
                if (right < left) throw new InvalidDataException("Party Meetup artwork is empty.");
                var result = UnityEngine.Sprite.Create(loadedTexture, new Rect(left, bottom, right - left + 1, top - bottom + 1), new Vector2(.5f, .5f), 100);
                result.name = name;
                return result;
        }
        internal static void Dispose()
        {
            if (sprite) UnityEngine.Object.Destroy(sprite);
            if (texture) UnityEngine.Object.Destroy(texture);
            if (roomSprite) UnityEngine.Object.Destroy(roomSprite);
            if (roomTexture) UnityEngine.Object.Destroy(roomTexture);
            sprite = null; texture = null; roomSprite = null; roomTexture = null;
        }

        [HarmonyPatch(typeof(SpriteLibrary), nameof(SpriteLibrary.Get), new Type[] { typeof(string) })]
        static class CustomIcon
        {
            static bool Prefix(string name, ref Sprite __result)
            {
                if (name == IconId) { __result = Sprite; return false; }
                if (name == RoomIconId) { __result = RoomSprite; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(UIElementTechNode), nameof(UIElementTechNode.SetTech))]
        static class ResearchArtwork
        {
            static void Postfix(UIElementTechNode __instance, TechNode node)
            {
                if (node.Identifier != Party.TechId) return;
                var icon = AccessTools.Field(typeof(UITechNode), "_icon").GetValue(__instance) as Image;
                if (icon)
                {
                    icon.preserveAspect = true;
                    icon.rectTransform.sizeDelta = node.HasChildNodes ? new Vector2(64, 64) : new Vector2(144, 80);
                }
                var detail = AccessTools.Field(typeof(UIElementTechNode), "_detailImage").GetValue(__instance) as Image;
                if (detail)
                {
                    detail.preserveAspect = true;
                    detail.rectTransform.sizeDelta = new Vector2(150, 82);
                }
                var description = AccessTools.Field(typeof(UIElementTechNode), "_description").GetValue(__instance) as TMP_Text;
                if (description && node.HasChildNodes)
                    description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 170);
            }
        }

        [HarmonyPatch(typeof(BuyMenuRow), nameof(BuyMenuRow.SetElement))]
        static class BuildMenuArtwork
        {
            static void Postfix(BuyMenuRow __instance, FlowElement el)
            {
                // Preserve the rectangular room's proportions in its native icon slot.
                var icon = AccessTools.Field(typeof(BuyMenuRow), "_icon").GetValue(__instance) as Image;
                if (icon && el is Party) icon.preserveAspect = true;
            }
        }
    }
}
