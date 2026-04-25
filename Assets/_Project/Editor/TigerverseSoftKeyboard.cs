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

            // Container.
            var kbGo = new GameObject("SoftKeyboard", typeof(RectTransform), typeof(SoftKeyboard));
            kbGo.transform.SetParent(canvas.transform, false);
            var kbRT = kbGo.GetComponent<RectTransform>();
            kbRT.sizeDelta = new Vector2(560, 320);
            kbRT.anchoredPosition = new Vector2(0, -180); // below the input field
            var kb = kbGo.GetComponent<SoftKeyboard>();
            var inputField = input.GetComponent<TMP_InputField>();
            kb.SetTarget(inputField);
            var kbSo = new SerializedObject(kb);
            kbSo.FindProperty("target").objectReferenceValue = inputField;
            kbSo.FindProperty("maxLength").intValue = 4;
            kbSo.ApplyModifiedPropertiesWithoutUndo();

            // 8 cols x 4 rows = 32 letter keys (matches Alphabet length).
            int cols = 8;
            float keyW = 62, keyH = 56;
            float gap = 4;
            float startX = -(cols * (keyW + gap)) / 2f + (keyW + gap) / 2f;
            float startY = (4 * (keyH + gap)) / 2f - (keyH + gap) / 2f;

            for (int i = 0; i < Alphabet.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                string ch = Alphabet[i].ToString();
                var key = MakeKey(kbGo.transform, ch, ch);
                var rt = key.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(keyW, keyH);
                rt.anchoredPosition = new Vector2(startX + col * (keyW + gap),
                                                  startY - row * (keyH + gap));
                var keyComp = key.AddComponent<SoftKeyboardKey>();
                keyComp.character = ch;
                keyComp.keyboard = kb;
                keyComp.action = SoftKeyboardKey.KeyAction.Append;
            }

            // Backspace + clear, in a row below the alphabet.
            float bottomRowY = startY - 4 * (keyH + gap) - keyH * 0.5f;

            var back = MakeKey(kbGo.transform, "Backspace", "BACKSPACE");
            var backRT = back.GetComponent<RectTransform>();
            backRT.sizeDelta = new Vector2(keyW * 4 + gap * 3, keyH);
            backRT.anchoredPosition = new Vector2(startX + (cols * 0.75f) * (keyW + gap) - keyW,
                                                  bottomRowY);
            var backKey = back.AddComponent<SoftKeyboardKey>();
            backKey.keyboard = kb;
            backKey.action = SoftKeyboardKey.KeyAction.Backspace;
            back.GetComponent<Image>().color = new Color(0.6f, 0.2f, 0.2f, 0.9f);
            back.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

            var clear = MakeKey(kbGo.transform, "Clear", "CLEAR");
            var clearRT = clear.GetComponent<RectTransform>();
            clearRT.sizeDelta = new Vector2(keyW * 4 + gap * 3, keyH);
            clearRT.anchoredPosition = new Vector2(startX + (cols * 0.25f) * (keyW + gap) - keyW,
                                                   bottomRowY);
            var clearKey = clear.AddComponent<SoftKeyboardKey>();
            clearKey.keyboard = kb;
            clearKey.action = SoftKeyboardKey.KeyAction.Clear;
            clear.GetComponent<Image>().color = new Color(0.4f, 0.4f, 0.4f, 0.9f);
            clear.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

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
            go.GetComponent<Image>().color = new Color(0.15f, 0.4f, 0.7f, 0.9f);

            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.GetComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = Vector2.zero; lblRt.offsetMax = Vector2.zero;
            var tmp = lblGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return go;
        }
    }
}
#endif
