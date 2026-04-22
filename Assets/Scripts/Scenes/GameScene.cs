using UnityEngine;

public class GameScene : BaseScene
{
    private const string TitanPrefabName = "Titan";
    private TitanController _titanController;
    private TitanRoleNetworkDriver _titanRoleDriver;

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

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Game;

        Debug.Log($"{InputDebug.Prefix} GameScene.Init SceneType={SceneType}");

        LoadManagers();
        EnsureTitanRuntime();
        CleanupLobbyRangers();
        Managers.Input.SetMode(Define.InputMode.Player);
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
    }
}
