using UnityEngine;
using UnityEngine.InputSystem;

public class GameScene : BaseScene
{
    private const string TitanPrefabName = "Titan";
    private const string GrolarPrefabName = "Grolar";
    private static readonly Vector3 GrolarSpawnPosition = new(0, 0, 1);
    private static readonly Quaternion GrolarSpawnRotation = Quaternion.Euler(0f, 180f, 0f);
    private TitanController _titanController;
    private TitanRoleNetworkDriver _titanRoleDriver;
    private GrolarController _grolarController;

    private UI_Boss _bossUi;
    private UI_TitanStat _titanStatUi;
    private UI_GameMenu _gameMenuUi;

    private static void LoadManagers()
    {
        _ = Managers.Input;
        _ = Managers.TitanRole;
    }

    private void EnsureTitanRuntime()
    {
        _titanController = FindAnyObjectByType<TitanController>();

        if (_titanController == null)
        {
            // Prefer using an authored Titan already placed in the scene.
            GameObject titanObject = GameObject.Find(TitanPrefabName);
            if (titanObject == null)
                titanObject = Managers.Resource.Instantiate(TitanPrefabName);
            if (titanObject == null)
                return;

            // If we instantiated a new titan at the origin, spawn it slightly above the floor.
            if (titanObject.scene.IsValid() && titanObject.transform.position.y <= 0.01f)
                titanObject.transform.position = new Vector3(titanObject.transform.position.x, 1.5f, titanObject.transform.position.z);

            _titanController = titanObject.GetComponent<TitanController>();
            if (_titanController == null)
            {
                Debug.LogError($"{InputDebug.Prefix} Titan prefab is missing TitanController. Add it to Resources/Prefabs/Titan.prefab");
                return;
            }
        }

        _titanController.EnsureInitialized();

        _titanRoleDriver = _titanController.GetComponent<TitanRoleNetworkDriver>();
        if (_titanRoleDriver == null)
        {
            Debug.LogError($"{InputDebug.Prefix} Titan prefab is missing TitanRoleNetworkDriver. Add it to Resources/Prefabs/Titan.prefab");
            return;
        }
    }

    private void EnsureGrolarRuntime()
    {
        _grolarController = FindAnyObjectByType<GrolarController>();
        if (_grolarController != null)
            return;

        GameObject grolarObject = GameObject.Find(GrolarPrefabName);
        if (grolarObject == null)
            grolarObject = Managers.Resource.Instantiate(GrolarPrefabName);

        if (grolarObject == null)
        {
            Debug.LogError($"{InputDebug.Prefix} Failed to load Grolar prefab from Resources/Prefabs/Grolar.prefab");
            return;
        }

        grolarObject.transform.SetPositionAndRotation(GrolarSpawnPosition, GrolarSpawnRotation);

        _grolarController = grolarObject.GetComponent<GrolarController>();
    }

    private void EnsureBossUI()
    {
        if (_bossUi != null)
            return;

        _bossUi = Managers.UI.ShowSceneUI<UI_Boss>(nameof(UI_Boss));
    }

    private void EnsureTitanStatUI()
    {
        if (_titanStatUi != null)
            return;

        _titanStatUi = Managers.UI.ShowSceneUI<UI_TitanStat>(nameof(UI_TitanStat));
    }

    private void EnsureGameMenuUI()
    {
        if (_gameMenuUi != null)
            return;

        _gameMenuUi = Managers.UI.ShowSceneUI<UI_GameMenu>(nameof(UI_GameMenu));
        if (_gameMenuUi != null)
            _gameMenuUi.gameObject.SetActive(false);
    }

    private void MapStatsToUIs()
    {
        EnsureBossUI();
        EnsureTitanStatUI();

        if (_bossUi != null && _grolarController != null)
            _bossUi.SetStat(_grolarController.GetComponent<BossStat>());

        if (_titanStatUi != null && _titanController != null)
            _titanStatUi.SetStat(_titanController.GetComponent<TitanStat>());
    }

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Game;

        Debug.Log($"{InputDebug.Prefix} GameScene.Init SceneType={SceneType}");

        LoadManagers();
        EnsureTitanRuntime();
        EnsureGrolarRuntime();
        EnsureBossUI();
        EnsureTitanStatUI();
        EnsureGameMenuUI();
        MapStatsToUIs();
        CleanupLobbyRangers();
        Managers.Input.SetMode(Define.InputMode.Player);
    }

    private void Update()
    {
        if (!IsEscapePressedThisFrame())
            return;

        ToggleGameMenu();
    }

    private void ToggleGameMenu()
    {
        EnsureGameMenuUI();
        if (_gameMenuUi == null)
            return;

        bool shouldShow = !_gameMenuUi.gameObject.activeSelf;
        _gameMenuUi.gameObject.SetActive(shouldShow);
        Managers.Input.SetMode(shouldShow ? Define.InputMode.UI : Define.InputMode.Player);
    }

    private static bool IsEscapePressedThisFrame()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private static void CleanupLobbyRangers()
    {
        Transform runtimeRoot = GameObject.Find("@NetworkManager")?.transform;

        LobbyNetworkPlayer[] players = LobbyNetworkPlayer.FindAllSpawnedPlayers();
        for (int i = 0; i < players.Length; i++)
        {
            LobbyNetworkPlayer player = players[i];
            if (player == null)
                continue;

            player.PrepareForGameScene(runtimeRoot);
        }
    }

    public override void Clear()
    {
        _titanController = null;
        _titanRoleDriver = null;
        _grolarController = null;
        _bossUi = null;
        _titanStatUi = null;
        _gameMenuUi = null;
    }
}
