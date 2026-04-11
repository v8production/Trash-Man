using System.Collections.Generic;
using UnityEngine;

public class ToastManager
{
    private const float OnceKeyRetentionSeconds = 180f;

    private readonly Queue<ToastRequest> _pendingChats = new();
    private readonly Dictionary<string, float> _registeredOnceKeyExpireTimes = new();
    private UI_Toast _activeToast;

    public void Init()
    {
        Clear();
    }

    public void Clear()
    {
        _pendingChats.Clear();
        _registeredOnceKeyExpireTimes.Clear();
        _activeToast = null;
    }

    public void EnqueueMessage(string message, float holdDuration = 5f)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _pendingChats.Enqueue(new ToastRequest(message, holdDuration));
        TryPlayNext();
    }

    public void EnqueueMessageOnce(string messageKey, string message, float holdDuration = 5f)
    {
        if (string.IsNullOrWhiteSpace(messageKey) || string.IsNullOrWhiteSpace(message))
            return;

        CleanupExpiredOnceKeys();

        float now = Time.unscaledTime;
        if (_registeredOnceKeyExpireTimes.TryGetValue(messageKey, out float expiresAt) && now < expiresAt)
            return;

        _registeredOnceKeyExpireTimes[messageKey] = now + OnceKeyRetentionSeconds;

        _pendingChats.Enqueue(new ToastRequest(message, holdDuration));
        TryPlayNext();
    }

    public void OnUpdate()
    {
        CleanupExpiredOnceKeys();

        if (_activeToast == null)
            _activeToast = Object.FindAnyObjectByType<UI_Toast>();

        if (_activeToast != null)
            return;

        TryPlayNext();
    }

    private void CleanupExpiredOnceKeys()
    {
        if (_registeredOnceKeyExpireTimes.Count == 0)
            return;

        float now = Time.unscaledTime;
        List<string> expiredKeys = null;
        foreach (KeyValuePair<string, float> pair in _registeredOnceKeyExpireTimes)
        {
            if (now < pair.Value)
                continue;

            expiredKeys ??= new List<string>();
            expiredKeys.Add(pair.Key);
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
            _registeredOnceKeyExpireTimes.Remove(expiredKeys[i]);
    }

    private void TryPlayNext()
    {
        if (_activeToast != null || _pendingChats.Count == 0)
            return;

        UI_Toast chat = Managers.UI.ShowSceneUI<UI_Toast>();
        if (chat == null)
            return;

        ToastRequest request = _pendingChats.Dequeue();
        chat.ShowBossMessage(request.Message, request.HoldDuration);
        _activeToast = chat;
    }

    private readonly struct ToastRequest
    {
        public ToastRequest(string message, float holdDuration)
        {
            Message = message;
            HoldDuration = holdDuration;
        }

        public string Message { get; }
        public float HoldDuration { get; }
    }
}
