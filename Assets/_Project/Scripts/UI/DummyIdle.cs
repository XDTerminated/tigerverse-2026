using UnityEngine;

namespace Tigerverse.UI
{
    /// <summary>
    /// Procedural idle for the practice dummy. CharacterBase.fbx ships with
    /// no animations, so without this it would stand frozen in T-pose.
    /// Drives a subtle vertical bob + side-to-side sway on a child model
    /// transform so it reads as alive without overlapping any animator.
    /// </summary>
    [DisallowMultipleComponent]
    public class DummyIdle : MonoBehaviour
    {
        [SerializeField] private float bobAmplitude = 0.018f;
        [SerializeField] private float bobHz        = 0.55f;
        [SerializeField] private float swayDeg      = 2.5f;
        [SerializeField] private float swayHz       = 0.35f;

        private Transform _model;
        private Vector3   _baseLocalPos;
        private Quaternion _baseLocalRot;
        private float     _phase;

        public void Bind(Transform model)
        {
            _model = model;
            if (_model == null) return;
            _baseLocalPos = _model.localPosition;
            _baseLocalRot = _model.localRotation;
            _phase = Random.Range(0f, 10f); // desync from any sibling dummy
        }

        private void Update()
        {
            if (_model == null) return;
            _phase += Time.deltaTime;

            Vector3 p = _baseLocalPos;
            p.y += Mathf.Sin(_phase * bobHz * Mathf.PI * 2f) * bobAmplitude;
            _model.localPosition = p;

            float swayZ = Mathf.Sin(_phase * swayHz * Mathf.PI * 2f) * swayDeg;
            _model.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, swayZ);
        }
    }
}
