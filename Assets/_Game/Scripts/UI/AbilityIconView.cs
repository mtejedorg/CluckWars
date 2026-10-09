using System;
using CluckWars.Abilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// One ability icon slot, built once: an accent hex (disc), the icon sprite on it, and a
    /// monogram label (safety net), which <see cref="AbilityIconPainter.Paint"/> repaints in place.
    /// </summary>
    internal sealed class AbilityIconView
    {
        public readonly VisualElement Disc;
        public readonly VisualElement Sprite;
        public readonly Label Mono;
        public readonly VisualElement Mark;   // category shape (CategoryMark), round-2 finding 9
        public string SpriteClass;

        public AbilityIconView(VisualElement host)
        {
            Disc = new VisualElement { pickingMode = PickingMode.Ignore };
            Disc.AddToClassList("cw-icon-disc");
            Sprite = new VisualElement { pickingMode = PickingMode.Ignore };
            Sprite.AddToClassList("cw-icon-sprite");
            Mono = new Label { pickingMode = PickingMode.Ignore };
            Mono.AddToClassList("cw-ability-mono");
            Mark = CategoryMark.Create();
            host.Add(Disc);
            host.Add(Sprite);
            host.Add(Mono);
            host.Add(Mark);
        }
    }

    /// <summary>
    /// Paints an ability into an <see cref="AbilityIconView"/>. Shared by the menu (deck cards, slots, starter
    /// chips, Coop hexes) and the in-match waiting room's seats (Phase 6 chunk 7d).
    /// </summary>
    internal static class AbilityIconPainter
    {
        /// <summary>
        /// Paints <paramref name="ab"/> into <paramref name="view"/>: its icon sprite
        /// (<see cref="AbilityIconStyle.ClassFor"/>; every mapped type is authored). A type with no
        /// mapping falls back to its <see cref="AbilityIconStyle.Monogram"/> on an accent disc and is
        /// logged once — a safety net, since AbilityIconArtTests fails on any unmapped type.
        /// </summary>
        public static void Paint(AbilityIconView view, AbilityBaseSO ab, bool disc = true, Action<AbilityBaseSO> onUnmapped = null)
        {
            if (view == null) return;
            if (!string.IsNullOrEmpty(view.SpriteClass)) view.Sprite.RemoveFromClassList(view.SpriteClass);
            view.SpriteClass = null;

            // The category shape, so a move's job never rests on colour alone. Perks have no category.
            CategoryMark.Apply(view.Mark, ab != null && ab is not PassiveAbilitySO ? ab.Category : (AbilityCategory?)null);

            if (ab == null)
            {
                view.Disc.style.display = DisplayStyle.None;
                view.Sprite.style.display = DisplayStyle.None;
                view.Mono.style.display = DisplayStyle.None;
                return;
            }

            var accent = AbilityPalette.HexColor(ab);
            string iconCls = AbilityIconStyle.ClassFor(ab);
            if (!string.IsNullOrEmpty(iconCls))
            {
                // Cream silhouette on the ability's accent hex (the lobby mini-hex is its own hex).
                view.Disc.style.display = disc ? DisplayStyle.Flex : DisplayStyle.None;
                view.Disc.style.unityBackgroundImageTintColor = accent;
                view.SpriteClass = iconCls;
                view.Sprite.AddToClassList(iconCls);
                view.Sprite.EnableInClassList("cw-icon-sprite--on-disc", disc);
                view.Sprite.style.display = DisplayStyle.Flex;
                view.Mono.style.display = DisplayStyle.None;
                return;
            }

            onUnmapped?.Invoke(ab);

            view.Disc.style.display = DisplayStyle.None;
            view.Sprite.style.display = DisplayStyle.None;
            view.Mono.style.display = DisplayStyle.Flex;
            view.Mono.text = AbilityIconStyle.Monogram(ab);
            // On a lobby hex the hex is already the accent disc: text only (contrast vs the accent).
            view.Mono.style.backgroundColor = disc ? accent : new Color(0f, 0f, 0f, 0f);
            view.Mono.style.color = AbilityPalette.InkOn(accent);
        }
    }
}
