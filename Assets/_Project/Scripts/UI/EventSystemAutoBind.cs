using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Tigerverse.UI
{
    /// <summary>
    /// Repairs the EventSystem's UI input wiring at runtime so mouse/keyboard
    /// works for laptop play-mode testing AND XR controller pointer events still
    /// work in headset. The scene-serialized XRUIInputModule references an
    /// InputActionAsset that went missing during a recent overhaul — without a
    /// fix every UI button is dead. The "Tigerverse → UI → Wire EventSystem
    /// Input Actions" menu item rewires it for the editor only; this runtime
    /// path makes play mode just work without depending on that menu being run.
    /// </summary>
    public static class EventSystemAutoBind
    {
        private static InputActionAsset _asset;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnLoad()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Apply();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        private static void Apply()
        {
            var es = EventSystem.current != null
                ? EventSystem.current
                : Object.FindFirstObjectByType<EventSystem>();
            if (es == null) return;

            // Strip the broken XR module — its action asset reference is dangling
            // (m_ActionsAsset GUID points to nothing, so no actions ever fire).
            var xr = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            if (xr != null) Object.Destroy(xr);

            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = es.gameObject.AddComponent<InputSystemUIInputModule>();

            // If we've already bound on a previous scene load, the existing
            // module is already wired — just leave it.
            if (module.point != null && module.leftClick != null) return;

            BuildAndBind(module);
        }

        private static void BuildAndBind(InputSystemUIInputModule module)
        {
            if (_asset == null)
            {
                _asset = ScriptableObject.CreateInstance<InputActionAsset>();
                _asset.name = "TigerverseUIActions";
                var map = _asset.AddActionMap("UI");

                var point = map.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
                point.AddBinding("<Mouse>/position");
                point.AddBinding("<Pen>/position");
                point.AddBinding("<Touchscreen>/primaryTouch/position");

                var leftClick = map.AddAction("Click", InputActionType.PassThrough, expectedControlLayout: "Button");
                leftClick.AddBinding("<Mouse>/leftButton");
                leftClick.AddBinding("<Pen>/tip");
                leftClick.AddBinding("<Touchscreen>/primaryTouch/press");
                leftClick.AddBinding("<XRController>/{PrimaryAction}");

                var middleClick = map.AddAction("MiddleClick", InputActionType.PassThrough, expectedControlLayout: "Button");
                middleClick.AddBinding("<Mouse>/middleButton");

                var rightClick = map.AddAction("RightClick", InputActionType.PassThrough, expectedControlLayout: "Button");
                rightClick.AddBinding("<Mouse>/rightButton");

                var scroll = map.AddAction("ScrollWheel", InputActionType.PassThrough, expectedControlLayout: "Vector2");
                scroll.AddBinding("<Mouse>/scroll");

                var navigate = map.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
                navigate.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w").With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/s").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/a").With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/d").With("Right", "<Keyboard>/rightArrow");
                navigate.AddBinding("<Gamepad>/leftStick");
                navigate.AddBinding("<Gamepad>/dpad");

                var submit = map.AddAction("Submit", InputActionType.Button);
                submit.AddBinding("<Keyboard>/enter");
                submit.AddBinding("<Keyboard>/numpadEnter");
                submit.AddBinding("<Gamepad>/buttonSouth");

                var cancel = map.AddAction("Cancel", InputActionType.Button);
                cancel.AddBinding("<Keyboard>/escape");
                cancel.AddBinding("<Gamepad>/buttonEast");

                var trackedPos = map.AddAction("TrackedDevicePosition", InputActionType.PassThrough, expectedControlLayout: "Vector3");
                trackedPos.AddBinding("<XRController>/devicePosition");

                var trackedRot = map.AddAction("TrackedDeviceOrientation", InputActionType.PassThrough, expectedControlLayout: "Quaternion");
                trackedRot.AddBinding("<XRController>/deviceRotation");

                _asset.Enable();
            }

            var actionMap = _asset.FindActionMap("UI");
            module.point = InputActionReference.Create(actionMap.FindAction("Point"));
            module.leftClick = InputActionReference.Create(actionMap.FindAction("Click"));
            module.middleClick = InputActionReference.Create(actionMap.FindAction("MiddleClick"));
            module.rightClick = InputActionReference.Create(actionMap.FindAction("RightClick"));
            module.scrollWheel = InputActionReference.Create(actionMap.FindAction("ScrollWheel"));
            module.move = InputActionReference.Create(actionMap.FindAction("Navigate"));
            module.submit = InputActionReference.Create(actionMap.FindAction("Submit"));
            module.cancel = InputActionReference.Create(actionMap.FindAction("Cancel"));
            module.trackedDevicePosition = InputActionReference.Create(actionMap.FindAction("TrackedDevicePosition"));
            module.trackedDeviceOrientation = InputActionReference.Create(actionMap.FindAction("TrackedDeviceOrientation"));
        }
    }
}
