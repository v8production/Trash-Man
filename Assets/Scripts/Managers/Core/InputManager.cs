using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager
{
    private InputActionAsset _asset;
    private string _playerMapName = "Player";
    private string _uiMapName = "UI";

    public Define.InputMode Mode { get; private set; } = Define.InputMode.Player;

    public InputActionMap PlayerMap;
    public InputActionMap UIMap;

    public void Init()
    {
        _asset = Managers.Resource.Load<InputActionAsset>("InputSystem_Actions");
        PlayerMap = _asset.FindActionMap(_playerMapName, throwIfNotFound: true);
        UIMap = _asset.FindActionMap(_uiMapName, throwIfNotFound: true);
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
}
