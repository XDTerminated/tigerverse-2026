using Tigerverse.Combat;
using Tigerverse.Net;
using UnityEngine;

namespace Tigerverse.Core
{
    /// <summary>
    /// Persistent boot-time component: wires up shared resources (BackendConfig, MoveCatalog)
    /// and ensures a GameStateManager exists for the rest of the session.
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        public BackendConfig backendConfig;
        public MoveCatalog moveCatalog;
        public GameStateManager gameStateManager;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (backendConfig == null)
            {
                backendConfig = BackendConfig.Load();
                if (backendConfig == null)
                {
                    Debug.LogWarning("[AppBootstrap] BackendConfig not found in Resources/. Configure backend URL/keys before play.");
                }
            }

            if (moveCatalog == null)
            {
                moveCatalog = MoveCatalog.Load();
                if (moveCatalog == null)
                {
                    Debug.LogWarning("[AppBootstrap] MoveCatalog not found in Resources/. Combat moves will be unavailable.");
                }
            }

            if (gameStateManager == null)
            {
                gameStateManager = GetComponent<GameStateManager>();
                if (gameStateManager == null)
                {
                    gameStateManager = gameObject.AddComponent<GameStateManager>();
                }
            }
        }
    }
}
