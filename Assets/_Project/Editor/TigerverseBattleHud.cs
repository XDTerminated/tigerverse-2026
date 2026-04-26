#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Tigerverse.Core;
using Tigerverse.UI;

namespace Tigerverse.EditorTools
{
    /// <summary>
    /// Builds 2 HP bars and 2 PreBattleRevealCards in the Title scene, parented
    /// to the spawn pivots, then wires them into GameStateManager.hpBars[] and
    /// .revealCards[].
    /// </summary>
    public static class TigerverseBattleHud
    {
        [MenuItem("Tigerverse/Battle -> Build HP Bars + Reveal Cards")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var gsm = GameObject.FindFirstObjectByType<GameStateManager>();
            if (gsm == null) { Debug.LogError("[Tigerverse] GameStateManager missing."); return; }

            var pivotA = GameObject.Find("MonsterSpawnPivotA");
            var pivotB = GameObject.Find("MonsterSpawnPivotB");
            if (pivotA == null || pivotB == null)
            {
                Debug.LogError("[Tigerverse] Spawn pivots missing — run 'Battle → Enable Mock + Wire Spawn Pivots' first.");
                return;
            }

            // Brand palette: red for player A, blue for player B — same accent
            // colors the website uses on the brush swatches.
            var hpA = BuildHpBar(pivotA.transform, "HPBarA", TigerverseTheme.BrushRed);
            var hpB = BuildHpBar(pivotB.transform, "HPBarB", TigerverseTheme.BrushBlue);

            var cardA = BuildRevealCard(pivotA.transform, "RevealCardA");
            var cardB = BuildRevealCard(pivotB.transform, "RevealCardB");

            // Wire arrays on GameStateManager.
            var so = new SerializedObject(gsm);
            var hpArr = so.FindProperty("hpBars");
            hpArr.arraySize = 2;
            hpArr.GetArrayElementAtIndex(0).objectReferenceValue = hpA;
            hpArr.GetArrayElementAtIndex(1).objectReferenceValue = hpB;
            var cardArr = so.FindProperty("revealCards");
            cardArr.arraySize = 2;
            cardArr.GetArrayElementAtIndex(0).objectReferenceValue = cardA;
            cardArr.GetArrayElementAtIndex(1).objectReferenceValue = cardB;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] HP bars + reveal cards built and wired to GameStateManager.");
        }

        private static HPBar BuildHpBar(Transform pivot, string name, Color tint)
        {
            var existing = pivot.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // World-space canvas above the pivot.
            var canvasGo = new GameObject(name,
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(HPBar));
            canvasGo.transform.SetParent(pivot, false);
            canvasGo.transform.localPosition = new Vector3(0, 1.4f, 0); // above the spawn point
            canvasGo.transform.localScale = Vector3.one * 0.005f;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400, 60);

            // Background.
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            // White panel with the themed black border — matches the website's
            // bordered "card" look. The colored Fill in front shows progress.
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = TigerverseTheme.White;
            var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
            if (panelSprite != null)
            {
                bgImg.sprite = panelSprite;
                bgImg.type = Image.Type.Sliced;
            }

            // Fill.
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(canvasGo.transform, false);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = new Vector2(0, 0.2f); fillRT.anchorMax = new Vector2(1, 0.8f);
            fillRT.offsetMin = new Vector2(6, 0); fillRT.offsetMax = new Vector2(-6, 0);
            var fillImg = fill.GetComponent<Image>();
            fillImg.color = tint;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 1f;

            // Label.
            var lbl = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lbl.transform.SetParent(canvasGo.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero; lblRT.offsetMax = Vector2.zero;
            var tmp = lbl.GetComponent<TextMeshProUGUI>();
            tmp.text = "100/100";
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TigerverseTheme.Black;
            ApplyThemeFont(tmp);

            // Wire HPBar component.
            var bar = canvasGo.GetComponent<HPBar>();
            var bso = new SerializedObject(bar);
            bso.FindProperty("fillImage").objectReferenceValue = fillImg;
            bso.FindProperty("labelText").objectReferenceValue = tmp;
            bso.ApplyModifiedPropertiesWithoutUndo();
            return bar;
        }

        private static PreBattleRevealCard BuildRevealCard(Transform pivot, string name)
        {
            var existing = pivot.Find(name);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var canvasGo = new GameObject(name,
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup),
                typeof(PreBattleRevealCard));
            canvasGo.transform.SetParent(pivot, false);
            canvasGo.transform.localPosition = new Vector3(0, 2.2f, 0);
            canvasGo.transform.localScale = Vector3.one * 0.005f;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(560, 380);

            // Background panel.
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            // Reveal card uses the website's white "modal" panel with a thick
            // black border so it reads as a doodle-style card.
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = TigerverseTheme.White;
            var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
            if (panelSprite != null)
            {
                bgImg.sprite = panelSprite;
                bgImg.type = Image.Type.Sliced;
            }

            var nameTmp = MakeTextChild(canvasGo.transform, "Name", "Name", 64, new Vector2(0, 130));
            var elemTmp = MakeTextChild(canvasGo.transform, "Element", "ELEMENT", 36, new Vector2(0, 60));
            var hpTmp   = MakeTextChild(canvasGo.transform, "HP",      "HP 100", 32, new Vector2(0, 10));
            var flav    = MakeTextChild(canvasGo.transform, "Flavor",  "...", 24, new Vector2(0, -40));
            // Wider flavor text
            var flavRT = (RectTransform)flav.transform;
            flavRT.sizeDelta = new Vector2(540, 80);

            var movesTmp = MakeTextChild(canvasGo.transform, "Moves", "Moves: ...", 26, new Vector2(0, -130));
            var movesRT = (RectTransform)movesTmp.transform;
            movesRT.sizeDelta = new Vector2(540, 80);

            var card = canvasGo.GetComponent<PreBattleRevealCard>();
            var cso = new SerializedObject(card);
            cso.FindProperty("nameText").objectReferenceValue = nameTmp;
            cso.FindProperty("elementText").objectReferenceValue = elemTmp;
            cso.FindProperty("hp").objectReferenceValue = hpTmp;
            cso.FindProperty("flavorText").objectReferenceValue = flav;
            cso.ApplyModifiedPropertiesWithoutUndo();

            // Hidden by default; PreBattleRevealCard.Show() will tween it in.
            canvasGo.GetComponent<CanvasGroup>().alpha = 0;
            return card;
        }

        private static TextMeshProUGUI MakeTextChild(Transform parent, string name, string text, int fontSize, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(540, 70);
            rt.anchoredPosition = anchoredPos;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = TigerverseTheme.Black;
            ApplyThemeFont(t);
            return t;
        }

        private static void ApplyThemeFont(TMP_Text tmp)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TigerverseTheme.FontAssetPath);
            if (font != null) tmp.font = font;
        }
    }
}
#endif
