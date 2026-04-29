using System.Collections;
using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Light wander behaviour for the Professor: between scripted lines he
    /// drifts to random points within a constraint box around his anchor,
    /// playing the Animator's Walk bool while moving and Idle while paused.
    /// During practice-attack moments, ProfessorTutorial calls MoveToSide()
    /// to reposition him close to the player but offset to the right so
    /// he's visible without blocking the dummy / scribble line of sight.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProfessorWander : MonoBehaviour
    {
        [Header("Constraint box (centred on the anchor passed to Init)")]
        [SerializeField] private Vector3 boxSize = new Vector3(1.6f, 0f, 1.6f);

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 0.55f;
        [SerializeField] private float turnSpeed = 360f;
        [SerializeField] private float pauseMin  = 1.5f;
        [SerializeField] private float pauseMax  = 4.0f;
        [SerializeField] private float arriveDistance = 0.10f;

        private Animator _animator;
        private PaperProfessor _professor;
        private Vector3  _anchor;
        private bool     _enabled = true;
        private bool     _overridePos;
        private Vector3  _overrideTarget;
        private static readonly int WalkHash = Animator.StringToHash("Walk");

        public void Init(Vector3 worldAnchor, Animator animator, PaperProfessor professor = null)
        {
            _anchor = worldAnchor;
            _animator = animator;
            _professor = professor;
            StopAllCoroutines();
            StartCoroutine(WanderLoop());
        }

        public void SetEnabled(bool on) => _enabled = on;

        /// <summary>
        /// One-shot reposition. Walks the Professor to (player + camera-right *
        /// 1.5m) and pauses there until SetEnabled(true) or another override.
        /// </summary>
        public void MoveToSide()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 right = cam.transform.right; right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            // 1.5m offset from the player, slightly forward so he's actually
            // visible in their view without crowding the dummy.
            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _overrideTarget = cam.transform.position + right * 1.5f + fwd * 1.0f;
            _overrideTarget.y = transform.position.y;
            _overridePos = true;
        }

        public void ClearOverride() => _overridePos = false;

        private IEnumerator WanderLoop()
        {
            while (true)
            {
                if (!_enabled) { yield return null; continue; }

                // Don't walk while the Professor is speaking a scripted line —
                // pacing back and forth during dialogue feels distracting.
                // The override-position path still wins (close-but-side
                // reposition during practice attacks) so the player isn't
                // left waiting for him to stop talking.
                if (_professor != null && _professor.IsSpeaking && !_overridePos)
                {
                    SetWalkBool(false);
                    yield return null;
                    continue;
                }

                Vector3 target;
                if (_overridePos) target = _overrideTarget;
                else target = PickRandomPointInBox();

                yield return WalkTo(target);
                if (_overridePos)
                {
                    // Hold position until cleared.
                    while (_overridePos) yield return null;
                    continue;
                }

                SetWalkBool(false);
                yield return new WaitForSeconds(Random.Range(pauseMin, pauseMax));
            }
        }

        private Vector3 PickRandomPointInBox()
        {
            float hx = boxSize.x * 0.5f;
            float hz = boxSize.z * 0.5f;
            return _anchor + new Vector3(Random.Range(-hx, hx), 0f, Random.Range(-hz, hz));
        }

        private IEnumerator WalkTo(Vector3 worldTarget)
        {
            SetWalkBool(true);
            while (true)
            {
                Vector3 toTarget = worldTarget - transform.position;
                toTarget.y = 0f;
                float dist = toTarget.magnitude;
                if (dist <= arriveDistance) break;

                Vector3 dir = toTarget.normalized;
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);

                float step = Mathf.Min(dist, moveSpeed * Time.deltaTime);
                transform.position += dir * step;
                yield return null;
            }
            SetWalkBool(false);
        }

        private void SetWalkBool(bool walking)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                if (p.nameHash == WalkHash) { _animator.SetBool(WalkHash, walking); return; }
        }
    }
}
