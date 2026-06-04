using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using CluckWars.UI;

public static class UiPrefabBuilder
{
    public static void Run()
    {
        if (!System.IO.Directory.Exists("Assets/_Game/Prefabs/UI"))
            System.IO.Directory.CreateDirectory("Assets/_Game/Prefabs/UI");

        BuildAbilitySection();
        BuildEquipSlot();
        BuildEquipArea();
        
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

        var view = hdr.AddComponent<UiAbilitySectionView>();
        var so = new SerializedObject(view);
        so.FindProperty("_headerText").objectReferenceValue = hTxt;
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
        var headerRow = CreateUIObject("UiEquipArea", out var hrRT);
        headerRow.AddComponent<LayoutElement>().preferredHeight = 30;
        var hrL = headerRow.AddComponent<HorizontalLayoutGroup>(); 
        hrL.childControlWidth = true; hrL.childAlignment = TextAnchor.MiddleCenter;
        var txt = CreateText("EqText", headerRow.transform, 22, TextAnchor.MiddleCenter);

        var view = headerRow.AddComponent<UiEquipAreaView>();
        var so = new SerializedObject(view);
        so.FindProperty("_headerText").objectReferenceValue = txt;
        // Wait, UiEquipArea also contains the SlotsRow... 
        so.ApplyModifiedProperties();

        PrefabUtility.SaveAsPrefabAsset(headerRow, "Assets/_Game/Prefabs/UI/UiEquipArea.prefab");
        GameObject.DestroyImmediate(headerRow);
    }
}
