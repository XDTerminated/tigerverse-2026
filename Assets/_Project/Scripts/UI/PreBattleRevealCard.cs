using System.Collections;
using TMPro;
using Tigerverse.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Tigerverse.UI
{
    /// <summary>
    /// One-shot reveal card that fades in, displays monster stats + cry, then fades out.
    /// </summary>
    public class PreBattleRevealCard : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text elementText;
        [SerializeField] private TMP_Text flavorText;
        [SerializeField] private TMP_Text hp;
        [SerializeField] private Image[] moveIcons;
        [SerializeField] private Image elementBadge;
        [SerializeField] private CanvasGroup canvasGroup;

        private void Reset()
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        public IEnumerator Show(MonsterStatsSO stats, MonsterCry cry, float displaySec = 2f)
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            gameObject.SetActive(true);

            if (stats != null)
            {
                nameText?.SetText(stats.displayName);
                elementText?.SetText(stats.element.ToString());
                flavorText?.SetText(stats.flavorText);
                hp?.SetText($"HP {stats.maxHP}");

                if (elementBadge != null)
                {
                    elementBadge.color = stats.element.ToColor();
                }

                if (moveIcons != null && stats.moves != null)
                {
                    int n = Mathf.Min(moveIcons.Length, stats.moves.Length);
                    for (int i = 0; i < moveIcons.Length; i++)
                    {
                        if (moveIcons[i] == null)
                        {
                            continue;
                        }

                        if (i < n && stats.moves[i] != null)
                        {
                            moveIcons[i].gameObject.SetActive(true);
                            if (stats.moves[i].icon != null)
                            {
                                moveIcons[i].sprite = stats.moves[i].icon;
                            }
                            moveIcons[i].color = stats.moves[i].element.ToColor();
                        }
                        else
                        {
                            moveIcons[i].gameObject.SetActive(false);
                        }
                    }
                }
            }

            cry?.PlaySpawn();

            // Fade in
            canvasGroup.alpha = 0f;
            const float fadeIn = 0.3f;
            float t = 0f;
            while (t < fadeIn)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(t / fadeIn);
                yield return null;
            }
            canvasGroup.alpha = 1f;

            yield return new WaitForSeconds(displaySec);

            // Fade out
            const float fadeOut = 0.3f;
            t = 0f;
            while (t < fadeOut)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(t / fadeOut);
                yield return null;
            }
            canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }
    }
}
