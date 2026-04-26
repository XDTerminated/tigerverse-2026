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
        [Tooltip("UI elements (e.g. HOST/JOIN buttons, soft keyboard, title logo) hidden as soon as the local player joins/hosts a room. They stay hidden for the rest of the lobby phase.")]
        [SerializeField] private GameObject[] hideOnReady;
        [Tooltip("Optional: shrink/move the status label once ready (e.g. tuck into a corner).")]
        [SerializeField] private RectTransform shrinkTarget;
        [SerializeField] private Vector2 shrinkAnchoredPos = new Vector2(0, 460);
        [SerializeField] private Vector2 shrinkSize = new Vector2(680, 60);

        private NetworkRunner _runner;
        private bool _shrunk;
        private bool _hidden;

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

            // Hide the join UI (keyboard / logo / inputs) the instant we
            // ourselves are in a room, even if the opponent hasn't joined yet.
            if (!_hidden && n >= 1)
            {
                _hidden = true;
                if (hideOnReady != null)
                {
                    foreach (var go in hideOnReady)
                    {
                        if (go != null) go.SetActive(false);
                    }
                }
            }

            // While we wait for the opponent, show the room code on the
            // status label so the host can read it out loud.
            string code = _runner.SessionInfo != null ? _runner.SessionInfo.Name : null;
            if (ready)
            {
                label.text = $"Players: {n}/{requiredPlayers}, Ready! Look around, you should see your partner.";
            }
            else if (!string.IsNullOrEmpty(code))
            {
                label.text = $"Room <b>{code}</b>, waiting for opponent… ({n}/{requiredPlayers})";
            }
            else
            {
                label.text = $"Players: {n}/{requiredPlayers}, waiting…";
            }

            if (ready && !_shrunk)
            {
                _shrunk = true;
                // Once both players are in, kill the entire join menu, the
                // status label, the canvas itself, anything still on the
                // TitleCanvas, so nothing floats in the player's view
                // while combat / hatching begins.
                var canvas = GetComponent<Canvas>();
                if (canvas != null) canvas.gameObject.SetActive(false);
                else gameObject.SetActive(false);
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
