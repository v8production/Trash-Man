using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct DiscordLobbyUser
{
    public DiscordLobbyUser(string userId, string displayName, bool isLocalUser)
    {
        UserId = userId;
        DisplayName = displayName;
        IsLocalUser = isLocalUser;
    }

    public string UserId { get; }
    public string DisplayName { get; }
    public bool IsLocalUser { get; }
}

public class DiscordManager
{
    private readonly Dictionary<string, DiscordLobbyUser> _members = new();
    private int _inviteSerial;

    public event Action<DiscordLobbyUser> OnLobbyUserJoined;
    public event Action<string> OnInviteRequested;
    public event Action<string> OnLocalDisplayNameChanged;

    public bool IsLinked { get; private set; }
    public string LocalUserId { get; private set; } = "local-user";
    public string LocalDisplayName { get; private set; } = "Player";

    public void Init()
    {
        if (IsLinked)
            return;

        string persistedName = PlayerPrefs.GetString("discord.local.nickname", string.Empty);
        string displayName = string.IsNullOrWhiteSpace(persistedName) ? Environment.UserName : persistedName;

        LinkLocalAccount(LocalUserId, displayName);
    }

    public void Clear()
    {
        _members.Clear();
        _inviteSerial = 0;
    }

    public void LinkLocalAccount(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Player";

        IsLinked = true;
        LocalUserId = userId;
        LocalDisplayName = displayName;

        PlayerPrefs.SetString("discord.local.nickname", LocalDisplayName);
        PlayerPrefs.Save();

        DiscordLobbyUser localUser = new(LocalUserId, LocalDisplayName, true);
        _members[LocalUserId] = localUser;

        OnLocalDisplayNameChanged?.Invoke(LocalDisplayName);
        OnLobbyUserJoined?.Invoke(localUser);
    }

    public void RequestFriendInvite(string friendDisplayName)
    {
        if (string.IsNullOrWhiteSpace(friendDisplayName))
            friendDisplayName = $"Friend{_inviteSerial + 1}";

        _inviteSerial++;
        string generatedUserId = $"friend-{_inviteSerial:D3}";

        OnInviteRequested?.Invoke(friendDisplayName);
        NotifyLobbyUserJoined(generatedUserId, friendDisplayName, false);
    }

    public void NotifyLobbyUserJoined(string userId, string displayName, bool isLocalUser)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = isLocalUser ? LocalDisplayName : "Friend";

        DiscordLobbyUser user = new(userId, displayName, isLocalUser);
        _members[userId] = user;
        OnLobbyUserJoined?.Invoke(user);
    }
}
