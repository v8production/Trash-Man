using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyDiscordBootstrap : MonoBehaviour
{
    private const string LobbySceneName = "LobbyScene";
    private const string RangerResourcePath = "Arts/Ranger/normal man a";

    private readonly Dictionary<string, RangerController> _rangersByUser = new();
    private Transform _spawnCenter;
    private int _spawnSerial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnSceneLoad()
    {
        if (SceneManager.GetActiveScene().name != LobbySceneName)
            return;

        if (FindAnyObjectByType<LobbyDiscordBootstrap>() != null)
            return;

        GameObject runner = new("@LobbyDiscordBootstrap");
        runner.GetorAddComponent<LobbyDiscordBootstrap>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += HandleSceneChanged;
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != LobbySceneName)
        {
            gameObject.SetActive(false);
            return;
        }

        InitializeLobbySystems();
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= HandleSceneChanged;
        Managers.Discord.OnLobbyUserJoined -= HandleLobbyUserJoined;
    }

    private void HandleSceneChanged(Scene previous, Scene current)
    {
        if (current.name != LobbySceneName)
        {
            Managers.Discord.OnLobbyUserJoined -= HandleLobbyUserJoined;
            _rangersByUser.Clear();
            return;
        }

        InitializeLobbySystems();
    }

    private void InitializeLobbySystems()
    {
        Managers.Discord.Init();
        Managers.UI.ShowSceneUI<UI_DiscordLobby>("UI_DiscordLobby");

        Managers.Discord.OnLobbyUserJoined -= HandleLobbyUserJoined;
        Managers.Discord.OnLobbyUserJoined += HandleLobbyUserJoined;

        RangerController localRanger = EnsureLocalRanger();
        if (localRanger == null)
            return;

        _spawnCenter = localRanger.transform;
        localRanger.SetIdentity(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
        _rangersByUser[Managers.Discord.LocalUserId] = localRanger;
    }

    private RangerController EnsureLocalRanger()
    {
        RangerController[] existing = FindObjectsByType<RangerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (existing.Length > 0)
            return existing[0];

        GameObject rangerInScene = GameObject.Find("Ranger");
        if (rangerInScene != null)
            return rangerInScene.GetorAddComponent<RangerController>();

        GameObject rangerPrefab = Resources.Load<GameObject>(RangerResourcePath);
        if (rangerPrefab == null)
        {
            Debug.LogWarning($"LobbyDiscordBootstrap: missing prefab at Resources/{RangerResourcePath}");
            return null;
        }

        GameObject localRanger = Instantiate(rangerPrefab);
        localRanger.name = "Ranger";
        localRanger.transform.position = Vector3.zero;
        return localRanger.GetorAddComponent<RangerController>();
    }

    private void HandleLobbyUserJoined(DiscordLobbyUser user)
    {
        if (_rangersByUser.ContainsKey(user.UserId))
        {
            _rangersByUser[user.UserId].SetIdentity(user.UserId, user.DisplayName, user.IsLocalUser);
            return;
        }

        if (user.IsLocalUser)
            return;

        RangerController ranger = SpawnFriendRanger(user.DisplayName);
        if (ranger == null)
            return;

        ranger.SetIdentity(user.UserId, user.DisplayName, false);
        _rangersByUser[user.UserId] = ranger;
    }

    private RangerController SpawnFriendRanger(string displayName)
    {
        GameObject rangerPrefab = Resources.Load<GameObject>(RangerResourcePath);
        if (rangerPrefab == null)
            return null;

        Vector3 center = _spawnCenter != null ? _spawnCenter.position : Vector3.zero;
        float angle = _spawnSerial * 36f;
        Vector3 spawnOffset = Quaternion.Euler(0f, angle, 0f) * new Vector3(2.25f, 0f, 0f);

        GameObject spawned = Instantiate(rangerPrefab, center + spawnOffset, Quaternion.identity);
        spawned.name = $"Ranger_{displayName}";

        _spawnSerial++;
        return spawned.GetorAddComponent<RangerController>();
    }
}
