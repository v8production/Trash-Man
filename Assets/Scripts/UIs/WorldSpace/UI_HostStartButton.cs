using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class UI_HostStartButton : UI_Base, ILobbyWorldButtonInteractionTarget
{
    private enum GameObjects
    {
        StartButton,
    }

    private enum Buttons
    {
        StartButton,
    }

    private enum Images
    {
        StartButton,
    }

    [SerializeField] private Renderer[] _outlineRenderers;
    [SerializeField] private float _outlineTriggerDistance = 3.5f;
    [SerializeField] private float _outlineWidth = 0.0125f;
    [SerializeField] private Color _outlineColor = new Color(1f, 0.85f, 0.25f, 0.95f);
    [SerializeField] private Color _uiNormalColor = Color.white;
    [SerializeField] private Color _uiHighlightColor = new Color(1f, 0.95f, 0.55f, 1f);
    [SerializeField] private bool _pulseUiHighlight = true;
    [SerializeField] private float _uiPulseSpeed = 6f;

    private GameObject _buttonRoot;
    private Button _startButton;
    private Image[] _uiHighlightImages;

    private bool _isBound;
    private bool _isInitialized;
    private bool _outlineRegistered;
    private bool _hostStateInitialized;
    private bool _lastHostState;
    private bool _hasOutlineRenderers;
    private bool _isInteractableByProximity;
    private float _currentProximitySqrDistance = float.PositiveInfinity;

    public event Action StartButtonClicked;

    bool ILobbyWorldButtonInteractionTarget.IsProximityInteractable => _isInteractableByProximity;
    float ILobbyWorldButtonInteractionTarget.ProximitySqrDistance => _currentProximitySqrDistance;
    int ILobbyWorldButtonInteractionTarget.InteractionPriority => 0;

    public override void Init()
    {
        if (_isInitialized)
            return;

        Bind<GameObject>(typeof(GameObjects));
        Bind<Button>(typeof(Buttons));
        Bind<Image>(typeof(Images));

        _buttonRoot = GetObject((int)GameObjects.StartButton);
        _startButton = GetButton((int)Buttons.StartButton);

        if (_buttonRoot == null)
            _buttonRoot = FindButtonRoot();

        if (_startButton == null && _buttonRoot != null)
            _startButton = _buttonRoot.GetComponentInChildren<Button>(true);

        if (_outlineRenderers == null || _outlineRenderers.Length == 0)
            _outlineRenderers = GetComponentsInChildren<Renderer>(true);

        _uiHighlightImages = _buttonRoot != null
            ? _buttonRoot.GetComponentsInChildren<Image>(true)
            : GetComponentsInChildren<Image>(true);

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
        SetVisible(false);
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
        StartButtonClicked = null;
        LobbyWorldButtonInteractionRegistry.Unregister(this);

        if (_outlineRegistered)
            Managers.Outline.UnregisterTarget(this);
    }

    private void BindButtonIfNeeded()
    {
        if (_isBound || _startButton == null)
            return;

        _startButton.onClick.AddListener(NotifyStartButtonClicked);
        _isBound = true;
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

    private void UnbindButton()
    {
        if (!_isBound || _startButton == null)
            return;

        _startButton.onClick.RemoveListener(NotifyStartButtonClicked);
        _isBound = false;
    }

    private void NotifyStartButtonClicked()
    {
        StartButtonClicked?.Invoke();
    }

    private void RefreshVisibility(bool force)
    {
        bool isHost = Managers.LobbySession.IsHosting;
        if (!force && _hostStateInitialized && _lastHostState == isHost)
            return;

        SetVisible(isHost);
        if (!isHost)
        {
            _isInteractableByProximity = false;
            _currentProximitySqrDistance = float.PositiveInfinity;
            SetOutlineVisible(false);
            SetUiHighlight(false);
        }

        _lastHostState = isHost;
        _hostStateInitialized = true;
    }

    private void RefreshProximityOutline()
    {
        if (!Managers.LobbySession.IsHosting)
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
        float sqrDistance = (rangerTransform.position - targetPosition).sqrMagnitude;
        float triggerDistance = Mathf.Max(0.1f, _outlineTriggerDistance);
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

        NotifyStartButtonClicked();
    }

    private void SetOutlineVisible(bool visible)
    {
        if (!_hasOutlineRenderers)
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
        GameObject root = _buttonRoot != null ? _buttonRoot : gameObject;
        if (root.activeSelf != visible)
            root.SetActive(visible);
    }

    private GameObject FindButtonRoot()
    {
        Transform byEnumName = transform.Find(nameof(GameObjects.StartButton));
        if (byEnumName != null)
            return byEnumName.gameObject;

        Transform legacy = transform.Find("UI_HostStartButton");
        if (legacy != null)
            return legacy.gameObject;

        if (transform.name == "UI_HostStartButton")
            return gameObject;

        return null;
    }
}
