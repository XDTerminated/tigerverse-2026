using System;
using UnityEngine;

namespace Tigerverse.Combat
{
    /// <summary>
    /// One-shot projectile spawned by <see cref="MonsterAimController.LaunchAttack"/>.
    /// Flies in a straight line at the configured speed; checks each frame
    /// whether it has reached the opponent monster (sphere overlap by
    /// distance). On hit it invokes the supplied callback (which the aim
    /// controller wires to <see cref="BattleManager.SubmitMove"/> to apply
    /// damage through the existing networked path) and self-destructs.
    /// On lifetime expiry without a hit, it self-destructs silently.
    ///
    /// Visual: instantiates the move's <see cref="MoveSO.vfxPrefab"/> as a
    /// child if assigned; otherwise a procedural emissive sphere. Audio:
    /// plays the move's <see cref="MoveSO.castSfx"/> at spawn and
    /// <see cref="MoveSO.hitSfx"/> at the impact site.
    /// </summary>
    public class MonsterProjectile : MonoBehaviour
    {
        private MoveSO _move;
        private int    _casterIndex;
        private Vector3 _direction;
        private Transform _opponent;
        private float  _speed;
        private float  _hitRadius;
        private float  _despawnAt;
        private bool   _hit;
        private Action<MoveSO, int> _onHit;

        public void Launch(MoveSO move, int casterIndex, Vector3 direction, Transform opponentTarget,
                          float speed, float lifetime, float hitRadius,
                          Action<MoveSO, int> onHit)
        {
            _move        = move;
            _casterIndex = casterIndex;
            _direction   = direction.sqrMagnitude > 1e-6f ? direction.normalized : transform.forward;
            _opponent    = opponentTarget;
            _speed       = speed;
            _hitRadius   = hitRadius;
            _despawnAt   = Time.time + Mathf.Max(0.1f, lifetime);
            _onHit       = onHit;

            BuildVisual(move);

            if (move != null && move.castSfx != null)
                AudioSource.PlayClipAtPoint(move.castSfx, transform.position);
        }

        private void BuildVisual(MoveSO move)
        {
            // Prefer the move's VFX prefab if it has one — same asset the
            // existing BattleManager.PlayMoveSequence uses, so both cast
            // paths look consistent.
            if (move != null && move.vfxPrefab != null)
            {
                var vfx = Instantiate(move.vfxPrefab, transform);
                vfx.transform.localPosition = Vector3.zero;
                vfx.transform.localRotation = Quaternion.identity;
                return;
            }

            // Procedural fallback: small emissive sphere tinted by element.
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "ProjectileVis";
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
            sphere.transform.SetParent(transform, worldPositionStays: false);
            sphere.transform.localScale = Vector3.one * 0.22f;

            var rend = sphere.GetComponent<Renderer>();
            if (rend != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    Color tint = ElementTint(move != null ? move.element : ElementType.Neutral);
                    mat.color = tint;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    rend.sharedMaterial = mat;
                }
            }
        }

        private static Color ElementTint(ElementType e)
        {
            switch (e)
            {
                case ElementType.Fire:     return new Color(1.0f, 0.45f, 0.20f, 1f);
                case ElementType.Water:    return new Color(0.30f, 0.60f, 1.00f, 1f);
                case ElementType.Electric: return new Color(1.0f, 0.95f, 0.30f, 1f);
                case ElementType.Earth:    return new Color(0.55f, 0.40f, 0.25f, 1f);
                case ElementType.Grass:    return new Color(0.40f, 0.85f, 0.40f, 1f);
                case ElementType.Ice:      return new Color(0.70f, 0.95f, 1.00f, 1f);
                case ElementType.Dark:     return new Color(0.45f, 0.25f, 0.55f, 1f);
                default:                   return new Color(1.0f, 0.95f, 0.85f, 1f);
            }
        }

        private void Update()
        {
            if (_hit) return;

            // Translate.
            transform.position += _direction * (_speed * Time.deltaTime);

            // Hit check vs opponent monster centre.
            if (_opponent != null)
            {
                float d = Vector3.Distance(transform.position, _opponent.position + Vector3.up * 0.5f);
                if (d <= _hitRadius)
                {
                    OnHit();
                    return;
                }
            }

            // Lifetime expiry — miss.
            if (Time.time >= _despawnAt)
            {
                Debug.Log($"[Projectile] '{(_move != null ? _move.displayName : "?")}' missed — despawn.");
                Destroy(gameObject);
            }
        }

        private void OnHit()
        {
            _hit = true;
            Debug.Log($"[Projectile] '{(_move != null ? _move.displayName : "?")}' HIT opponent — submitting move.");

            if (_move != null && _move.hitSfx != null)
                AudioSource.PlayClipAtPoint(_move.hitSfx, transform.position);

            try { _onHit?.Invoke(_move, _casterIndex); }
            catch (Exception e) { Debug.LogException(e); }

            // Tiny pause so the hit SFX gets a chance to play, then despawn.
            Destroy(gameObject, 0.15f);
        }
    }
}
