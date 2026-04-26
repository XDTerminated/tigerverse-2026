using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Lazy singleton that plays UI click sounds. Loads the click clip from
    /// Resources/Sfx/ButtonClick at first use, so no inspector wiring is
    /// required. Call <see cref="PlayClick"/> from any UI handler (e.g.
    /// TigerverseHoverFlip.OnPointerDown) to fire a one-shot click.
    /// </summary>
    public class UISfx : MonoBehaviour
    {
        private const string ClickResourcePath = "Sfx/ButtonClick";

        private static UISfx _instance;
        private AudioSource _source;
        private AudioClip _clickClip;

        public static UISfx Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var go = new GameObject("[UISfx]");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<UISfx>();
                _instance.Init();
                return _instance;
            }
        }

        private void Init()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // pure 2D UI sound
            _source.loop = false;
            _clickClip = Resources.Load<AudioClip>(ClickResourcePath);
            if (_clickClip == null)
            {
                Debug.LogWarning($"[UISfx] Could not load click clip at Resources/{ClickResourcePath}.wav");
            }
        }

        /// <summary>
        /// Play the standard UI click one-shot. Safe to call every frame; uses
        /// PlayOneShot so overlapping clicks layer rather than cut each other off.
        /// </summary>
        public static void PlayClick(float volume = 1f)
        {
            var inst = Instance;
            if (inst == null || inst._source == null || inst._clickClip == null) return;
            inst._source.PlayOneShot(inst._clickClip, volume);
        }
    }
}
