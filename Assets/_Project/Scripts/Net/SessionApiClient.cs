using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Tigerverse.Net
{
    [Serializable]
    public class SessionData
    {
        public string code;
        public PlayerData p1;
        public PlayerData p2;

        public bool BothReady => p1 != null && p2 != null
            && p1.status == "ready" && p2.status == "ready"
            && !string.IsNullOrEmpty(p1.glbUrl) && !string.IsNullOrEmpty(p2.glbUrl);
    }

    [Serializable]
    public class PlayerData
    {
        public string status;       // queued|generating|rigging|cry|ready|error
        public string name;
        public string imageUrl;
        public string glbUrl;
        public string cryUrl;
        public MonsterStatsData stats;
    }

    [Serializable]
    public class MonsterStatsData
    {
        public int hp;
        public float attackMult;
        public float speed;
        public string element;       // "fire","water","electric","earth","grass","ice","dark","neutral"
        public string[] moves;       // 3 entries (Dodge auto-added downstream)
        public string flavorText;
    }

    public class SessionApiClient : MonoBehaviour
    {
        [SerializeField] private BackendConfig config;

        private bool _polling;
        private SessionData _lastSession;

        public bool IsPolling => _polling;
        public SessionData LastSession => _lastSession;

        private void Awake()
        {
            if (config == null)
            {
                config = BackendConfig.Load();
            }
        }

        public void StopPolling()
        {
            _polling = false;
        }

        public IEnumerator FetchSession(string code, Action<SessionData, string> onComplete)
        {
            if (config == null) config = BackendConfig.Load();

            if (config.useMock)
            {
                // Mock mode: instant single-shot fetch from Resources/MockSession.json
                yield return FetchMockSession(code, onComplete);
                yield break;
            }

            string url = config.backendBaseUrl.TrimEnd('/') + "/api/session/" + UnityWebRequest.EscapeURL(code);
            Debug.Log($"[SessionApiClient] GET {url}");
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 10;
                req.SetRequestHeader("Accept", "application/json");
                float t0 = Time.realtimeSinceStartup;
                yield return req.SendWebRequest();
                float dt = Time.realtimeSinceStartup - t0;

#if UNITY_2020_1_OR_NEWER
                bool isError = req.result != UnityWebRequest.Result.Success;
#else
                bool isError = req.isNetworkError || req.isHttpError;
#endif
                Debug.Log($"[SessionApiClient] RESP {url} after {dt:F2}s → HTTP {req.responseCode} result={req.result}");

                if (isError || req.responseCode >= 400)
                {
                    if (req.responseCode == 404)
                    {
                        // Expected until at least one player visits the join URL
                        // on the website. Don't spam Warnings for this — it's
                        // the steady-state while waiting for the first submit.
                        Debug.Log($"[SessionApiClient] 404 — backend has no session for this code yet. Open '{config.backendBaseUrl.TrimEnd('/')}/join/{code}?p=1' (or p=2 for the joiner) in a browser and submit a drawing to create the session.");
                    }
                    else
                    {
                        Debug.LogWarning($"[SessionApiClient] GET {url} → HTTP {req.responseCode} result={req.result} err='{req.error}'");
                    }
                    onComplete?.Invoke(null, $"HTTP {req.responseCode}: {req.error}");
                    yield break;
                }

                string text = req.downloadHandler != null ? req.downloadHandler.text : null;
                Debug.Log($"[SessionApiClient] body length = {(text == null ? "null" : text.Length + " chars")}");
                if (string.IsNullOrEmpty(text))
                {
                    onComplete?.Invoke(null, "Empty response body");
                    yield break;
                }

                SessionData data = null;
                string parseError = null;
                try
                {
                    data = JsonConvert.DeserializeObject<SessionData>(text);
                }
                catch (Exception e)
                {
                    parseError = e.Message;
                }

                if (data == null)
                {
                    onComplete?.Invoke(null, $"Parse error: {parseError ?? "null"}");
                    yield break;
                }

                _lastSession = data;
                onComplete?.Invoke(data, null);
            }
        }

        public IEnumerator PollUntilReady(string code, Action<SessionData> onUpdate, Action<SessionData> onBothReady)
        {
            if (config == null) config = BackendConfig.Load();

            // Cancel any previously-running poll loop so we don't end up
            // with N parallel coroutines firing GETs every interval. Each
            // press of the Host / Join button calls BeginDrawWait which
            // calls this — without this guard the old loops keep going
            // forever, multiplying request volume by N each press.
            if (_polling)
            {
                Debug.LogWarning($"[SessionApiClient] PollUntilReady called while a previous poll loop was still running. Stopping the old one.");
                _polling = false;
                // Yield one frame so the old loop sees _polling=false and exits cleanly.
                yield return null;
            }
            _polling = true;

            if (config.useMock)
            {
                // Mock mode: 5-second progression queued -> generating -> rigging -> cry -> ready.
                yield return MockProgressionPoll(code, onUpdate, onBothReady);
                yield break;
            }

            Debug.Log($"[SessionApiClient] Starting poll for code='{code}' against {config.backendBaseUrl}");
            int pollNum = 0;

            while (_polling)
            {
                pollNum++;
                bool gotResponse = false;
                SessionData latest = null;
                string err = null;

                yield return FetchSession(code, (d, e) =>
                {
                    latest = d;
                    err = e;
                    gotResponse = true;
                });

                if (!_polling) yield break;

                if (gotResponse && latest != null)
                {
                    _lastSession = latest;
                    string s1 = latest.p1 != null ? (latest.p1.status ?? "?") : "null";
                    string s2 = latest.p2 != null ? (latest.p2.status ?? "?") : "null";
                    Debug.Log($"[SessionApiClient] poll #{pollNum} p1={s1} p2={s2} bothReady={latest.BothReady}");
                    onUpdate?.Invoke(latest);
                    if (latest.BothReady)
                    {
                        _polling = false;
                        onBothReady?.Invoke(latest);
                        yield break;
                    }
                }
                else if (!string.IsNullOrEmpty(err))
                {
                    Debug.LogWarning($"[SessionApiClient] poll #{pollNum} fetch error: {err}");
                }
                else
                {
                    Debug.LogWarning($"[SessionApiClient] poll #{pollNum} returned no data and no error");
                }

                float wait = Mathf.Max(0.25f, config.pollIntervalSec);
                yield return new WaitForSeconds(wait);
            }
        }

        // ---------- Mock helpers ----------

        private SessionData _cachedMock;

        private IEnumerator FetchMockSession(string code, Action<SessionData, string> onComplete)
        {
            if (_cachedMock == null)
            {
                yield return LoadMockFromResources();
            }

            if (_cachedMock == null)
            {
                onComplete?.Invoke(null, "MockSession.json missing in Resources/");
                yield break;
            }

            // Clone code into the mock so callers can verify.
            _cachedMock.code = code;
            _lastSession = _cachedMock;
            onComplete?.Invoke(_cachedMock, null);
        }

        private IEnumerator LoadMockFromResources()
        {
            var ta = Resources.Load<TextAsset>("MockSession");
            if (ta == null)
            {
                Debug.LogWarning("[SessionApiClient] Resources/MockSession.json not found.");
                yield break;
            }
            try
            {
                _cachedMock = JsonConvert.DeserializeObject<SessionData>(ta.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[SessionApiClient] Failed to parse MockSession.json: " + e.Message);
            }
        }

        private IEnumerator MockProgressionPoll(string code, Action<SessionData> onUpdate, Action<SessionData> onBothReady)
        {
            if (_cachedMock == null) yield return LoadMockFromResources();
            if (_cachedMock == null)
            {
                Debug.LogError("[SessionApiClient] Mock session missing; cannot run mock progression.");
                _polling = false;
                yield break;
            }

            // Snapshot the final ready data.
            var finalData = _cachedMock;
            string[] phases = { "queued", "generating", "rigging", "cry", "ready" };
            float perStep = 1.0f; // 5 phases x 1s = 5s total

            for (int i = 0; i < phases.Length && _polling; i++)
            {
                var snap = new SessionData
                {
                    code = code,
                    p1 = ClonePartial(finalData.p1, phases[i], i == phases.Length - 1),
                    p2 = ClonePartial(finalData.p2, phases[i], i == phases.Length - 1)
                };

                _lastSession = snap;
                onUpdate?.Invoke(snap);

                if (snap.BothReady)
                {
                    _polling = false;
                    onBothReady?.Invoke(snap);
                    yield break;
                }

                yield return new WaitForSeconds(perStep);
            }

            _polling = false;
        }

        private static PlayerData ClonePartial(PlayerData src, string status, bool reveal)
        {
            if (src == null) return null;
            var p = new PlayerData
            {
                status = status,
                name = reveal ? src.name : null,
                imageUrl = reveal ? src.imageUrl : null,
                glbUrl = reveal ? src.glbUrl : null,
                cryUrl = reveal ? src.cryUrl : null,
                stats = reveal ? src.stats : null
            };
            return p;
        }
    }
}
