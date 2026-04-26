#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Tigerverse.UI;

namespace Tigerverse.EditorTools
{
    public static class TigerverseSoftKeyboard
    {
        // Codes use this alphabet (drops ambiguous chars). Same as RoomCodeGenerator.Alphabet.
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        [MenuItem("Tigerverse/UI -> Add Soft Keyboard to Title")]
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(
                "Assets/_Project/Scenes/Title.unity", OpenSceneMode.Single);

            var canvas = GameObject.Find("TitleCanvas");
            var input = GameObject.Find("CodeInput");
            if (canvas == null || input == null)
            {
                Debug.LogError("[Tigerverse] TitleCanvas or CodeInput missing.");
                return;
            }

            // Remove existing keyboard if present (idempotent rebuild).
            var oldKb = GameObject.Find("SoftKeyboard");
            if (oldKb != null) Object.DestroyImmediate(oldKb);

            // 8 cols x (4 letter rows + 1 action row). Same gap/key sizes as
            // the live layout so re-running the scaffold matches what's on
            // screen instead of resetting it to the old cramped grid.
            int cols = 8;
            int letterRows = 4;
            float keyW = 60f, keyH = 56f, gap = 6f;
            int totalRows = letterRows + 1;
            float rowStride = keyH + gap;
            float colStride = keyW + gap;
            float kbWidth  = cols * keyW + (cols - 1) * gap;
            float kbHeight = totalRows * keyH + (totalRows - 1) * gap;

            // Container.
            var kbGo = new GameObject("SoftKeyboard", typeof(RectTransform), typeof(SoftKeyboard));
            kbGo.transform.SetParent(canvas.transform, false);
            var kbRT = kbGo.GetComponent<RectTransform>();
            kbRT.sizeDelta = new Vector2(kbWidth + 12, kbHeight + 12);
            kbRT.anchoredPosition = new Vector2(0, -210); // below the input column
            var kb = kbGo.GetComponent<SoftKeyboard>();
            var inputField = input.GetComponent<TMP_InputField>();
            kb.SetTarget(inputField);
            var kbSo = new SerializedObject(kb);
            kbSo.FindProperty("target").objectReferenceValue = inputField;
            kbSo.FindProperty("maxLength").intValue = 4;
            kbSo.ApplyModifiedPropertiesWithoutUndo();

            float startX = -((cols - 1) * colStride) * 0.5f;
            float startY =  ((totalRows - 1) * rowStride) * 0.5f;

            for (int i = 0; i < Alphabet.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                string ch = Alphabet[i].ToString();
                var key = MakeKey(kbGo.transform, ch, ch);
                var rt = key.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(keyW, keyH);
                rt.anchoredPosition = new Vector2(startX + col * colStride,
                                                  startY - row * rowStride);
                var keyComp = key.AddComponent<SoftKeyboardKey>();
                keyComp.character = ch;
                keyComp.keyboard = kb;
                keyComp.action = SoftKeyboardKey.KeyAction.Append;
            }

            // Action row sits as the 5th grid row, one row-stride below the
            // last letter row, so it visually belongs to the keyboard panel.
            float halfW = 4 * keyW + 3 * gap;
            float halfCenterX = 2 * colStride; // center of cols 4-7 in the 8-col grid
            float bottomRowY = startY - letterRows * rowStride;

            var clear = MakeKey(kbGo.transform, "Clear", "CLEAR");
            var clearRT = clear.GetComponent<RectTransform>();
            clearRT.sizeDelta = new Vector2(halfW, keyH);
            clearRT.anchoredPosition = new Vector2(-halfCenterX, bottomRowY);
            var clearKey = clear.AddComponent<SoftKeyboardKey>();
            clearKey.keyboard = kb;
            clearKey.action = SoftKeyboardKey.KeyAction.Clear;
            clear.GetComponentInChildren<TextMeshProUGUI>().fontSize = 24;

            var back = MakeKey(kbGo.transform, "Backspace", "BACKSPACE");
            var backRT = back.GetComponent<RectTransform>();
            backRT.sizeDelta = new Vector2(halfW, keyH);
            backRT.anchoredPosition = new Vector2(halfCenterX, bottomRowY);
            var backKey = back.AddComponent<SoftKeyboardKey>();
            backKey.keyboard = kb;
            backKey.action = SoftKeyboardKey.KeyAction.Backspace;
            back.GetComponentInChildren<TextMeshProUGUI>().fontSize = 24;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Tigerverse] Soft keyboard added below CodeInput. 32 letter keys + Clear + Backspace.");
        }

        private static GameObject MakeKey(Transform parent, string name, string label)
        {
            var go = new GameObject(name,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            go.transform.SetParent(parent, false);

            // Theme-fidelity: outline white panel, themed b&w button colors,
            // hover-flip helper so the label inverts in step with the panel.
            var img = go.GetComponent<Image>();
            var panel = AssetDatabase.LoadAssetAtPath<Sprite>(TigerverseTheme.PanelSpritePath);
            if (panel != null) { img.sprite = panel; img.type = Image.Type.Sliced; }
            img.color = TigerverseTheme.White;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = TigerverseTheme.White;
            colors.highlightedColor = TigerverseTheme.Black;
            colors.pressedColor = TigerverseTheme.Black;
            colors.selectedColor = TigerverseTheme.Black;
            colors.colorMultiplier = 1f;
            btn.colors = colors;
            go.AddComponent<TigerverseHoverFlip>();

            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
            var tmp = lblGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 28;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TigerverseTheme.Black;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TigerverseTheme.FontAssetPath);
            if (font != null) tmp.font = font;
            return go;
        }
    }
}
#endif
