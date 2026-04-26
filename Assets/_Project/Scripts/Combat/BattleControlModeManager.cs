using System;
using Tigerverse.Core;
using Tigerverse.UI;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

namespace Tigerverse.Combat
{
    /// <summary>
    /// Owns the local player's <see cref="BattleControlMode"/> during the
    /// battle phase. Press A on the right controller (or T in the editor) to
    /// toggle. The trainer's locomotion is locked in BOTH modes — what
    /// changes is whether the joystick steers the local scribble (Scribble
    /// mode) and whether voice commands can fire attacks (Trainer mode).
    ///
    /// Created on demand by <c>GameStateManager</c> when the battle starts;
    /// torn down when the battle ends.
    /// </summary>
    public class BattleControlModeManager : MonoBehaviour
    {
        public static BattleControlModeManager Instance { get; private set; }

        public event Action<BattleControlMode> OnModeChanged;

        public BattleControlMode CurrentMode { get; private set; } = BattleControlMode.Trainer;

        /// <summary>True when voice commands are allowed to submit attacks.</summary>
        public bool CanAttack => CurrentMode == BattleControlMode.Trainer;

        // Refs supplied by GameStateManager when battle starts.
        private ContinuousMoveProvider _xrMoveProvider;
        private FlatMoveController _editorMoveController;
        private ScribbleMoveController _scribbleMover;
        private BattleModeOverlay _overlay;

        private bool _primaryWas;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Restore movement so menus / lobby work after battle ends.
            if (_xrMoveProvider != null) _xrMoveProvider.enabled = true;
            if (_editorMoveController != null) _editorMoveController.enabled = true;
        }

        /// <summary>
        /// Wire the manager to the local rig + scribble. Must be called once
        /// after the manager is created. Disables locomotion immediately and
        /// applies the default Trainer mode.
        /// </summary>
        public void Configure(
            ContinuousMoveProvider xrMoveProvider,
            FlatMoveController editorMoveController,
            ScribbleMoveController scribbleMover,
            BattleModeOverlay overlay)
        {
            _xrMoveProvider = xrMoveProvider;
            _editorMoveController = editorMoveController;
            _scribbleMover = scribbleMover;
            _overlay = overlay;

            // Lock the trainer's body for the rest of the battle. Continuous
            // move stays disabled in BOTH modes — only the scribble moves
            // with the joystick.
            if (_xrMoveProvider != null) _xrMoveProvider.enabled = false;
            if (_editorMoveController != null) _editorMoveController.enabled = false;

            ApplyMode(announce: true);
        }

        private void Update()
        {
            // A on right Quest controller, or M in the editor (T is taken by
            // the XR Device Simulator for device-focus toggling).
            bool pressed = ReadAButtonEdge();
#if UNITY_EDITOR
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame) pressed = true;
#endif
            if (pressed) Toggle();
        }

        public void Toggle()
        {
            CurrentMode = CurrentMode == BattleControlMode.Trainer
                ? BattleControlMode.Scribble
                : BattleControlMode.Trainer;
            ApplyMode(announce: true);
        }

        public void SetMode(BattleControlMode mode)
        {
            if (CurrentMode == mode) return;
            CurrentMode = mode;
            ApplyMode(announce: true);
        }

        private void ApplyMode(bool announce)
        {
            // Scribble mover only listens to joystick in Scribble mode.
            if (_scribbleMover != null)
                _scribbleMover.enabled = (CurrentMode == BattleControlMode.Scribble);

            if (announce && _overlay != null) _overlay.Show(CurrentMode);
            OnModeChanged?.Invoke(CurrentMode);
        }

        private bool ReadAButtonEdge()
        {
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!dev.isValid) { _primaryWas = false; return false; }
            bool isPressed = false;
            dev.TryGetFeatureValue(CommonUsages.primaryButton, out isPressed);
            bool edge = isPressed && !_primaryWas;
            _primaryWas = isPressed;
            return edge;
        }
    }
}
