using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{
    private const string SpeakerGlyph = "🔊";
    private const float SpeakerWidth = 28f;
    private const float SpeakerGap = 6f;

    [SerializeField] private Vector3 worldOffset = Vector3.zero;
    [SerializeField] private Vector2 canvasOffset = Vector2.zero;

    private string _text;
    private Transform _target;
    private Transform _anchorTarget;
    private RectTransform _selfRect;
    private RectTransform _textRect;
    private TextMeshProUGUI _textComponent;
    private RectTransform _speakerRect;
    private TextMeshProUGUI _speakerComponent;
    private bool _isVoiceChatActive;

    enum Texts
    {
        Text,
    }

    public override void Init()
    {
        _target = transform.parent;

        transform.SetParent(Managers.UI._root.transform, false);
        Managers.UI.ShowCanvas(gameObject, false);

        _selfRect = gameObject.GetComponent<RectTransform>();
        Bind<TextMeshProUGUI>(typeof(Texts));
        _textComponent = Get<TextMeshProUGUI>((int)Texts.Text);
        _textRect = _textComponent != null ? _textComponent.rectTransform : null;
        _speakerComponent = EnsureSpeakerComponent();
        _speakerRect = _speakerComponent != null ? _speakerComponent.rectTransform : null;
        _textComponent.text = _text;
        ApplyVoiceChatState();
    }

    void LateUpdate()
    {
        if (_target == null || _textRect == null)
        {
            Hide();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Hide();
            return;
        }

        Transform anchorTarget = _anchorTarget != null ? _anchorTarget : _target;
        Vector3 worldAnchor = GetTargetCenter(anchorTarget) + worldOffset;
        Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);

        if (screenPoint.z <= 0f)
        {
            Hide();
            return;
        }

        RectTransform parentRect = _textRect.parent as RectTransform;
        if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, null, out Vector2 localPoint))
            _textRect.anchoredPosition = localPoint + canvasOffset;
        else
            _textRect.position = screenPoint + (Vector3)canvasOffset;

        if (_textComponent != null)
        {
            _textComponent.text = _text;
            UpdateSpeakerPosition();
        }
    }

    public void SetText(string text)
    {
        _text = text;
        if (_textComponent != null)
        {
            _textComponent.text = _text;
            UpdateSpeakerPosition();
        }
    }

    public void SetVoiceChatActive(bool isActive)
    {
        _isVoiceChatActive = isActive;
        ApplyVoiceChatState();
        UpdateSpeakerPosition();
    }

    public void SetAnchorTarget(Transform anchorTarget)
    {
        _anchorTarget = anchorTarget;
    }

    public void Hide() => gameObject.SetActive(false);
    public void Show() => gameObject.SetActive(true);

    private TextMeshProUGUI EnsureSpeakerComponent()
    {
        Transform existingSpeaker = transform.Find("Speaker");
        if (existingSpeaker != null)
            return existingSpeaker.GetComponent<TextMeshProUGUI>();

        if (_textComponent == null)
            return null;

        GameObject speakerObject = new("Speaker", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        speakerObject.transform.SetParent(_textComponent.transform.parent, false);

        TextMeshProUGUI speaker = speakerObject.GetComponent<TextMeshProUGUI>();
        speaker.text = SpeakerGlyph;
        speaker.font = _textComponent.font;
        speaker.fontSharedMaterial = _textComponent.fontSharedMaterial;
        speaker.fontSize = Mathf.Max(18f, _textComponent.fontSize * 0.85f);
        speaker.color = _textComponent.color;
        speaker.raycastTarget = false;
        speaker.alignment = TextAlignmentOptions.Center;
        speaker.enableWordWrapping = false;
        speaker.overflowMode = TextOverflowModes.Overflow;

        RectTransform rect = speaker.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(SpeakerWidth, _textRect != null ? _textRect.sizeDelta.y : SpeakerWidth);

        return speaker;
    }

    private void ApplyVoiceChatState()
    {
        if (_speakerComponent == null)
            return;

        _speakerComponent.text = SpeakerGlyph;
        _speakerComponent.gameObject.SetActive(_isVoiceChatActive);
    }

    private void UpdateSpeakerPosition()
    {
        if (_speakerRect == null || _textRect == null)
            return;

        _textComponent.ForceMeshUpdate();
        float preferredWidth = Mathf.Max(_textComponent.preferredWidth, _textRect.rect.width);
        float speakerX = _textRect.anchoredPosition.x - (preferredWidth * 0.5f) - SpeakerGap - (SpeakerWidth * 0.5f);
        _speakerRect.anchoredPosition = new Vector2(speakerX, _textRect.anchoredPosition.y);
    }

    private static Vector3 GetTargetCenter(Transform target)
    {
        if (target == null)
            return Vector3.zero;

        if (target.TryGetComponent(out IInteractGuideAnchorProvider anchorProvider))
            return anchorProvider.GetInteractGuideAnchorWorldPosition();

        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
            return collider.bounds.center;

        Collider colliderInChildren = target.GetComponentInChildren<Collider>();
        if (colliderInChildren != null)
            return colliderInChildren.bounds.center;

        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
            return renderer.bounds.center;

        return target.position;
    }
}

public interface IInteractGuideAnchorProvider
{
    Vector3 GetInteractGuideAnchorWorldPosition();
}
