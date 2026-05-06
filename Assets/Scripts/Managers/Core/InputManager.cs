using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager
{
    private InputActionAsset _asset;
    private string _playerMapName = "Player";
    private string _uiMapName = "UI";
    private const string LookActionName = "Look";
    private bool _hasVirtualMousePosition;
    private Vector2 _virtualMousePosition;
    private Vector2Int _virtualMouseScreenSize;
    private CursorLockMode _lastCursorLockMode;
    private float _nextTitanInputLogTime;
    private const float TitanInputLogIntervalSeconds = 0.20f;
    private const bool UseTitanSingleAxisCorrection = false;
    private const float TitanSingleAxisDeadZonePixels = 2f;
    private const float TitanHorizontalAssistBiasPixels = 18f;
    private const float TitanMouseSensitivity = 5f;

    public Define.InputMode Mode { get; private set; } = Define.InputMode.Player;

    public InputActionMap PlayerMap;
    public InputActionMap UIMap;

    public float GetTitanMouseSensitivity()
    {
        return TitanMouseSensitivity;
    }

    public void ResetTitanMouseBaseline()
    {
        // Used when switching active titan control roles.
        // Without this, the virtual mouse position (cursor-locked mode) carries over
        // and absolute mouse-to-pose mappings can snap immediately on role activation.
        _hasVirtualMousePosition = false;
    }

    private InputAction _lookAction;

    public void Init()
    {
        _asset = Managers.Resource.Load<InputActionAsset>("InputSystem_Actions");
        PlayerMap = _asset.FindActionMap(_playerMapName, throwIfNotFound: true);
        UIMap = _asset.FindActionMap(_uiMapName, throwIfNotFound: true);
        _lookAction = PlayerMap.FindAction(LookActionName, throwIfNotFound: false);
        SetMode(Define.InputMode.UI);
    }

    public void SetMode(Define.InputMode mode)
    {
        Mode = mode;

        // Disable everything first (clean slate)
        PlayerMap.Disable();
        UIMap.Disable();

        switch (mode)
        {
            case Define.InputMode.Player:
                PlayerMap.Enable();
                // Keep UI map enabled so the first UI interaction after a mode switch
                // (e.g., opening a menu then clicking immediately) is not dropped.
                UIMap.Enable();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                break;

            case Define.InputMode.UI:
                UIMap.Enable();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;

            case Define.InputMode.Cinematic:
                // none enabled
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }

    public TitanAggregatedInput CaptureTitanInput()
    {
        TitanAggregatedInput input = default;

        input.MouseDelta = ReadTitanMouseDelta();
        input.MousePosition = ReadMousePosition();
        input.MousePosition = CorrectTitanMousePosition(input.MousePosition);
        input.RightMouseHeld = IsRightMouseHeld();
        input.RightMousePressedThisFrame = WasRightMousePressedThisFrame();
        input.TorsoForward = GetAxis(Key.UpArrow, Key.DownArrow);
        input.TorsoStrafe = GetAxis(Key.RightArrow, Key.LeftArrow);
        input.TorsoTurn = GetAxis(Key.Period, Key.Comma);
        input.TorsoWaist = GetAxis(Key.D, Key.A);

        float ws = GetAxis(Key.W, Key.S);
        bool shiftHeld = IsShiftHeld();
        input.LeftArmElbow = ws;
        input.RightArmElbow = ws;
        input.LeftLegKnee = shiftHeld ? 0f : ws;
        input.RightLegKnee = shiftHeld ? 0f : ws;
        input.LeftLegAnkle = shiftHeld ? ws : 0f;
        input.RightLegAnkle = shiftHeld ? ws : 0f;

        MaybeLogTitanInput(input);
        return input;
    }

    public Vector2 ReadTitanMouseDelta()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return Vector2.zero;

        return mouse.delta.ReadValue();
    }

    public Vector2 ReadPlayerLookInput()
    {
        if (_lookAction != null)
            return _lookAction.ReadValue<Vector2>();

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return Vector2.zero;

        return mouse.delta.ReadValue();
    }

    public bool IsRightMouseHeld()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.isPressed;
    }

    public bool WasRightMousePressedThisFrame()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && mouse.rightButton.wasPressedThisFrame;
    }

    private bool IsShiftHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    private Vector2 CorrectTitanMousePosition(Vector2 mousePosition)
    {
        if (!UseTitanSingleAxisCorrection)
            return mousePosition;

        Vector2 origin = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 fromOrigin = mousePosition - origin;
        Vector2 dominant = TitanInputUtility.KeepDominantAxisWithHorizontalBias(
            fromOrigin,
            TitanSingleAxisDeadZonePixels,
            TitanHorizontalAssistBiasPixels);
        return origin + dominant;
    }

    public Vector2 ReadMousePosition()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return Vector2.zero;

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            _hasVirtualMousePosition = false;
            _lastCursorLockMode = Cursor.lockState;
            return mouse.position.ReadValue();
        }

        Vector2Int screenSize = new(Screen.width, Screen.height);
        if (!_hasVirtualMousePosition
            || _virtualMouseScreenSize != screenSize
            || _lastCursorLockMode != CursorLockMode.Locked)
        {
            _virtualMousePosition = new Vector2(screenSize.x * 0.5f, screenSize.y * 0.5f);
            _virtualMouseScreenSize = screenSize;
            _hasVirtualMousePosition = true;
        }

        _virtualMousePosition += mouse.delta.ReadValue();
        _virtualMousePosition.x = Mathf.Clamp(_virtualMousePosition.x, 0f, screenSize.x);
        _virtualMousePosition.y = Mathf.Clamp(_virtualMousePosition.y, 0f, screenSize.y);
        _lastCursorLockMode = CursorLockMode.Locked;
        return _virtualMousePosition;
    }

    public float GetAxis(Key positive, Key negative)
    {
        float axis = 0f;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return 0f;

        if (keyboard[positive].isPressed)
            axis += 1f;

        if (keyboard[negative].isPressed)
            axis -= 1f;

        return axis;
    }

    public bool WasDigitPressedThisFrame(int digitOneToFive)
    {
        switch (digitOneToFive)
        {
            case 1:
                return WasPressedThisFrame(Key.Digit1);
            case 2:
                return WasPressedThisFrame(Key.Digit2);
            case 3:
                return WasPressedThisFrame(Key.Digit3);
            case 4:
                return WasPressedThisFrame(Key.Digit4);
            case 5:
                return WasPressedThisFrame(Key.Digit5);
            default:
                return false;
        }
    }

    private bool WasPressedThisFrame(Key key)
    {
        return Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
    }

    private void MaybeLogTitanInput(in TitanAggregatedInput input)
    {
        if (!InputDebug.Enabled)
            return;

        if (Time.unscaledTime < _nextTitanInputLogTime)
            return;

        bool hasWs = Mathf.Abs(input.LeftArmElbow) > 0.001f;
        bool hasWaist = Mathf.Abs(input.TorsoWaist) > 0.001f;
        bool hasArrows = Mathf.Abs(input.TorsoForward) > 0.001f || Mathf.Abs(input.TorsoStrafe) > 0.001f;
        bool hasTurn = Mathf.Abs(input.TorsoTurn) > 0.001f;

        if (!hasWs && !hasWaist && !hasArrows && !hasTurn)
            return;

        _nextTitanInputLogTime = Time.unscaledTime + TitanInputLogIntervalSeconds;
        InputDebug.Log($"Managers.Input arrows(fwd={input.TorsoForward}, strafe={input.TorsoStrafe}) turn={input.TorsoTurn} waist(A/D)={input.TorsoWaist} ws(W/S)={input.LeftArmElbow}");
    }
}
