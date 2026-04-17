using System;
using UnityEngine;
using UnityEngine.UI;

public class UI_HostStartButton : MonoBehaviour
{
    [SerializeField] private GameObject _buttonRoot;
    [SerializeField] private Button _startButton;
    [SerializeField] private Renderer[] _outlineRenderers;
    [SerializeField] private float _outlineTriggerDistance = 3.5f;
    [SerializeField] private float _outlineWidth = 0.0125f;
    [SerializeField] private Color _outlineColor = new Color(1f, 0.85f, 0.25f, 0.95f);
    [SerializeField] private Image[] _uiHighlightImages;
    [SerializeField] private Color _uiNormalColor = Color.white;
    [SerializeField] private Color _uiHighlightColor = new Color(1f, 0.95f, 0.55f, 1f);
    [SerializeField] private bool _pulseUiHighlight = true;
    [SerializeField] private float _uiPulseSpeed = 6f;

    private bool _isBound;
    private bool _hostStateInitialized;
    private bool _lastHostState;
    private bool _hasOutlineRenderers;

    public event Action StartButtonClicked;

    private void Awake()
    {
        ResolveReferences();
        RegisterOutlineTarget();
        SetOutlineVisible(false);
        SetUiHighlight(false);
        SetVisible(false);
    }

    private void OnEnable()
    {
        ResolveReferences();
        RegisterOutlineTarget();
        RefreshVisibility(true);
    }

    private void OnDisable()
    {
        SetOutlineVisible(false);
        SetUiHighlight(false);
        SetVisible(false);
    }

    private void Update()
    {
        RefreshVisibility(false);
        RefreshProximityOutline();
    }

    private void OnDestroy()
    {
        UnbindButton();
        StartButtonClicked = null;

        if (Managers.Outline != null)
            Managers.Outline.UnregisterTarget(this);
    }

    private void ResolveReferences()
    {
        if (_buttonRoot == null)
            _buttonRoot = FindButtonRoot();

        if (_startButton == null)
            _startButton = FindStartButton();

        if (_outlineRenderers == null || _outlineRenderers.Length == 0)
            _outlineRenderers = GetComponentsInChildren<Renderer>(true);

        if (_uiHighlightImages == null || _uiHighlightImages.Length == 0)
        {
            if (_buttonRoot != null)
                _uiHighlightImages = _buttonRoot.GetComponentsInChildren<Image>(true);
            else
                _uiHighlightImages = GetComponentsInChildren<Image>(true);
        }

        if (!_isBound && _startButton != null)
        {
            _startButton.onClick.AddListener(NotifyStartButtonClicked);
            _isBound = true;
        }
    }

    private void RegisterOutlineTarget()
    {
        _hasOutlineRenderers = HasAnyRenderer(_outlineRenderers);
        if (!_hasOutlineRenderers)
            return;

        Managers.Outline.RegisterTarget(this, _outlineRenderers, _outlineColor, _outlineWidth, false);
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
        bool isHost = Managers.LobbySession != null && Managers.LobbySession.IsHosting;
        if (!force && _hostStateInitialized && _lastHostState == isHost)
            return;

        SetVisible(isHost);
        if (!isHost)
        {
            SetOutlineVisible(false);
            SetUiHighlight(false);
        }

        _lastHostState = isHost;
        _hostStateInitialized = true;
    }

    private void RefreshProximityOutline()
    {
        if (Managers.LobbySession == null || !Managers.LobbySession.IsHosting)
        {
            SetOutlineVisible(false);
            SetUiHighlight(false);
            return;
        }

        if (!Managers.LobbySession.TryGetLocalRangerTransform(out Transform rangerTransform) || rangerTransform == null)
        {
            SetOutlineVisible(false);
            SetUiHighlight(false);
            return;
        }

        Vector3 targetPosition = _buttonRoot != null ? _buttonRoot.transform.position : transform.position;
        float sqrDistance = (rangerTransform.position - targetPosition).sqrMagnitude;
        float triggerDistance = Mathf.Max(0.1f, _outlineTriggerDistance);
        bool shouldHighlight = sqrDistance <= triggerDistance * triggerDistance;

        SetOutlineVisible(shouldHighlight);
        SetUiHighlight(shouldHighlight);
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

    private GameObject FindButtonRoot()
    {
        Transform byUiName = transform.Find("UI_HostStartButton");
        if (byUiName != null)
            return byUiName.gameObject;

        Transform legacy = transform.Find("HostStartButton");
        if (legacy != null)
            return legacy.gameObject;

        if (transform.name == "UI_HostStartButton")
            return gameObject;

        Debug.LogWarning("[Lobby] UI_HostStartButton object is not assigned. Set _buttonRoot in inspector.");
        return null;
    }

    private Button FindStartButton()
    {
        if (_buttonRoot == null)
            return null;

        Transform startButtonRoot = _buttonRoot.transform.Find("StartButton");
        if (startButtonRoot != null)
        {
            Button direct = startButtonRoot.GetComponent<Button>();
            if (direct != null)
                return direct;
        }

        return _buttonRoot.GetComponentInChildren<Button>(true);
    }
}
