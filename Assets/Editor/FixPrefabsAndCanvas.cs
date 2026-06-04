using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using CluckWars.UI;
using UnityEditor.SceneManagement;

public static class FixPrefabsAndCanvas
{
    public static void Run()
    {
        FixCanvas();
        FixClassCardPrefab();
        FixAbilityCardPrefab();
        FixEquipSlotPrefab();
        FixAbilitySectionPrefab();
        FixEquipAreaPrefab();
        
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("Fixed Prefabs and Canvas!");
    }

    private static void StyleText(Text t, int size, Color c, bool shadow)
    {
        t.font = UiGfx.ChunkyFont();
        t.fontSize = size;
        t.color = c;
        if (shadow && t.GetComponent<Shadow>() == null)
        {
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.5f);
            sh.effectDistance = new Vector2(1, -2);
        }
    }

    private static void StylePanel(GameObject go, Color fill, Color border, float radius, float borderWidth)
    {
        var img = go.GetComponent<Image>();
        if (img == null) img = go.AddComponent<Image>();
        img.material = new Material(Shader.Find("CluckWars/UI/SDF"));
        img.material.SetFloat("_Shape", 0);
        img.material.SetFloat("_Radius", radius);
        img.material.SetFloat("_BorderWidth", borderWidth);
        img.material.SetColor("_Color", fill);
        img.material.SetColor("_BorderColor", border);
    }

    private static void FixCanvas()
    {
        var csPanel = GameObject.Find("CharSelectPanel");
        if (csPanel == null) return;

        var mainPanel = csPanel.transform.Find("MainPanel");
        if (mainPanel != null)
        {
            StylePanel(mainPanel.gameObject, CharacterSelectController.DtPanelBg, UiGfx.CardBorder, 18, 3);
            var l = mainPanel.GetComponent<HorizontalLayoutGroup>();
            l.padding = new RectOffset(30,30,30,30); l.spacing = 20;
        }

        var left = mainPanel?.Find("LeftPanel");
        if (left != null)
        {
            var hdr = left.Find("Hdr");
            if (hdr != null)
            {
                var title = hdr.Find("Title")?.GetComponent<Text>();
                if (title != null) StyleText(title, 42, CharacterSelectController.DtGold, true);
            }

            var ribbon = left.Find("Ribbon");
            if (ribbon != null)
            {
                StylePanel(ribbon.gameObject, CharacterSelectController.DtGold, CharacterSelectController.DtGoldMid, 10, 3);
                var txt = ribbon.Find("Txt")?.GetComponent<Text>();
                if (txt != null) StyleText(txt, 22, Color.white, true);
            }

            var cl = left.Find("ClassList")?.GetComponent<HorizontalLayoutGroup>();
            if (cl != null) { cl.spacing = 15; cl.childForceExpandWidth = false; }

            var previewGo = left.Find("PreviewPanel");
            if (previewGo != null)
            {
                var prImg = previewGo.GetComponent<Image>();
                if (prImg != null) GameObject.DestroyImmediate(prImg);

                var pName = previewGo.Find("Name")?.GetComponent<Text>();
                if (pName != null) { StyleText(pName, 32, CharacterSelectController.DtRed, true); pName.fontStyle = FontStyle.Bold; }

                var pBadge = previewGo.Find("Badge")?.GetComponent<Text>();
                if (pBadge != null)
                {
                    StyleText(pBadge, 16, Color.white, true);
                    if (pBadge.transform.parent == previewGo)
                    {
                        var bw = new GameObject("BadgeWrap", typeof(RectTransform));
                        bw.transform.SetParent(previewGo, false);
                        var bwt = (RectTransform)bw.transform;
                        bwt.anchoredPosition = pBadge.rectTransform.anchoredPosition;
                        bwt.sizeDelta = new Vector2(120, 24);
                        var bwi = bw.AddComponent<Image>();
                        bwi.sprite = UiGfx.Rounded(12); bwi.color = CharacterSelectController.DtRed;
                        pBadge.transform.SetParent(bw.transform, false);
                        pBadge.rectTransform.anchorMin = Vector2.zero; pBadge.rectTransform.anchorMax = Vector2.one;
                        pBadge.rectTransform.offsetMin = Vector2.zero; pBadge.rectTransform.offsetMax = Vector2.zero;
                    }
                }

                var pDesc = previewGo.Find("Desc")?.GetComponent<Text>();
                if (pDesc != null) StyleText(pDesc, 18, CharacterSelectController.DtTextPrimary, false);
            }
        }

        var right = mainPanel?.Find("RightPanel");
        if (right != null)
        {
            var bot = right.Find("Bot");
            if (bot != null)
            {
                var btnGo = bot.Find("BtnGo");
                if (btnGo != null)
                {
                    StylePanel(btnGo.gameObject, CharacterSelectController.DtCardOff, UiGfx.CardBorder, 10, 2);
                    var txt = btnGo.Find("Txt")?.GetComponent<Text>();
                    if (txt != null) StyleText(txt, 28, CharacterSelectController.DtTextPrimary, true);
                }
            }
        }
    }

    private static void FixClassCardPrefab()
    {
        var path = "Assets/_Game/Prefabs/UI/UiClassCard.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var card = editScope.prefabContentsRoot;
            StylePanel(card, CharacterSelectController.DtCardOff, UiGfx.CardBorder, 16, 2);
            
            var tcol = card.transform.Find("TextCol");
            if (tcol != null)
            {
                var nTxt = tcol.Find("NameTxt")?.GetComponent<Text>();
                if (nTxt != null) StyleText(nTxt, 28, CharacterSelectController.DtTextPrimary, true);
                
                var rTxt = tcol.Find("RoleTxt")?.GetComponent<Text>();
                if (rTxt != null) StyleText(rTxt, 16, new Color(0.8f,0.8f,0.8f), false);
            }

            var bw = card.transform.Find("BadgeWrap/Badge");
            if (bw != null)
            {
                var img = bw.GetComponent<Image>();
                if (img != null) { img.sprite = UiGfx.Rounded(12); img.color = new Color(0,0,0,0.4f); }
                var bTxt = bw.Find("bTxt")?.GetComponent<Text>();
                if (bTxt != null) StyleText(bTxt, 14, Color.white, false);
            }
        }
    }

    private static void FixAbilityCardPrefab()
    {
        var path = "Assets/_Game/Prefabs/UI/UiAbilityCard.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var card = editScope.prefabContentsRoot;
            // The fill color is driven by the code, but we set a default
            StylePanel(card, CharacterSelectController.DtPanelBg, CharacterSelectController.DtTextPrimary, 16, 2);

            var iconTMP = card.transform.Find("Icon")?.GetComponent<TextMeshProUGUI>();
            if (iconTMP != null)
            {
                var tmpFont = UiGfx.TmpEmojiFont();
                if (tmpFont != null) iconTMP.font = tmpFont;
            }

            var nTxt = card.transform.Find("Name")?.GetComponent<Text>();
            if (nTxt != null) StyleText(nTxt, 16, CharacterSelectController.DtTextPrimary, true);

            var bw = card.transform.Find("BadgeWrap/Badge");
            if (bw != null)
            {
                var img = bw.GetComponent<Image>();
                if (img != null) { img.sprite = UiGfx.Rounded(10); }
                var bTxt = bw.Find("bTxt")?.GetComponent<Text>();
                if (bTxt != null) StyleText(bTxt, 12, CharacterSelectController.DtCardOff, false);
            }
        }
    }

    private static void FixEquipSlotPrefab()
    {
        var path = "Assets/_Game/Prefabs/UI/UiEquipSlot.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var hex = editScope.prefabContentsRoot;
            StylePanel(hex, Color.clear, new Color(0.4f, 0.35f, 0.3f), 0, 3);
            hex.GetComponent<Image>().material.SetFloat("_Shape", 1); // Pointy top hex

            var t = hex.transform.Find("IconText")?.GetComponent<Text>();
            if (t != null) StyleText(t, 32, UiGfx.CardBorder, true);

            var l = hex.transform.Find("Label")?.GetComponent<Text>();
            if (l != null) StyleText(l, 18, CharacterSelectController.DtGoldMid, true);
        }
    }

    private static void FixAbilitySectionPrefab()
    {
        var path = "Assets/_Game/Prefabs/UI/UiAbilitySection.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var sec = editScope.prefabContentsRoot;
            var t = sec.transform.Find("Txt")?.GetComponent<Text>();
            if (t != null) StyleText(t, 20, Color.white, true);
        }
    }

    private static void FixEquipAreaPrefab()
    {
        var path = "Assets/_Game/Prefabs/UI/UiEquipArea.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            var area = editScope.prefabContentsRoot;
            var t = area.transform.Find("HdrRow/EqText")?.GetComponent<Text>();
            if (t != null) StyleText(t, 22, CharacterSelectController.DtGold, true);
        }
    }
}
