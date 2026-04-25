using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Tigerverse.Networking
{
    public class SessionManager : MonoBehaviour
    {
        public const int MaxPlayersPerRoom = 2;

        public static SessionManager Instance { get; private set; }

        public ISession CurrentSession { get; private set; }
        public string JoinCode => CurrentSession?.Code;

        public event Action<ISession> OnSessionJoined;
        public event Action OnSessionLeft;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async Task<ISession> CreateSessionAsync()
        {
            await WaitForBootstrapAsync();

            var options = new SessionOptions
            {
                MaxPlayers = MaxPlayersPerRoom
            }.WithRelayNetwork();

            CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            Debug.Log($"[SessionManager] Created session. Join code: {CurrentSession.Code}");
            OnSessionJoined?.Invoke(CurrentSession);
            return CurrentSession;
        }

        public async Task<ISession> JoinSessionByCodeAsync(string code)
        {
            await WaitForBootstrapAsync();

            CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            Debug.Log($"[SessionManager] Joined session {CurrentSession.Id} with code {code}");
            OnSessionJoined?.Invoke(CurrentSession);
            return CurrentSession;
        }

        public async Task LeaveSessionAsync()
        {
            if (CurrentSession == null) return;

            try
            {
                await CurrentSession.LeaveAsync();
            }
            finally
            {
                CurrentSession = null;
                OnSessionLeft?.Invoke();
            }
        }

        async Task WaitForBootstrapAsync()
        {
            while (!GameBootstrap.IsReady)
            {
                await Task.Yield();
            }
        }

        async void OnApplicationQuit()
        {
            await LeaveSessionAsync();
        }
    }
}
