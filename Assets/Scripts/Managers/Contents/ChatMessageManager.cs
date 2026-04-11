using System.Collections.Generic;
using UnityEngine;

public class ChatManager
{
    private const float OnceKeyRetentionSeconds = 180f;

    private readonly Queue<ChatRequest> _pendingChats = new();
    private readonly Dictionary<string, float> _registeredOnceKeyExpireTimes = new();
    private UI_Chat _activeChat;

    public void Init()
    {
        Clear();
    }

    public void Clear()
    {
        _pendingChats.Clear();
        _registeredOnceKeyExpireTimes.Clear();
        _activeChat = null;
    }

    public void EnqueueMessage(string message, float holdDuration = 5f)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _pendingChats.Enqueue(new ChatRequest(message, holdDuration));
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

        _pendingChats.Enqueue(new ChatRequest(message, holdDuration));
        TryPlayNext();
    }

    public void OnUpdate()
    {
        CleanupExpiredOnceKeys();

        if (_activeChat == null)
            _activeChat = Object.FindAnyObjectByType<UI_Chat>();

        if (_activeChat != null)
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
        if (_activeChat != null || _pendingChats.Count == 0)
            return;

        UI_Chat chat = Managers.UI.ShowSceneUI<UI_Chat>();
        if (chat == null)
            return;

        ChatRequest request = _pendingChats.Dequeue();
        chat.ShowBossMessage(request.Message, request.HoldDuration);
        _activeChat = chat;
    }

    private readonly struct ChatRequest
    {
        public ChatRequest(string message, float holdDuration)
        {
            Message = message;
            HoldDuration = holdDuration;
        }

        public string Message { get; }
        public float HoldDuration { get; }
    }
}
