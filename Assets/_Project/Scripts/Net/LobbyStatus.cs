#if FUSION2
using System.Linq;
using Fusion;
using TMPro;
using UnityEngine;

namespace Tigerverse.Net
{
    /// <summary>
    /// Updates a TMP label with the current lobby occupancy: "Players: N/2".
    /// </summary>
    public class LobbyStatus : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private int requiredPlayers = 2;
        [Tooltip("UI elements (e.g. HOST/JOIN buttons, soft keyboard) hidden once both players are in.")]
        [SerializeField] private GameObject[] hideOnReady;
        [Tooltip("Optional: shrink/move the status label once ready (e.g. tuck into a corner).")]
        [SerializeField] private RectTransform shrinkTarget;
        [SerializeField] private Vector2 shrinkAnchoredPos = new Vector2(0, 460);
        [SerializeField] private Vector2 shrinkSize = new Vector2(680, 60);

        private NetworkRunner _runner;
        private bool _shrunk;

        public void SetLabel(TMP_Text t) { label = t; }

        private void Update()
        {
            if (label == null) return;
            if (_runner == null || !_runner.IsRunning)
            {
                _runner = FindFirstObjectByType<NetworkRunner>();
                if (_runner == null || !_runner.IsRunning)
                {
                    return;
                }
            }
            int n = _runner.ActivePlayers.Count();
            bool ready = n >= requiredPlayers;
            label.text = ready
                ? $"Players: {n}/{requiredPlayers} — Ready! Look around — you should see your partner."
                : $"Players: {n}/{requiredPlayers} — waiting…";

            if (ready && !_shrunk)
            {
                _shrunk = true;
                if (hideOnReady != null)
                {
                    foreach (var go in hideOnReady)
                    {
                        if (go != null) go.SetActive(false);
                    }
                }
                if (shrinkTarget != null)
                {
                    shrinkTarget.sizeDelta = shrinkSize;
                    shrinkTarget.anchoredPosition = shrinkAnchoredPos;
                }
            }
        }
    }
}
#else
namespace Tigerverse.Net
{
    public class LobbyStatus : UnityEngine.MonoBehaviour { }
}
#endif
