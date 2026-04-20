using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TitanRoleManager
{
    private readonly Dictionary<Define.TitanRole, LobbyNetworkPlayer> _playersByRole = new();
    private readonly Dictionary<ulong, int> _roleMasksByClientId = new();

    public void Init()
    {
        Clear();
    }

    public void Clear()
    {
        _playersByRole.Clear();
        _roleMasksByClientId.Clear();
    }

    public bool RefreshRoleMap(bool requireAllRoles, out string error)
    {
        error = string.Empty;
        _playersByRole.Clear();
        _roleMasksByClientId.Clear();

        LobbyNetworkPlayer[] players = UnityEngine.Object.FindObjectsByType<LobbyNetworkPlayer>();
        if (players == null || players.Length == 0)
        {
            error = "No lobby players were found.";
            return false;
        }

        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player == null || !player.IsSpawned)
                continue;

            int roleMask = player.SelectedTitanRoleMaskValue;
            if (roleMask == 0)
                continue;

            _roleMasksByClientId[player.OwnerClientId] = roleMask;

            for (int roleValue = (int)Define.TitanRole.Body; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
            {
                int bit = 1 << (roleValue - (int)Define.TitanRole.Body);
                if ((roleMask & bit) == 0)
                    continue;

                Define.TitanRole role = (Define.TitanRole)roleValue;

                if (_playersByRole.TryGetValue(role, out LobbyNetworkPlayer existing)
                    && existing != null
                    && existing.OwnerClientId != player.OwnerClientId)
                {
                    error = $"Duplicate role selected: {role}";
                    return false;
                }

                _playersByRole[role] = player;
            }
        }

        if (requireAllRoles)
        {
            for (int roleValue = (int)Define.TitanRole.Body; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
            {
                Define.TitanRole role = (Define.TitanRole)roleValue;
                if (!_playersByRole.ContainsKey(role))
                {
                    error = $"Missing role owner: {role}";
                    return false;
                }
            }
        }

        return _playersByRole.Count > 0;
    }

    public bool TryGetLocalRole(out Define.TitanRole role)
    {
        role = Define.TitanRole.Body;
        if (!RefreshRoleMap(false, out _))
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            return false;

        if (!_roleMasksByClientId.TryGetValue(networkManager.LocalClientId, out int roleMask) || roleMask == 0)
            return false;

        for (int roleValue = (int)Define.TitanRole.Body; roleValue <= (int)Define.TitanRole.RightLeg; roleValue++)
        {
            int bit = 1 << (roleValue - (int)Define.TitanRole.Body);
            if ((roleMask & bit) != 0)
            {
                role = (Define.TitanRole)roleValue;
                return true;
            }
        }

        return false;
    }

    public bool TryGetRoleInput(Define.TitanRole role, out TitanAggregatedInput input)
    {
        input = default;
        if (!RefreshRoleMap(false, out _))
            return false;

        if (!_playersByRole.TryGetValue(role, out LobbyNetworkPlayer player) || player == null)
            return false;

        input = player.CurrentRoleInput.ToAggregatedInput();
        return true;
    }
}
