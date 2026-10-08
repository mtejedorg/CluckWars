using CluckWars.Abilities;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// The small shape on an ability hex that names its category without colour (round-2 finding 9:
    /// Steal and Defense read alike under deuteranopia): Steal = diamond, Control = circle,
    /// Defense = square, Utility = plus. Cream with an ink rim, drawn in USS
    /// (Assets/UI/Styles/CategoryMark.uss), the same mark on the menu's icons and the in-match hexes.
    /// </summary>
    public static class CategoryMark
    {
        public const string BaseClass = "cw-cat-mark";

        /// <summary>The USS modifier that draws <paramref name="category"/>'s shape.</summary>
        public static string ModifierClass(AbilityCategory category) => category switch
        {
            AbilityCategory.Steal   => "cw-cat-mark--steal",
            AbilityCategory.Control => "cw-cat-mark--control",
            AbilityCategory.Defense => "cw-cat-mark--defense",
            _                       => "cw-cat-mark--utility",
        };

        private static readonly AbilityCategory[] All =
            (AbilityCategory[])System.Enum.GetValues(typeof(AbilityCategory));

        /// <summary>A hidden mark. Its three children draw the Utility plus (two outlined bars and a cream hub
        /// that hides where their rims cross); the other shapes are the mark itself. Never takes a tap.</summary>
        public static VisualElement Create()
        {
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList(BaseClass);
            foreach (var part in new[] { "__h", "__v", "__hub" })
            {
                var child = new VisualElement { pickingMode = PickingMode.Ignore };
                child.AddToClassList(BaseClass + part);
                mark.Add(child);
            }
            mark.style.display = DisplayStyle.None;
            return mark;
        }

        /// <summary>Shows <paramref name="category"/>'s shape on <paramref name="mark"/>, or hides it (null).</summary>
        public static void Apply(VisualElement mark, AbilityCategory? category)
        {
            if (mark == null) return;
            foreach (var c in All) mark.RemoveFromClassList(ModifierClass(c));
            if (category.HasValue) mark.AddToClassList(ModifierClass(category.Value));
            mark.style.display = category.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
