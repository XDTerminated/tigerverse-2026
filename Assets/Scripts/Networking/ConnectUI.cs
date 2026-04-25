using UnityEngine;

namespace Tigerverse.Networking
{
    public class ConnectUI : MonoBehaviour
    {
        string joinCodeInput = "";
        string status = "";
        bool busy;

        void OnGUI()
        {
            const int w = 360;
            const int h = 240;
            GUILayout.BeginArea(new Rect(20, 20, w, h), GUI.skin.box);

            GUILayout.Label("Tigerverse — 2-player room");

            if (!GameBootstrap.IsReady)
            {
                GUILayout.Label("Connecting to Unity Services…");
                GUILayout.EndArea();
                return;
            }

            var session = SessionManager.Instance != null
                ? SessionManager.Instance.CurrentSession
                : null;

            if (session != null)
            {
                GUILayout.Label($"In room. Code: {session.Code}");
                GUILayout.Label($"Players: {session.PlayerCount} / {SessionManager.MaxPlayersPerRoom}");
                GUI.enabled = !busy;
                if (GUILayout.Button("Leave"))
                {
                    Run(async () => await SessionManager.Instance.LeaveSessionAsync());
                }
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = !busy;
                if (GUILayout.Button("Host new room"))
                {
                    Run(async () => await SessionManager.Instance.CreateSessionAsync());
                }

                GUILayout.Space(8);
                GUILayout.Label("Join code:");
                joinCodeInput = GUILayout.TextField(joinCodeInput ?? "");
                if (GUILayout.Button("Join with code") && !string.IsNullOrWhiteSpace(joinCodeInput))
                {
                    var code = joinCodeInput.Trim().ToUpperInvariant();
                    Run(async () => await SessionManager.Instance.JoinSessionByCodeAsync(code));
                }
                GUI.enabled = true;
            }

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(6);
                GUILayout.Label(status);
            }

            GUILayout.EndArea();
        }

        async void Run(System.Func<System.Threading.Tasks.Task> op)
        {
            busy = true;
            status = "Working…";
            try
            {
                await op();
                status = "";
            }
            catch (System.Exception e)
            {
                status = $"Error: {e.Message}";
                Debug.LogException(e);
            }
            finally
            {
                busy = false;
            }
        }
    }
}
