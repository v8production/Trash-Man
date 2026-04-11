using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UI_Nickname : UI_Base
{

    enum Images
    {
        Speaker,
    }

    enum Texts
    {
        Nickname,
    }

    private const float DefaultSpeakerWidth = 28f;
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
        EnsureSpeakerRenderable();

        if (_textComponent != null)
            _textComponent.text = _text;

        ApplyVoiceChatState();
        UpdateSpeakerPosition();
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

    private void ApplyVoiceChatState()
    {
        if (_speakerImage == null)
            return;

        _speakerImage.gameObject.SetActive(_isVoiceChatActive);
    }

    private void EnsureSpeakerRenderable()
    {
        if (_speakerImage == null)
            return;

        if (_speakerImage.sprite != null)
            return;

        Sprite fallbackSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        if (fallbackSprite == null)
        {
            Debug.LogWarning("[LobbyVoice] UI_Nickname Speaker image has no sprite and fallback sprite is unavailable.");
            return;
        }

        _speakerImage.sprite = fallbackSprite;
        _speakerImage.type = Image.Type.Simple;
        _speakerImage.preserveAspect = true;
        _speakerImage.raycastTarget = false;
        Debug.LogWarning("[LobbyVoice] UI_Nickname Speaker image had no sprite. Applied built-in fallback sprite.");
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
