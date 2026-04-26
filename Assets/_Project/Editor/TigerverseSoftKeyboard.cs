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
        // Codes use this alphabet. Matches RoomCodeGenerator.Alphabet (full A-Z + 0-9).
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        // Full QWERTY layout, top → bottom.
        private static readonly string[] QwertyRows =
        {
            "1234567890",    // 10 numeric keys (top)
            "QWERTYUIOP",    // 10 keys
            "ASDFGHJKL",     // 9 keys
            "ZXCVBNM",       // 7 keys
        };

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

            float keyW = 60f, keyH = 56f, gap = 6f;
            int letterRows = QwertyRows.Length;
            int totalRows  = letterRows + 1; // +1 for action row
            float rowStride = keyH + gap;
            float colStride = keyW + gap;

            // Width is determined by the widest row (ASDFGHJKL = 9 keys).
            int maxCols = 0;
            for (int r = 0; r < QwertyRows.Length; r++)
                if (QwertyRows[r].Length > maxCols) maxCols = QwertyRows[r].Length;
            float kbWidth  = maxCols * keyW + (maxCols - 1) * gap;
            float kbHeight = totalRows * keyH + (totalRows - 1) * gap;

            // Container.
            var kbGo = new GameObject("SoftKeyboard", typeof(RectTransform), typeof(SoftKeyboard));
            kbGo.transform.SetParent(canvas.transform, false);
            var kbRT = kbGo.GetComponent<RectTransform>();
            kbRT.sizeDelta = new Vector2(kbWidth + 12, kbHeight + 12);
            kbRT.anchoredPosition = new Vector2(0, -210);
            var kb = kbGo.GetComponent<SoftKeyboard>();
            var inputField = input.GetComponent<TMP_InputField>();
            kb.SetTarget(inputField);
            var kbSo = new SerializedObject(kb);
            kbSo.FindProperty("target").objectReferenceValue = inputField;
            kbSo.FindProperty("maxLength").intValue = 4;
            kbSo.ApplyModifiedPropertiesWithoutUndo();

            float startY = ((totalRows - 1) * rowStride) * 0.5f;
            int totalKeys = 0;

            for (int row = 0; row < QwertyRows.Length; row++)
            {
                string rowStr = QwertyRows[row];
                int cols = rowStr.Length;
                float rowWidth = cols * keyW + (cols - 1) * gap;
                float rowStartX = -(rowWidth - keyW) * 0.5f; // each row centered

                for (int col = 0; col < cols; col++)
                {
                    string ch = rowStr[col].ToString();
                    var key = MakeKey(kbGo.transform, ch, ch);
                    var rt = key.GetComponent<RectTransform>();
                    rt.sizeDelta = new Vector2(keyW, keyH);
                    rt.anchoredPosition = new Vector2(rowStartX + col * colStride,
                                                      startY - row * rowStride);
                    var keyComp = key.AddComponent<SoftKeyboardKey>();
                    keyComp.character = ch;
                    keyComp.keyboard = kb;
                    keyComp.action = SoftKeyboardKey.KeyAction.Append;
                    totalKeys++;
                }
            }

            // Action row: two big half-width buttons (Clear | Backspace).
            float halfW = (kbWidth - gap) * 0.5f;
            float halfCenterX = (halfW + gap) * 0.5f;
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
            Debug.Log($"[Tigerverse] QWERTY soft keyboard added below CodeInput. {totalKeys} keys (digits + QWERTY) + Clear + Backspace.");
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
