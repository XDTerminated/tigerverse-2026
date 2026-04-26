using System;
using System.Collections;
using TMPro;
using Tigerverse.MR;
using Tigerverse.Net;
using Tigerverse.UI;
using Tigerverse.Voice;
using UnityEngine;
using UnityEngine.XR;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Post-hatch "ready up" phase. The local player must do BOTH of:
    ///   1. Say "ready" / "let's go" / "fight" out loud (open-mic).
    ///   2. Fist-bump the opponent (right hand within 0.20 m of the
    ///      remote PlayerAvatar's right hand).
    ///
    /// The READY button is kept as a dev/editor bypass that fires both
    /// flags at once — useful for solo testing where there's no opponent
    /// hand to bump.
    /// </summary>
    [DisallowMultipleComponent]
    public class ReadyHandshake : MonoBehaviour
    {
        [Tooltip("How close two right hands must be (m) to count as a fist bump.")]
        [SerializeField] private float fistBumpRadius = 0.20f;

        [Tooltip("Voice substrings (case-insensitive) that count as 'ready'.")]
        [SerializeField] private string[] readyKeywords = new[] { "ready", "let's go", "lets go", "fight", "begin" };

        [Tooltip("After 'ready' is said, the voice ✓ holds for this long before auto-clearing. Player must be fist-bumping during this window for the handshake to fire.")]
        [SerializeField] private float voiceMemorySec = 2.0f;

        public event Action OnLocalReady;

        private TutorialStartButton _button;       // reused — same paper-craft pressable
        private TextMeshPro _statusLabel;
        private VoiceCommandRouter  _voice;
        private Transform _localRightHand;
        private Transform _localLeftHand;
        private bool _fired;
        private bool _voiceConfirmed;
        private bool _bumpConfirmed;
        private float _voiceConfirmedAt;
        private float _bumpConfirmedAt;
        private float _lastBumpCheck;
        private float _lastBumpDiag;
        private float _lastFindAt;
        // World-space midpoint between the two right hands at the moment
        // the fist bump confirmed. Used as the shared MR arena anchor so
        // both players see monsters land in the same physical spot.
        private Vector3 _bumpMidpointWorld;
        private bool    _bumpMidpointValid;

        public void Configure(Vector3 buttonWorldPos, Quaternion buttonWorldRot)
        {
            // Paper-craft 3D button next to the player's monster (dev bypass).
            var btnGo = new GameObject("ReadyButton");
            btnGo.transform.SetParent(transform, false);
            btnGo.transform.position = buttonWorldPos;
            btnGo.transform.rotation = buttonWorldRot;
            _button = btnGo.AddComponent<TutorialStartButton>();
            _button.StartCoroutine(RelabelButton(_button, "I'M READY"));
            _button.OnPressed += HandlePress;

            // Floating status text above the button so the player can see
            // which gestures they've already done.
            var lblGo = new GameObject("ReadyStatus");
            lblGo.transform.SetParent(btnGo.transform, false);
            lblGo.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            _statusLabel = lblGo.AddComponent<TextMeshPro>();
            _statusLabel.fontSize = 0.40f;
            _statusLabel.alignment = TextAlignmentOptions.Center;
            _statusLabel.color = new Color(0.07f, 0.06f, 0.10f);
            _statusLabel.outlineColor = new Color32(255, 255, 255, 220);
            _statusLabel.outlineWidth = 0.18f;
            _statusLabel.enableWordWrapping = false;
            _statusLabel.rectTransform.sizeDelta = new Vector2(1.1f, 0.32f);
            UpdateStatusLabel();

            _voice = FindFirstObjectByType<VoiceCommandRouter>();
            if (_voice != null)
            {
                _voice.OnTranscript.AddListener(HandleVoice);
                // Open-mic so the player can just say "ready" without a
                // push-to-talk press. Tutorial may have already left it
                // off after it ended.
                _voice.SetOpenMicMode(true);
            }

            // Find the local player's right + left hand transforms via the XR rig.
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                _localRightHand = FindUnderRig(origin.transform, "Right Controller")
                              ?? FindUnderRig(origin.transform, "RightHand Controller")
                              ?? FindUnderRig(origin.transform, "Right Hand Controller");
                _localLeftHand  = FindUnderRig(origin.transform, "Left Controller")
                              ?? FindUnderRig(origin.transform, "LeftHand Controller")
                              ?? FindUnderRig(origin.transform, "Left Hand Controller");
            }

            Debug.Log("[ReadyHandshake] Waiting for BOTH: voice 'ready' AND fist bump. Self-bump (own L+R hands) also counts. Bypass via button.");
        }

        private IEnumerator RelabelButton(TutorialStartButton btn, string newLabel)
        {
            // Wait a frame so the button finished BuildVisual().
            yield return null;
            if (btn == null) yield break;
            var tmp = btn.GetComponentInChildren<TextMeshPro>();
            if (tmp != null) tmp.text = newLabel;
        }

        // Button = dev bypass: instantly fires Fire() (skips both gestures).
        private void HandlePress()
        {
            _voiceConfirmed = true;
            _bumpConfirmed  = true;
            UpdateStatusLabel();
            Fire("button (bypass)");
        }

        private void HandleVoice(string transcript)
        {
            if (_fired || _voiceConfirmed) return;
            if (string.IsNullOrEmpty(transcript)) return;
            string lower = transcript.ToLowerInvariant();
            foreach (var kw in readyKeywords)
            {
                if (!string.IsNullOrEmpty(kw) && lower.Contains(kw))
                {
                    _voiceConfirmed = true;
                    _voiceConfirmedAt = Time.time;
                    Buzz();
                    UpdateStatusLabel();
                    Debug.Log($"[ReadyHandshake] Voice CONFIRMED via '{transcript.Trim()}' at t={Time.time:F2}. Fist bump within {voiceMemorySec:F1}s.");
                    if (_bumpConfirmed) Fire("voice (bump already active)");
                    return;
                }
            }
        }

        private void UpdateStatusLabel()
        {
            if (_statusLabel == null) return;
            string voiceMark = _voiceConfirmed ? "<color=#2a9930>[X]</color>" : "<color=#666666>[ ]</color>";
            string bumpMark  = _bumpConfirmed  ? "<color=#2a9930>[X]</color>" : "<color=#666666>[ ]</color>";
            _statusLabel.text = $"{voiceMark} Say READY     {bumpMark} Fist bump";
        }

        private static void Buzz()
        {
            for (int i = 0; i < 2; i++)
            {
                var node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                    dev.SendHapticImpulse(0, 0.45f, 0.07f);
            }
        }

        private void Update()
        {
            if (_fired) return;
            if (Time.unscaledTime - _lastBumpCheck < 0.05f) return;
            _lastBumpCheck = Time.unscaledTime;

            // Voice memory: auto-clear voice ✓ if too much time has passed
            // without a fist bump.
            if (_voiceConfirmed && Time.time - _voiceConfirmedAt > voiceMemorySec)
            {
                _voiceConfirmed = false;
                UpdateStatusLabel();
                Debug.Log("[ReadyHandshake] Voice ✓ expired — say 'ready' again.");
            }

            // Real-time bump: ✓ only while hands are physically together.
            bool prevBump = _bumpConfirmed;
            _bumpConfirmed = false;
            float bestD = float.MaxValue;
            string bestSrc = null;
            Vector3 bestMidpoint = Vector3.zero;
            bool bestMidpointValid = false;

            if (_localRightHand != null)
            {
                if (_localLeftHand != null)
                {
                    float dSelf = Vector3.Distance(_localLeftHand.position, _localRightHand.position);
                    if (dSelf < bestD)
                    {
                        bestD = dSelf;
                        bestSrc = "self-bump (L+R)";
                        bestMidpoint = (_localLeftHand.position + _localRightHand.position) * 0.5f;
                        bestMidpointValid = true;
                    }
                }
#if FUSION2
                var avatars = FindObjectsByType<Tigerverse.Net.PlayerAvatar>(FindObjectsSortMode.None);
                for (int i = 0; i < avatars.Length; i++)
                {
                    var a = avatars[i];
                    if (a == null || a.HasInputAuthority) continue;
                    Transform remoteHand = FindRemoteRightHand(a.gameObject);
                    if (remoteHand == null) continue;
                    float d = Vector3.Distance(_localRightHand.position, remoteHand.position);
                    if (d < bestD)
                    {
                        bestD = d;
                        bestSrc = $"opponent-bump (d={d:F2}m)";
                        bestMidpoint = (_localRightHand.position + remoteHand.position) * 0.5f;
                        bestMidpointValid = true;
                    }
                }
#endif
            }

            if (bestD < fistBumpRadius)
            {
                _bumpConfirmed = true;
                if (bestMidpointValid)
                {
                    _bumpMidpointWorld = bestMidpoint;
                    _bumpMidpointValid = true;
                }
                if (!prevBump)
                {
                    Buzz();
                    Debug.Log($"[ReadyHandshake] Fist bump START via {bestSrc} mid={(bestMidpointValid ? bestMidpoint.ToString("F2") : "n/a")}");
                }
            }
            else if (prevBump)
            {
                Debug.Log("[ReadyHandshake] Fist bump RELEASED.");
            }

            if (_bumpConfirmed != prevBump) UpdateStatusLabel();

            // (Periodic diag log removed — it was hammering the console in VR.)

            // Both ✓ at the same instant → fire.
            if (_voiceConfirmed && _bumpConfirmed) Fire(bestSrc ?? "simultaneous");
        }

