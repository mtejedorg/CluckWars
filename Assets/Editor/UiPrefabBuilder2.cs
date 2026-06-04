using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using CluckWars.UI;

public static class UiPrefabBuilder2
{
    public static void Run()
    {
        if (!System.IO.Directory.Exists("Assets/_Game/Prefabs/UI"))
            System.IO.Directory.CreateDirectory("Assets/_Game/Prefabs/UI");

        BuildAbilitySection();
        BuildEquipSlot();
        BuildEquipArea();
        BuildAbilityCard();
        BuildClassCard();
        
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Prefabs Built Successfully!");
    }

    private static GameObject CreateUIObject(string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        rt = (RectTransform)go.transform;
        return go;
    }

    private static Text CreateText(string name, Transform parent, int size, TextAnchor align)
    {
        var go = CreateUIObject(name, out var rt);
        rt.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.fontSize = size; txt.alignment = align;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return txt;
    }

    private static void BuildAbilitySection()
    {
        var hdr = CreateUIObject("UiAbilitySection", out var hRT);
        hdr.AddComponent<LayoutElement>().preferredHeight = 30;
        var hTxt = CreateText("Txt", hdr.transform, 20, TextAnchor.MiddleLeft);
        hTxt.fontStyle = FontStyle.Bold;

        // Container for cards
        var grid = CreateUIObject("Grid", out var gRT);
        gRT.SetParent(hdr.transform, false);
        var gL = grid.AddComponent<GridLayoutGroup>();
        gL.cellSize = new Vector2(210, 100); gL.spacing = new Vector2(15, 15);
        
        var view = hdr.AddComponent<UiAbilitySectionView>();
        var so = new SerializedObject(view);
        so.FindProperty("_headerText").objectReferenceValue = hTxt;
        so.FindProperty("_gridContainer").objectReferenceValue = grid.transform;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(hdr, "Assets/_Game/Prefabs/UI/UiAbilitySection.prefab");
        GameObject.DestroyImmediate(hdr);
    }

    private static void BuildEquipSlot()
    {
        var hexGO = CreateUIObject("UiEquipSlot", out var hRT);
        hRT.sizeDelta = new Vector2(80, 92);
        
        var img = hexGO.AddComponent<Image>();
        img.material = new Material(Shader.Find("CluckWars/UI/SDF"));
        img.material.SetFloat("_Shape", 1);
        img.material.SetFloat("_Radius", 0);
        img.material.SetFloat("_BorderWidth", 3);
        
        var btn = hexGO.AddComponent<Button>();
        var txt = CreateText("IconText", hexGO.transform, 32, TextAnchor.MiddleCenter);
        txt.rectTransform.anchorMin = Vector2.zero; txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = Vector2.zero; txt.rectTransform.offsetMax = Vector2.zero;

        var sLbl = CreateText("Label", hexGO.transform, 18, TextAnchor.MiddleCenter);
        sLbl.rectTransform.anchoredPosition = new Vector2(0, 50);

        var view = hexGO.AddComponent<UiEquipSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("_borderHexagon").objectReferenceValue = img;
        so.FindProperty("_iconText").objectReferenceValue = txt;
        so.FindProperty("_label").objectReferenceValue = sLbl;
        so.FindProperty("_button").objectReferenceValue = btn;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(hexGO, "Assets/_Game/Prefabs/UI/UiEquipSlot.prefab");
        GameObject.DestroyImmediate(hexGO);
    }

    private static void BuildEquipArea()
    {
        var area = CreateUIObject("UiEquipArea", out var aRT);
        var aL = area.AddComponent<VerticalLayoutGroup>(); 
        aL.childControlHeight = true; aL.childForceExpandHeight = false;

        var headerRow = CreateUIObject("HdrRow", out var hrRT);
        hrRT.SetParent(area.transform, false);
        headerRow.AddComponent<LayoutElement>().preferredHeight = 30;
        var hrL = headerRow.AddComponent<HorizontalLayoutGroup>(); 
        hrL.childControlWidth = true; hrL.childAlignment = TextAnchor.MiddleCenter;
        var txt = CreateText("EqText", headerRow.transform, 22, TextAnchor.MiddleCenter);

        var slotsRow = CreateUIObject("SlotsRow", out var sRT);
        sRT.SetParent(area.transform, false);
        var sL = slotsRow.AddComponent<HorizontalLayoutGroup>(); sL.spacing = 15; sL.childAlignment = TextAnchor.MiddleCenter;
        sL.childForceExpandWidth = false; sL.childForceExpandHeight = false;

        var view = area.AddComponent<UiEquipAreaView>();
        var so = new SerializedObject(view);
        so.FindProperty("_headerText").objectReferenceValue = txt;
        so.FindProperty("_slotsContainer").objectReferenceValue = slotsRow.transform;
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(area, "Assets/_Game/Prefabs/UI/UiEquipArea.prefab");
        GameObject.DestroyImmediate(area);
    }

