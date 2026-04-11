using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{

    enum Texts
    {
        Nickname,
    }

    enum Images
    {
        Speaker,
    }

    private const float DefaultSpeakerWidth = 28f;
    private const float SpeakerGap = 6f;
    private const float NicknameHorizontalPadding = 12f;

    [SerializeField] private Vector3 worldOffset = Vector3.zero;
    [SerializeField] private Vector2 canvasOffset = Vector2.zero;

    private string _text;
    private Transform _target;
    private Transform _anchorTarget;
    private RectTransform _selfRect;
    private RectTransform _textRect;
    private TextMeshProUGUI _textComponent;
    private RectTransform _speakerRect;
    private Image _speakerImage;
    private bool _isVoiceChatActive;

    public override void Init()
    {
        _target = transform.parent;

        transform.SetParent(Managers.UI._root.transform, false);
        Managers.UI.ShowCanvas(gameObject, false);

        _selfRect = gameObject.GetComponent<RectTransform>();
        Bind<TextMeshProUGUI>(typeof(Texts));
        Bind<Image>(typeof(Images));
        _textComponent = GetText((int)Texts.Nickname);
        _textRect = _textComponent != null ? _textComponent.rectTransform : null;
        _speakerImage = GetImage((int)Images.Speaker);
        _speakerRect = _speakerImage != null ? _speakerImage.rectTransform : null;

        if (_textComponent != null)
        {
            _textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            _textComponent.overflowMode = TextOverflowModes.Overflow;
            _textComponent.text = _text;
        }

        ApplySpeakerState();
        UpdateNicknameWidth();
        UpdateSpeakerPosition();
    }

    void LateUpdate()
    {
        if (_target == null || _selfRect == null)
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
        Vector3 worldAnchor = GetTargetTop(anchorTarget) + worldOffset;
        Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);

        if (screenPoint.z <= 0f)
        {
            Hide();
            return;
        }

        RectTransform parentRect = _selfRect.parent as RectTransform;
        if (parentRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, null, out Vector2 localPoint))
            _selfRect.anchoredPosition = localPoint + canvasOffset;
        else
            _selfRect.position = screenPoint + (Vector3)canvasOffset;

        if (_textComponent != null)
        {
            _textComponent.text = _text;
            UpdateSpeakerPosition();
        }

        ApplySpeakerState();
    }

    public void SetText(string text)
    {
        _text = text;
        if (_textComponent != null)
        {
            _textComponent.text = _text;
            UpdateNicknameWidth();
            UpdateSpeakerPosition();
        }
    }

    public void SetActive(bool isVoiceChatActive)
    {
        _isVoiceChatActive = isVoiceChatActive;
        UpdateSpeakerPosition();
    }

    public void SetVoiceChatActive(bool isActive)
    {
        SetActive(isActive);
    }

    public void SetAnchorTarget(Transform anchorTarget)
    {
        _anchorTarget = anchorTarget;
    }

    public void Hide() => gameObject.SetActive(false);
    public void Show() => gameObject.SetActive(true);

    private void ApplySpeakerState()
    {
        if (_speakerImage == null)
            return;

        _speakerImage.gameObject.SetActive(_isVoiceChatActive);
    }

    private void UpdateSpeakerPosition()
    {
        if (_speakerRect == null || _textRect == null)
            return;

        if (_textComponent == null)
            return;

        _textComponent.ForceMeshUpdate();
        float preferredWidth = Mathf.Max(_textComponent.preferredWidth, _textRect.rect.width);
        float speakerWidth = _speakerRect.rect.width > 0f ? _speakerRect.rect.width : _speakerRect.sizeDelta.x;
        if (speakerWidth <= 0f)
            speakerWidth = DefaultSpeakerWidth;

        float speakerX = _textRect.anchoredPosition.x - (preferredWidth * 0.5f) - SpeakerGap - (speakerWidth * 0.5f);
        _speakerRect.anchoredPosition = new Vector2(speakerX, _textRect.anchoredPosition.y);
    }

    private void UpdateNicknameWidth()
    {
        if (_textRect == null || _textComponent == null)
            return;

        _textComponent.ForceMeshUpdate();
        float preferredWidth = Mathf.Max(0f, _textComponent.preferredWidth);
        Vector2 size = _textRect.sizeDelta;
        size.x = preferredWidth + NicknameHorizontalPadding;
        _textRect.sizeDelta = size;
    }

    private static Vector3 GetTargetTop(Transform target)
    {
        if (target == null)
            return Vector3.zero;

        CharacterController characterController = target.GetComponent<CharacterController>();
        if (characterController != null)
            return characterController.center + Vector3.up * characterController.height;

        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
            return collider.bounds.center + Vector3.up * collider.bounds.extents.y;

        Renderer renderer = target.GetComponentInChildren<Renderer>();
        if (renderer != null)
            return renderer.bounds.center + Vector3.up * renderer.bounds.extents.y;

        return target.position;
    }
}

public interface IInteractGuideAnchorProvider
{
    Vector3 GetInteractGuideAnchorWorldPosition();
}
