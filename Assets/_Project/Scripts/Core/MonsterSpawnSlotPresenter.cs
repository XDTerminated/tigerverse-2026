using Tigerverse.Net;
using Tigerverse.UI;
using UnityEngine;

namespace Tigerverse.Core
{
    /// <summary>
    /// Watches a single player slot in the live session. The instant that
    /// player submits a drawing (status moves past 'queued' or imageUrl
    /// arrives), this spawns an egg at this transform with a pop-in
    /// animation, the player's name floating above it, and a progress bar
    /// that fills while Meshy generates the model. ModelFetcher later
    /// reuses the same egg for the GLB-load + hatch.
    /// </summary>
    [DisallowMultipleComponent]
    public class MonsterSpawnSlotPresenter : MonoBehaviour
    {
        [Tooltip("0 = player A (data.p1), 1 = player B (data.p2)")]
        [SerializeField] private int slotIndex = 0;

        [Tooltip("How high above this transform the egg hovers (metres).")]
        [SerializeField] private float eggHoverHeight = 0.55f;

        [Tooltip("Estimated total seconds for Meshy to return the GLB. The progress bar climbs to ~95% over this duration; the actual hatch fires whenever the GLB really arrives.")]
        [SerializeField] private float estimatedTotalSeconds = 35f;

        [Tooltip("If true, look up the SessionApiClient automatically on Awake.")]
        [SerializeField] private bool autoFindApi = true;

        [SerializeField] private SessionApiClient apiClient;

        [Tooltip("If true, show a 'Start Tutorial' button next to the egg. The Professor Hooten tutorial spawns when the player presses it.")]
        [SerializeField] private bool spawnTutorial = true;

        [Tooltip("If true, only spawn the tutorial+button for the player whose caster index matches this slot. Set to false during solo dev testing so it spawns for both pivots.")]
        [SerializeField] private bool localOnly = true;

        [Tooltip("Where the Start-Tutorial button hovers, relative to the egg position.")]
        [SerializeField] private Vector3 startButtonOffset = new Vector3(0.55f, -0.05f, 0f);

        private HatchingEggSequence _egg;
        private ProfessorTutorial   _tutorial;
        private TutorialStartButton _startButton;
        private float _spawnTime;
        private string _lastName;
        private bool   _hatchHandedOff;

        private void Awake()
        {
            if (apiClient == null && autoFindApi)
                apiClient = FindFirstObjectByType<SessionApiClient>();
        }

        private void Update()
        {
            if (apiClient == null || apiClient.LastSession == null) return;

            PlayerData data = slotIndex == 0 ? apiClient.LastSession.p1 : apiClient.LastSession.p2;
            if (data == null) return;

            // Player has started producing: status past 'queued', OR an image
            // URL is available, OR a name has been chosen — any signal that
            // they hit submit on the website.
            bool started = !string.IsNullOrEmpty(data.imageUrl)
                          || !string.IsNullOrEmpty(data.name)
                          || (!string.IsNullOrEmpty(data.status) && data.status != "queued");

            if (_egg == null && started)
            {
                SpawnEgg(data);
            }

            if (_egg != null && !_egg.IsHatched)
            {
                if (data.name != _lastName)
                {
                    _egg.SetName(data.name);
                    _lastName = data.name;
                }

                // Time-based progress, capped at 95% until the GLB really arrives.
                float elapsed = Time.time - _spawnTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(estimatedTotalSeconds, 0.01f));
                if (string.IsNullOrEmpty(data.glbUrl)) t = Mathf.Min(t, 0.95f);
                _egg.SetDisplayProgress(t);

                // Crack progress is held at 0 during the wait — cracks only
                // appear when the GLB is actually ready and ModelFetcher
                // takes over for the burst. This way the egg looks intact
                // and idle while we wait, then dramatically cracks open.
                _egg.progress01 = 0f;
            }

            // Hand off to the hatch sequence — when the GLB really arrives,
            // tell the Professor to wrap up so the egg burst owns the moment.
            if (!_hatchHandedOff && _tutorial != null && !string.IsNullOrEmpty(data.glbUrl))
            {
                _tutorial.Stop();
                _hatchHandedOff = true;
            }
            if (_egg != null && _egg.IsHatched && _tutorial != null)
            {
                _tutorial.Stop();
            }
        }

        private void SpawnEgg(PlayerData data)
        {
            var eggHost = new GameObject("HatchingEgg");
            eggHost.transform.SetParent(transform, worldPositionStays: false);
            eggHost.transform.localPosition = Vector3.up * eggHoverHeight;
            eggHost.transform.localRotation = Quaternion.identity;

            _egg = eggHost.AddComponent<HatchingEggSequence>();
            _egg.SetName(string.IsNullOrEmpty(data.name) ? $"Player {slotIndex + 1}" : data.name);
            _egg.SetDisplayProgress(0f);
            _egg.progress01 = 0.05f;
            _spawnTime = Time.time;

            StartCoroutine(_egg.PlayPopInAnimation());

            // Spawn the Start-Tutorial button next to the egg, but only for
            // the local player (each headset shows its own button privately).
            if (spawnTutorial && IsLocalCaster())
            {
                SpawnStartButton();
            }

            Debug.Log($"[SlotPresenter] Slot {slotIndex} egg spawned for '{data.name}' under '{transform.name}'. StartButton={(_startButton != null)}");
        }

        private void SpawnStartButton()
        {
            var btnGo = new GameObject("StartTutorialButton");
            btnGo.transform.SetParent(transform, false);
            btnGo.transform.localPosition = (Vector3.up * eggHoverHeight) + startButtonOffset;
            // Face the camera (or world +Z fallback).
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - btnGo.transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                    btnGo.transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            }
            _startButton = btnGo.AddComponent<TutorialStartButton>();
            _startButton.OnPressed += StartTutorial;
        }

        private void StartTutorial()
        {
            if (_tutorial != null) return;
            var tutGo = new GameObject("ProfessorTutorial");
            tutGo.transform.SetParent(transform, false);
            tutGo.transform.localPosition = Vector3.zero;
            tutGo.transform.localRotation = Quaternion.identity;
            _tutorial = tutGo.AddComponent<ProfessorTutorial>();
            Debug.Log($"[SlotPresenter] Slot {slotIndex} tutorial started by player.");
        }

        private bool IsLocalCaster()
        {
            if (!localOnly) return true; // solo dev testing: ignore caster index, spawn for both

            var mgr = FindFirstObjectByType<GameStateManager>();
            int localIdx = mgr != null ? mgr.localCasterIndex : 0;
            return slotIndex == localIdx;
        }

        public void Reset()
        {
            _egg = null;
            _lastName = null;
        }
    }
}