#if FUSION2
        private static Transform FindRemoteRightHand(GameObject avatar)
        {
            // Match the remote avatar's "right hand visual" by name fallbacks.
            foreach (var t in avatar.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                string n = t.name;
                if (n == "RightHand" || n == "RightHandVisual" || n == "rightHandVisual"
                    || n.IndexOf("right", System.StringComparison.OrdinalIgnoreCase) >= 0
                       && n.IndexOf("hand",  System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return t;
                }
            }
            return null;
        }
#endif

        private void Fire(string sourceLabel)
        {
            if (_fired) return;
            _fired = true;
            Debug.Log($"[ReadyHandshake] Local player READY via {sourceLabel}.");

            // The fist bump itself is the colocation event — both players'
            // controllers are at the same physical point at the moment it
            // confirmed. Use that point (snapped to the floor) as the
            // shared MR battle arena anchor and switch the headset into
            // passthrough so combat plays out in the real room.
            //
            // Gated on the Meta-OpenXR package being present. Without it
            // there is no passthrough source, so reparenting the spawn
            // pivots would teleport the monsters to a random spot in the
            // existing VR lobby — combat just stays where it is instead.
#if UNITY_XR_META_OPENXR
            EnterMRWithBumpAnchor();
#endif

            try { OnLocalReady?.Invoke(); } catch (Exception e) { Debug.LogException(e); }

            if (_voice != null)
            {
                _voice.OnTranscript.RemoveListener(HandleVoice);
                _voice.SetOpenMicMode(false); // restore push-to-talk for combat
            }
            if (_button != null && _button.gameObject != null) Destroy(_button.gameObject);
        }

        private void EnterMRWithBumpAnchor()
        {
            // Build a world-space anchor at the bump midpoint, snapped to
            // floor (Y=0). If we never captured a real bump (button-bypass),
            // anchor 1.5 m forward of the camera as a sane default.
            Vector3 anchorPos;
            if (_bumpMidpointValid)
            {
                anchorPos = new Vector3(_bumpMidpointWorld.x, 0f, _bumpMidpointWorld.z);
            }
            else
            {
                var cam = Camera.main;
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; else fwd.Normalize();
                Vector3 head = cam != null ? cam.transform.position : Vector3.zero;
                anchorPos = new Vector3(head.x, 0f, head.z) + fwd * 1.5f;
            }

            var anchorGo = new GameObject("BattleArenaAnchor");
            anchorGo.transform.position = anchorPos;
            anchorGo.transform.rotation = Quaternion.identity;

            // Reparent the existing monster spawn pivots under the anchor so
            // the rest of the combat code (which positions monsters at
            // those pivots) keeps working — they just appear in the room
            // relative to the bump point instead of the lobby world origin.
            var pivotA = GameObject.Find("MonsterSpawnPivotA");
            var pivotB = GameObject.Find("MonsterSpawnPivotB");
            if (pivotA != null) pivotA.transform.SetParent(anchorGo.transform, worldPositionStays: false);
            if (pivotB != null) pivotB.transform.SetParent(anchorGo.transform, worldPositionStays: false);
            // Spread the two pivots ~0.6 m on either side of the anchor so
            // both monsters have visible breathing room on the floor.
            if (pivotA != null) pivotA.transform.localPosition = new Vector3(-0.6f, 0f, 0f);
            if (pivotB != null) pivotB.transform.localPosition = new Vector3( 0.6f, 0f, 0f);

            MRSession.Enter(anchorGo.transform);
        }

        private void OnDestroy()
        {
            if (_voice != null)
            {
                _voice.OnTranscript.RemoveListener(HandleVoice);
                _voice.SetOpenMicMode(false);
            }
        }

        private static Transform FindUnderRig(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