    private static void BuildAbilityCard()
    {
        var card = CreateUIObject("UiAbilityCard", out var rt);
        rt.sizeDelta = new Vector2(210, 100);
        var fill = card.AddComponent<Image>();
        fill.material = new Material(Shader.Find("CluckWars/UI/SDF"));
        fill.material.SetFloat("_Shape", 0);
        fill.material.SetFloat("_Radius", 16);
        fill.material.SetFloat("_BorderWidth", 2);
        card.AddComponent<Button>();

        var lay = card.AddComponent<VerticalLayoutGroup>();
        lay.padding = new RectOffset(5,5,5,5); lay.spacing=2; lay.childAlignment = TextAnchor.MiddleCenter;
        lay.childControlWidth = true; lay.childForceExpandWidth = false;
        lay.childControlHeight = true; lay.childForceExpandHeight = false;

        var iconGO = CreateUIObject("Icon", out var iRT);
        iRT.SetParent(card.transform, false);
        var iconTMP = iconGO.AddComponent<TextMeshProUGUI>();
        iconTMP.alignment = TextAlignmentOptions.Center;
        iconTMP.fontSize = 36;
        iconGO.AddComponent<LayoutElement>().preferredHeight = 40;

        var cTxt = CreateText("Name", card.transform, 16, TextAnchor.MiddleCenter);
        cTxt.fontStyle = FontStyle.Bold;
        cTxt.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

        var badgeWrap = CreateUIObject("BadgeWrap", out var bwRT);
        bwRT.SetParent(card.transform, false);
        var bwLay = badgeWrap.AddComponent<HorizontalLayoutGroup>();
        bwLay.childControlWidth = true; bwLay.childForceExpandWidth = false; bwLay.childAlignment = TextAnchor.MiddleCenter;

        var badge = CreateUIObject("Badge", out var bRT);
        bRT.SetParent(badgeWrap.transform, false);
        badge.AddComponent<LayoutElement>().preferredWidth = 50;
        badge.GetComponent<LayoutElement>().preferredHeight = 20;
        var badgeImg = badge.AddComponent<Image>(); 
        var bTxt = CreateText("bTxt", badge.transform, 12, TextAnchor.MiddleCenter);
        bTxt.rectTransform.anchorMin = Vector2.zero; bTxt.rectTransform.anchorMax = Vector2.one;
        bTxt.rectTransform.offsetMin = Vector2.zero; bTxt.rectTransform.offsetMax = Vector2.zero;

        var view = card.AddComponent<UiAbilityCardView>();
        var so = new SerializedObject(view);
        so.FindProperty("_bgPanel").objectReferenceValue = fill;
        so.FindProperty("_iconText").objectReferenceValue = iconTMP;
        so.FindProperty("_nameText").objectReferenceValue = cTxt;
        so.FindProperty("_badgeBg").objectReferenceValue = badgeImg;
        so.FindProperty("_badgeText").objectReferenceValue = bTxt;
        so.FindProperty("_button").objectReferenceValue = card.GetComponent<Button>();
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(card, "Assets/_Game/Prefabs/UI/UiAbilityCard.prefab");
        GameObject.DestroyImmediate(card);
    }

    private static void BuildClassCard()
    {
        var card = CreateUIObject("UiClassCard", out var rt);
        rt.sizeDelta = new Vector2(0, 110);
        var fill = card.AddComponent<Image>();
        fill.material = new Material(Shader.Find("CluckWars/UI/SDF"));
        fill.material.SetFloat("_Shape", 0);
        fill.material.SetFloat("_Radius", 16);
        fill.material.SetFloat("_BorderWidth", 2);
        card.AddComponent<Button>();

        var layout = card.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(15,15,10,10); layout.spacing = 15; layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true; layout.childForceExpandWidth = false;

        var chick = CreateUIObject("Avatar", out var cRT);
        cRT.SetParent(card.transform, false);
        cRT.sizeDelta = new Vector2(70, 70);
        var chickImg = chick.AddComponent<Image>(); chickImg.preserveAspect = true;

        var textCol = CreateUIObject("TextCol", out var tcRT);
        tcRT.SetParent(card.transform, false);
        var tL = textCol.AddComponent<VerticalLayoutGroup>();
        tL.childControlHeight = true; tL.childForceExpandHeight = false; tL.childAlignment = TextAnchor.MiddleLeft;
        textCol.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var lbl = CreateText("NameTxt", textCol.transform, 28, TextAnchor.MiddleLeft);
        lbl.fontStyle = FontStyle.Bold;

        var roleTxt = CreateText("RoleTxt", textCol.transform, 16, TextAnchor.MiddleLeft);
        
        var badgeWrap = CreateUIObject("BadgeWrap", out var bwRT);
        bwRT.SetParent(card.transform, false);
        var bwLay = badgeWrap.AddComponent<HorizontalLayoutGroup>();
        bwLay.childControlWidth = true; bwLay.childForceExpandWidth = false; bwLay.childAlignment = TextAnchor.MiddleRight;

        var badge = CreateUIObject("Badge", out var bRT);
        bRT.SetParent(badgeWrap.transform, false);
        badge.AddComponent<LayoutElement>().preferredWidth = 40;
        badge.GetComponent<LayoutElement>().preferredHeight = 24;
        var badgeImg = badge.AddComponent<Image>();
        var bTxt = CreateText("bTxt", badge.transform, 14, TextAnchor.MiddleCenter);
        bTxt.rectTransform.anchorMin = Vector2.zero; bTxt.rectTransform.anchorMax = Vector2.one;
        bTxt.rectTransform.offsetMin = Vector2.zero; bTxt.rectTransform.offsetMax = Vector2.zero;

        var view = card.AddComponent<UiClassCardView>();
        var so = new SerializedObject(view);
        so.FindProperty("_bgPanel").objectReferenceValue = fill;
        so.FindProperty("_avatarImage").objectReferenceValue = chickImg;
        so.FindProperty("_nameText").objectReferenceValue = lbl;
        so.FindProperty("_roleText").objectReferenceValue = roleTxt;
        so.FindProperty("_badgeText").objectReferenceValue = bTxt;
        so.FindProperty("_button").objectReferenceValue = card.GetComponent<Button>();
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(card, "Assets/_Game/Prefabs/UI/UiClassCard.prefab");
        GameObject.DestroyImmediate(card);
    }
}
