using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// 2D world-space title / loading panel that mirrors the host/join section
    /// styling (panel-rounded-sm button backgrounds, scribble-showdown logo,
    /// TigerverseHoverFlip transitions). Initially shows PLAY (grayed) /
    /// TUTORIAL / SETTINGS — pressing PLAY hands off to the existing
    /// HostButton / JoinButton flow that lives on the same TitleCanvas. The
    /// canvas softly follows the player's head via CanvasFollowPlayer so the
    /// menu stays in front of them no matter where they look.
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleScreen : MonoBehaviour
    {
        private const string TutorialCompletedPrefKey = "Tigerverse.TutorialCompleted";
        private const string MasterVolumePrefKey      = "Tigerverse.MasterVolume";

        [Tooltip("Name of the existing 2D TitleCanvas in the scene; we host all menu UI inside it so the XR raycaster + styling are inherited.")]
        [SerializeField] private string existingTitleCanvasName = "TitleCanvas";

        [Tooltip("Names of children inside TitleCanvas that should remain visible at all times (logo, etc.). Everything else gets hidden until PLAY is pressed.")]
        [SerializeField] private string[] alwaysVisibleChildrenNames = { "TitleHeader" };

        [Tooltip("Sprite asset path under Resources for the rounded-panel button background. Falls back to a flat color if missing.")]
        [SerializeField] private string panelSpriteResourcePath = "UI/panel-rounded-sm";

        // ─── Runtime ────────────────────────────────────────────────────
        private GameObject _titleCanvasGo;
        private RectTransform _titleCanvasRT;
        private readonly List<GameObject> _hostJoinChildren = new List<GameObject>();
        private GameObject _menuPanel;            // PLAY / TUTORIAL / SETTINGS
        private GameObject _settingsPanel;
        private Button _playButton;
        private TMP_Text _hintLabel;
        private TMP_Text _volumeValueLabel;
        private ProfessorTutorial _activeTutorial;

        private bool _hasCompletedTutorial;
        private float _masterVolume = 1f;

        private static Sprite _cachedPanelSprite;
        private static TMP_FontAsset _cachedFont;

        private void Awake()
        {
            _hasCompletedTutorial = PlayerPrefs.GetInt(TutorialCompletedPrefKey, 0) == 1;
            _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 1f));
            ApplyMasterVolume();
        }

        private IEnumerator Start()
        {
            // Wait one frame so the XR rig + Camera.main resolve, and so any
            // scene-init scripts get to set up TitleCanvas first.
            yield return null;

            _titleCanvasGo = GameObject.Find(existingTitleCanvasName);
            if (_titleCanvasGo == null)
            {
                Debug.LogError($"[TitleScreen] Could not find TitleCanvas named '{existingTitleCanvasName}'. The 2D loading menu won't render.");
                yield break;
            }
            _titleCanvasRT = _titleCanvasGo.GetComponent<RectTransform>();

            // Add follow-the-player. CanvasFollowPlayer drives world-space
            // position/rotation each LateUpdate, so it overrides whatever the
            // XR rig parent provides — works even though TitleCanvas is
            // currently parented to "XR Origin (XR Rig)".
            if (_titleCanvasGo.GetComponent<CanvasFollowPlayer>() == null)
                _titleCanvasGo.AddComponent<CanvasFollowPlayer>();

            // Snapshot the existing direct children of TitleCanvas (except
            // anything in alwaysVisibleChildrenNames such as the logo). These
            // are the host/join flow widgets — HostButton, JoinButton,
            // CodeInput, StatusLabel, SoftKeyboard, ModeSlider, etc. We hide
            // them all on launch and restore them when PLAY is pressed.
            // Snapshot must happen BEFORE we add our own LoadingMenu /
            // SettingsPanel so we don't accidentally hide our own UI on launch.
            _hostJoinChildren.Clear();
            var alwaysVisible = new HashSet<string>(alwaysVisibleChildrenNames ?? new string[0]);
            var titleRoot = _titleCanvasGo.transform;
            for (int i = 0; i < titleRoot.childCount; i++)
            {
                var child = titleRoot.GetChild(i);
                if (child == null) continue;
                if (alwaysVisible.Contains(child.name)) continue;
                _hostJoinChildren.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }

            // Repurpose the SoftKeyboard's existing CLEAR key as a BACK-to-menu
            // button. Has to happen AFTER the snapshot/hide loop so we don't
            // miss the SoftKeyboard child, and BEFORE Build* runs so any
            // listener wiring is in place by the time the player presses PLAY.
            RepurposeClearKeyAsBack();

            BuildMenuPanel();
            BuildSettingsPanel();
            _settingsPanel.SetActive(false);
        }

        // ─── Build ──────────────────────────────────────────────────────
        private void BuildMenuPanel()
        {
            _menuPanel = new GameObject("LoadingMenu", typeof(RectTransform));
            var rt = _menuPanel.GetComponent<RectTransform>();
            rt.SetParent(_titleCanvasRT, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(700, 1000);

            // PLAY / TUTORIAL / SETTINGS, stacked vertically and centered.
            _playButton     = BuildButton(rt, "PLAY",     new Vector2(0f,  -40f), OnPlayPressed);
            BuildButton(rt, "TUTORIAL", new Vector2(0f, -150f), OnTutorialPressed);
            BuildButton(rt, "SETTINGS", new Vector2(0f, -260f), OnSettingsPressed);

            // Hint label visible only while PLAY is grayed.
            _hintLabel = BuildLabel(rt, "Press TUTORIAL first to unlock PLAY",
                                     new Vector2(0f, 50f), new Vector2(640, 60), fontSize: 28,
                                     color: new Color(0.30f, 0.20f, 0.10f, 0.80f), italic: true, bold: false);

            ApplyPlayGate();
        }

        private void RepurposeClearKeyAsBack()
        {
            // Find the existing CLEAR key on the SoftKeyboard and turn it into
            // BACK-to-menu. We piggy-back on the existing key visual so the
            // styling stays identical to the keyboard's other keys.
            var keys = _titleCanvasGo.GetComponentsInChildren<SoftKeyboardKey>(includeInactive: true);
            foreach (var key in keys)
            {
                if (key.action != SoftKeyboardKey.KeyAction.Clear) continue;

                // Drop the SoftKeyboardKey behaviour — we don't want it
                // re-wiring onClick to keyboard.Clear() ever again.
                Destroy(key);

                // Update the label.
                var tmp = key.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) tmp.text = "BACK";

                // Re-wire the Button to our handler.
                var btn = key.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(OnBackToMenuFromHostJoin);
                }
                break;
            }
        }

        private void BuildSettingsPanel()
        {
            _settingsPanel = new GameObject("SettingsPanel", typeof(RectTransform));
            var rt = _settingsPanel.GetComponent<RectTransform>();
            rt.SetParent(_titleCanvasRT, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(700, 1000);

            // Heading (no card background — keeps things light, matches the
            // host/join section which is just buttons + logo on a transparent
            // canvas).
            BuildLabel(rt, "SETTINGS", new Vector2(0f, 200f), new Vector2(500, 80), fontSize: 56,
                       color: new Color(0.10f, 0.07f, 0.05f), italic: false, bold: true);

            BuildLabel(rt, "Master Volume", new Vector2(-120f, 60f), new Vector2(360, 60), fontSize: 36,
                       color: new Color(0.10f, 0.07f, 0.05f), italic: false, bold: false);
            _volumeValueLabel = BuildLabel(rt, VolumeText(_masterVolume), new Vector2(170f, 60f),
                                            new Vector2(120, 60), fontSize: 40,
                                            color: new Color(0.10f, 0.07f, 0.05f), italic: false, bold: true);

            // Smaller +/- buttons inline with the value label.
            BuildButton(rt, "-", new Vector2(50f, 60f), () => AdjustVolume(-0.20f), width: 86, height: 86, fontSize: 56);
            BuildButton(rt, "+", new Vector2(290f, 60f), () => AdjustVolume(+0.20f), width: 86, height: 86, fontSize: 56);

            BuildButton(rt, "BACK", new Vector2(0f, -160f), OnBackToMenuFromSettings);
        }

        // ─── Procedural button matching host/join style ─────────────────
        private Button BuildButton(RectTransform parent, string label, Vector2 anchoredPos, Action onClick,
                                    float width = 259f, float height = 86f, int fontSize = 0)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(CanvasRenderer));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(width, height);

            var img = go.AddComponent<Image>();
            var panelSprite = LoadPanelSprite();
            if (panelSprite != null)
            {
                img.sprite = panelSprite;
                img.type = Image.Type.Sliced;
            }
            img.color = Color.white;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var cb = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = Color.black;
            cb.pressedColor     = Color.black;
            cb.selectedColor    = Color.white;
            cb.disabledColor    = new Color(0.30f, 0.30f, 0.30f, 0.50f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.10f;
            btn.colors = cb;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { try { onClick?.Invoke(); } catch (Exception e) { Debug.LogException(e); } });

            // Label child.
            var lblGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer));
            var lblRT = lblGo.GetComponent<RectTransform>();
            lblRT.SetParent(rt, false);
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(8, 8);
            lblRT.offsetMax = new Vector2(-8, -8);
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize > 0 ? fontSize : 44;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.black;
            tmp.raycastTarget = false;
            var font = LoadSophiecomicFont();
            if (font != null) tmp.font = font;

            // Match the website-style hover behavior used on HostButton/JoinButton.
            var hover = go.AddComponent<TigerverseHoverFlip>();
            hover.normalColor = Color.black;
            hover.hoverColor  = Color.white;
            hover.pressedScale = 0.92f;
            hover.scaleTweenSeconds = 0.08f;
            hover.Refresh();

            return btn;
        }

        private TMP_Text BuildLabel(RectTransform parent, string text, Vector2 anchoredPos, Vector2 size,
                                     int fontSize, Color color, bool italic, bool bold)
        {
            var go = new GameObject("Lbl_" + text.Replace(" ", "_"), typeof(RectTransform), typeof(CanvasRenderer));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = fontSize;
            FontStyles fs = FontStyles.Normal;
            if (bold) fs |= FontStyles.Bold;
            if (italic) fs |= FontStyles.Italic;
            tmp.fontStyle = fs;
            tmp.color = color;
            tmp.raycastTarget = false;
            var font = LoadSophiecomicFont();
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static TMP_FontAsset LoadSophiecomicFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = Resources.Load<TMP_FontAsset>("Fonts/sophiecomic SDF");
#if UNITY_EDITOR
            if (_cachedFont == null)
                _cachedFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Fonts/sophiecomic SDF.asset");
#endif
            return _cachedFont;
        }

        // ─── Menu actions ───────────────────────────────────────────────
        private void OnPlayPressed()
        {
            if (_playButton != null && !_playButton.interactable) return;

            // Tear our procedural menu down and reveal the existing host/join
            // controls that already live on TitleCanvas. The repurposed CLEAR
            // key on the SoftKeyboard (now labelled BACK) is what the player
            // uses to come back to the main menu.
            if (_menuPanel != null) _menuPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            foreach (var c in _hostJoinChildren)
                if (c != null) c.SetActive(true);
        }

        private void OnBackToMenuFromHostJoin()
        {
            // Inverse of OnPlayPressed: hide host/join, show main menu.
            foreach (var c in _hostJoinChildren)
                if (c != null) c.SetActive(false);
            if (_menuPanel != null) _menuPanel.SetActive(true);
        }

        private void OnTutorialPressed()
        {
            if (_activeTutorial != null) return;
            // Hide the entire title canvas (logo + menu + every other UI bit)
            // while the tutorial runs so the Professor has the player's full
            // attention. SetActive on the parent cascades to all children;
            // their individual active states are preserved for when we
            // re-show the canvas in OnTutorialFinished.
            if (_titleCanvasGo != null) _titleCanvasGo.SetActive(false);

            var tutGo = new GameObject("ProfessorTutorial");
            tutGo.transform.SetParent(transform, false);
            tutGo.transform.localPosition = Vector3.zero;
            tutGo.transform.localRotation = Quaternion.identity;
            _activeTutorial = tutGo.AddComponent<ProfessorTutorial>();
            _activeTutorial.OnTutorialFinished += OnTutorialFinished;
        }

        private void OnTutorialFinished()
        {
            _hasCompletedTutorial = true;
            PlayerPrefs.SetInt(TutorialCompletedPrefKey, 1);
            PlayerPrefs.Save();
            _activeTutorial = null;

            // Restore the canvas; child states (menu visible, settings hidden,
            // host/join hidden) were preserved by the SetActive(false) above.
            if (_titleCanvasGo != null) _titleCanvasGo.SetActive(true);
            ApplyPlayGate();
        }

        private void OnSettingsPressed()
        {
            if (_menuPanel != null) _menuPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(true);
        }

        private void OnBackToMenuFromSettings()
        {
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            if (_menuPanel != null) _menuPanel.SetActive(true);
        }

        // ─── Volume ─────────────────────────────────────────────────────
        private void AdjustVolume(float delta)
        {
            _masterVolume = Mathf.Clamp01(_masterVolume + delta);
            PlayerPrefs.SetFloat(MasterVolumePrefKey, _masterVolume);
            PlayerPrefs.Save();
            ApplyMasterVolume();
            if (_volumeValueLabel != null) _volumeValueLabel.text = VolumeText(_masterVolume);
        }

        private void ApplyMasterVolume()
        {
            AudioListener.volume = _masterVolume;
        }

        private static string VolumeText(float v01)
        {
            return Mathf.RoundToInt(v01 * 100f) + "%";
        }

        // ─── Helpers ────────────────────────────────────────────────────
        private void ApplyPlayGate()
        {
            if (_playButton != null)
                _playButton.interactable = _hasCompletedTutorial;
            if (_hintLabel != null)
                _hintLabel.gameObject.SetActive(!_hasCompletedTutorial);
        }

        private Sprite LoadPanelSprite()
        {
            if (_cachedPanelSprite != null) return _cachedPanelSprite;
            _cachedPanelSprite = Resources.Load<Sprite>(panelSpriteResourcePath);
#if UNITY_EDITOR
            if (_cachedPanelSprite == null)
                _cachedPanelSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Generated/panel-rounded-sm.png");
#endif
            return _cachedPanelSprite;
        }
    }
}
