using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CluckWars.UI;
using UnityEngine.UI;
using TMPro;

public static class WireMenuScene
{
    public static void Run()
    {
        var controller = GameObject.FindFirstObjectByType<CharacterSelectController>();
        if (controller == null)
        {
            var go = new GameObject("BootstrapController");
            controller = go.AddComponent<CharacterSelectController>();
        }

        var so = new SerializedObject(controller);
        
        var abilityCard = AssetDatabase.LoadAssetAtPath<UiAbilityCardView>("Assets/_Game/Prefabs/UI/UiAbilityCard.prefab");
        var abilitySection = AssetDatabase.LoadAssetAtPath<UiAbilitySectionView>("Assets/_Game/Prefabs/UI/UiAbilitySection.prefab");
        var equipSlot = AssetDatabase.LoadAssetAtPath<UiEquipSlotView>("Assets/_Game/Prefabs/UI/UiEquipSlot.prefab");
        var classCard = AssetDatabase.LoadAssetAtPath<UiClassCardView>("Assets/_Game/Prefabs/UI/UiClassCard.prefab");
        var equipAreaPrefab = AssetDatabase.LoadAssetAtPath<UiEquipAreaView>("Assets/_Game/Prefabs/UI/UiEquipArea.prefab");

        so.FindProperty("_abilityCardPrefab").objectReferenceValue = abilityCard;
        so.FindProperty("_abilitySectionPrefab").objectReferenceValue = abilitySection;
        so.FindProperty("_equipSlotPrefab").objectReferenceValue = equipSlot;
        so.FindProperty("_classCardPrefab").objectReferenceValue = classCard;

        // Destroy existing MenuCanvas if it exists to build a fresh one
        var existingCanvas = GameObject.Find("MenuCanvas");
        if (existingCanvas != null) GameObject.DestroyImmediate(existingCanvas);

        var canvasGO = new GameObject("MenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var bgGO = CreateUIObject("ScreenBackground", canvasGO.transform, out var bgRT);
        bgGO.AddComponent<Image>().color = CharacterSelectController.DtScreenBg;
        SetStretch(bgRT);

        var csPanel = CreateUIObject("CharSelectPanel", canvasGO.transform, out var csRT);
        SetStretch(csRT);
        so.FindProperty("_charSelectPanel").objectReferenceValue = csPanel.gameObject;

        var lbPanel = CreateUIObject("LobbyPanel", canvasGO.transform, out var lbRT);
        SetStretch(lbRT);
        so.FindProperty("_lobbyPanel").objectReferenceValue = lbPanel.gameObject;
        lbPanel.SetActive(false); // Hide lobby by default

        // --- Char Select Layout ---
        var mainPanel = CreateUIObject("MainPanel", csPanel.transform, out var mpRT);
        SetStretch(mpRT);
        var mainLay = mainPanel.AddComponent<HorizontalLayoutGroup>();
        mainLay.padding = new RectOffset(50, 50, 50, 50); mainLay.spacing = 30;

        // Left Panel
        var left = CreateUIObject("LeftPanel", mainPanel.transform, out _);
        var lLayout = left.AddComponent<VerticalLayoutGroup>();
        lLayout.spacing = 20; lLayout.childControlHeight = true; lLayout.childForceExpandHeight = false;
        left.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Title
        var hdr = CreateUIObject("Hdr", left.transform, out var hRT);
        hdr.AddComponent<LayoutElement>().preferredHeight = 60;
        var hdrL = hdr.AddComponent<HorizontalLayoutGroup>(); hdrL.spacing = 20;
        CreateText("Title", hdr.transform, "CHARACTER SELECT", 48, TextAnchor.MiddleLeft);

        // Ribbon
        var ribbon = CreateUIObject("Ribbon", left.transform, out var ribRT);
        ribbon.AddComponent<LayoutElement>().preferredHeight = 40;
        var rImg = ribbon.AddComponent<Image>(); rImg.color = CharacterSelectController.DtGold;
        CreateText("Txt", ribbon.transform, "CHOOSE YOUR CLASS", 20, TextAnchor.MiddleCenter);

        // Class List
        var classList = CreateUIObject("ClassList", left.transform, out _);
        var clLay = classList.AddComponent<HorizontalLayoutGroup>();
        clLay.spacing = 15; clLay.childForceExpandWidth = true; clLay.childControlWidth = true;
        classList.AddComponent<LayoutElement>().preferredHeight = 110;
        so.FindProperty("_classListContainer").objectReferenceValue = classList.transform;

        // Preview Area
        var previewGo = CreateUIObject("PreviewPanel", left.transform, out _);
        previewGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var prImg = previewGo.AddComponent<Image>(); prImg.color = CharacterSelectController.DtPanelBg;

        var previewGlow = CreateUIObject("Glow", previewGo.transform, out var pgRT);
        pgRT.sizeDelta = new Vector2(300, 300); pgRT.anchoredPosition = new Vector2(0, 50);
        var pgImg = previewGlow.AddComponent<Image>();
        so.FindProperty("_previewGlowImg").objectReferenceValue = pgImg;

        var previewChick = CreateUIObject("Chicken", previewGo.transform, out var pcRT);
        pcRT.sizeDelta = new Vector2(250, 250); pcRT.anchoredPosition = new Vector2(0, 50);
        var pcImg = previewChick.AddComponent<Image>();
        so.FindProperty("_previewChickenImg").objectReferenceValue = pcImg;

        var pName = CreateText("Name", previewGo.transform, "WARRIOR CHICKEN", 36, TextAnchor.MiddleCenter);
        pName.rectTransform.anchoredPosition = new Vector2(0, -100);
        so.FindProperty("_previewNameText").objectReferenceValue = pName;

        var pBadge = CreateText("Badge", previewGo.transform, "PASSIVE", 18, TextAnchor.MiddleCenter);
        pBadge.rectTransform.anchoredPosition = new Vector2(0, -140);
        so.FindProperty("_previewBadgeText").objectReferenceValue = pBadge;

        var pDesc = CreateText("Desc", previewGo.transform, "Deals increased ability damage.", 20, TextAnchor.MiddleCenter);
        pDesc.rectTransform.anchoredPosition = new Vector2(0, -170);
        so.FindProperty("_previewDescText").objectReferenceValue = pDesc;

        // Right Panel
        var right = CreateUIObject("RightPanel", mainPanel.transform, out _);
        right.AddComponent<LayoutElement>().minWidth = 720f;
        var rLayout = right.AddComponent<VerticalLayoutGroup>();
        rLayout.spacing = 15; rLayout.childControlHeight = true; rLayout.childForceExpandHeight = false;

        // Equip Area Instantiation
        var equipArea = (UiEquipAreaView)PrefabUtility.InstantiatePrefab(equipAreaPrefab, right.transform);
        equipArea.gameObject.name = "EquipArea";
        so.FindProperty("_equipArea").objectReferenceValue = equipArea;

        // Separator
        var sep = CreateUIObject("Sep", right.transform, out var sepRT);
        sepRT.sizeDelta = new Vector2(0, 2); sep.AddComponent<Image>().color = new Color(0.2f,0.2f,0.2f);

        // Scroll Area
        var scroll = CreateUIObject("Scroll", right.transform, out var scRT);
        scroll.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var sc = scroll.AddComponent<ScrollRect>(); sc.horizontal = false; sc.vertical = true;
        var vp = CreateUIObject("VP", scroll.transform, out var vpRT);
        SetStretch(vpRT); vp.AddComponent<RectMask2D>(); sc.viewport = vpRT;
        var content = CreateUIObject("Content", vp.transform, out var cRT);
        cRT.anchorMin = new Vector2(0,1); cRT.anchorMax = new Vector2(1,1); cRT.pivot = new Vector2(0,1);
        var cL = content.AddComponent<VerticalLayoutGroup>(); 
        cL.spacing = 10; cL.childForceExpandHeight = false; cL.childControlHeight = true;
        cL.childForceExpandWidth = true; cL.childControlWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sc.content = cRT;
        so.FindProperty("_abilityGridContent").objectReferenceValue = content.transform;

        // Bottom Button
        var rightBot = CreateUIObject("Bot", right.transform, out _);
        rightBot.AddComponent<LayoutElement>().minHeight = 70f;
        var btnGo = CreateUIObject("BtnGo", rightBot.transform, out var bRT);
        SetStretch(bRT);
        var bImg = btnGo.AddComponent<Image>(); bImg.color = CharacterSelectController.DtCardOff;
        var btn = btnGo.AddComponent<Button>();
        CreateText("Txt", btnGo.transform, "CONFIRM", 32, TextAnchor.MiddleCenter);

        so.ApplyModifiedProperties();

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Wired CharacterSelectController and saved scene!");
    }

    private static GameObject CreateUIObject(string name, Transform parent, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = (RectTransform)go.transform;
        return go;
    }

    private static void SetStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor align)
    {
        var go = CreateUIObject(name, parent, out _);
        var t = go.AddComponent<Text>();
        t.text = content; t.fontSize = fontSize; t.alignment = align;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.color = Color.white;
        return t;
    }
}
