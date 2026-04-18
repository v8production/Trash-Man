using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UI_RoleSelectButton : UI_Base, ILobbyWorldButtonInteractionTarget
{
    private enum GameObjects
    {
        RoleSelectButton,
    }

    private enum Buttons
    {
        RoleSelectButton,
    }

    private enum Images
    {
        RoleSelectButton,
    }

    private enum Texts
    {
        Nickname,
    }

    [SerializeField] private Renderer[] _outlineRenderers;
    [SerializeField] private float _outlineTriggerDistance = 3.5f;
    [SerializeField] private float _outlineWidth = 0.0125f;
    [SerializeField] private Color _outlineColor = new(1f, 0.85f, 0.25f, 0.95f);
    [SerializeField] private Color _uiNormalColor = Color.white;
    [SerializeField] private Color _uiHighlightColor = new(1f, 0.95f, 0.55f, 1f);
    [SerializeField] private bool _pulseUiHighlight = true;
    [SerializeField] private float _uiPulseSpeed = 6f;
    [SerializeField] private string _labelText = "Role";

    private GameObject _buttonRoot;
    private Button _button;
    private Image[] _uiHighlightImages;
    private TextMeshProUGUI _label;
    private bool _isBound;
    private bool _isInitialized;
    private bool _outlineRegistered;
    private bool _hasOutlineRenderers;
    private bool _visibilityInitialized;
    private bool _lastVisibleState;
    private bool _isInteractableByProximity;
    private float _currentProximitySqrDistance = float.PositiveInfinity;

    public event Action RoleSelectButtonClicked;

    bool ILobbyWorldButtonInteractionTarget.IsProximityInteractable => _isInteractableByProximity;
    float ILobbyWorldButtonInteractionTarget.ProximitySqrDistance => _currentProximitySqrDistance;
    int ILobbyWorldButtonInteractionTarget.InteractionPriority => 1;

    public override void Init()
    {
        if (_isInitialized)
            return;

        Bind<GameObject>(typeof(GameObjects));
        Bind<Button>(typeof(Buttons));
        Bind<Image>(typeof(Images));
        Bind<TextMeshProUGUI>(typeof(Texts));

        _buttonRoot = GetObject((int)GameObjects.RoleSelectButton);
        _button = GetButton((int)Buttons.RoleSelectButton);
        _label = GetText((int)Texts.Nickname);

        if (_buttonRoot == null)
            _buttonRoot = gameObject;

        if (_button == null)
            _button = _buttonRoot.GetComponentInChildren<Button>(true);

        if (_label == null)
            _label = GetComponentInChildren<TextMeshProUGUI>(true);

        if (_outlineRenderers == null || _outlineRenderers.Length == 0)
            _outlineRenderers = GetComponentsInChildren<Renderer>(true);

        _uiHighlightImages = _buttonRoot != null
            ? _buttonRoot.GetComponentsInChildren<Image>(true)
            : GetComponentsInChildren<Image>(true);

        if (_label != null)
            _label.text = _labelText;

        BindButtonIfNeeded();
        RegisterOutlineTarget();
        LobbyWorldButtonInteractionRegistry.Register(this);
        SetOutlineVisible(false);
        SetUiHighlight(false);
        SetVisible(false);
        _isInitialized = true;
    }

    private void OnEnable()
    {
        if (!_isInitialized)
            Init();

        RefreshVisibility(true);
    }

    private void OnDisable()
    {
        _isInteractableByProximity = false;
        _currentProximitySqrDistance = float.PositiveInfinity;
        SetOutlineVisible(false);
        SetUiHighlight(false);
    }

    private void Update()
    {
        if (!_isInitialized)
            return;

        RefreshVisibility(false);
        RefreshProximityOutline();
        TryHandleDirectClick();
    }

    private void OnDestroy()
    {
        UnbindButton();
        RoleSelectButtonClicked = null;
        LobbyWorldButtonInteractionRegistry.Unregister(this);

        if (_outlineRegistered && Managers.Outline != null)
            Managers.Outline.UnregisterTarget(this);
    }

    private void BindButtonIfNeeded()
    {
        if (_isBound || _button == null)
            return;

        _button.onClick.AddListener(NotifyRoleSelectButtonClicked);
        _isBound = true;
    }

    private void UnbindButton()
    {
        if (!_isBound || _button == null)
            return;

        _button.onClick.RemoveListener(NotifyRoleSelectButtonClicked);
        _isBound = false;
    }

    private void NotifyRoleSelectButtonClicked()
    {
        RoleSelectButtonClicked?.Invoke();
    }

    private void RefreshVisibility(bool force)
    {
        bool isVisible = Managers.LobbySession != null && Managers.LobbySession.HasJoinedLobbySession;
        if (!force && _visibilityInitialized && _lastVisibleState == isVisible)
            return;

        SetVisible(isVisible);
        if (!isVisible)
        {
            _isInteractableByProximity = false;
            _currentProximitySqrDistance = float.PositiveInfinity;
            SetOutlineVisible(false);
            SetUiHighlight(false);
        }

        _lastVisibleState = isVisible;
        _visibilityInitialized = true;
    }

    private void RefreshProximityOutline()
    {
        if (Managers.LobbySession == null || !Managers.LobbySession.HasJoinedLobbySession)
        {
            _isInteractableByProximity = false;
            _currentProximitySqrDistance = float.PositiveInfinity;
            SetOutlineVisible(false);
            SetUiHighlight(false);
            return;
        }

        if (!Managers.LobbySession.TryGetLocalRangerTransform(out Transform rangerTransform) || rangerTransform == null)
        {
            _isInteractableByProximity = false;
            _currentProximitySqrDistance = float.PositiveInfinity;
            SetOutlineVisible(false);
            SetUiHighlight(false);
            return;
        }

        Vector3 targetPosition = _buttonRoot != null ? _buttonRoot.transform.position : transform.position;
        float triggerDistance = Mathf.Max(0.1f, _outlineTriggerDistance);
        float sqrDistance = (rangerTransform.position - targetPosition).sqrMagnitude;
        bool shouldHighlight = sqrDistance <= triggerDistance * triggerDistance;
        _isInteractableByProximity = shouldHighlight;
        _currentProximitySqrDistance = shouldHighlight ? sqrDistance : float.PositiveInfinity;

        SetOutlineVisible(shouldHighlight);
        SetUiHighlight(shouldHighlight);
    }

    private void TryHandleDirectClick()
    {
        if (!_isInteractableByProximity)
            return;

        if (!LobbyWorldButtonInteractionRegistry.IsClosestAvailable(this))
            return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
            return;

        NotifyRoleSelectButtonClicked();
    }

    private void RegisterOutlineTarget()
    {
        if (_outlineRegistered)
            return;

        _hasOutlineRenderers = HasAnyRenderer(_outlineRenderers);
        if (!_hasOutlineRenderers)
            return;

        Managers.Outline.RegisterTarget(this, _outlineRenderers, _outlineColor, _outlineWidth, false);
        _outlineRegistered = true;
    }

    private static bool HasAnyRenderer(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                return true;
        }

        return false;
    }

    private void SetOutlineVisible(bool visible)
    {
        if (!_hasOutlineRenderers || Managers.Outline == null)
            return;

        Managers.Outline.SetVisible(this, visible);
    }

    private void SetUiHighlight(bool highlighted)
    {
        if (_uiHighlightImages == null || _uiHighlightImages.Length == 0)
            return;

        Color targetColor;
        if (!highlighted)
        {
            targetColor = _uiNormalColor;
        }
        else if (_pulseUiHighlight)
        {
            float pulse = 0.5f + (Mathf.Sin(Time.unscaledTime * Mathf.Max(0.1f, _uiPulseSpeed)) * 0.5f);
            targetColor = Color.Lerp(_uiNormalColor, _uiHighlightColor, pulse);
        }
        else
        {
            targetColor = _uiHighlightColor;
        }

        for (int i = 0; i < _uiHighlightImages.Length; i++)
        {
            Image image = _uiHighlightImages[i];
            if (image == null)
                continue;

            image.color = targetColor;
        }
    }

    private void SetVisible(bool visible)
    {
        if (_buttonRoot == null)
            return;

        if (_buttonRoot.activeSelf != visible)
            _buttonRoot.SetActive(visible);
    }
}
