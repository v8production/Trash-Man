using UnityEngine;

public class GameScene : BaseScene
{
    private const string TitanPrefabName = "Titan";
    private const string GrolarPrefabName = "Grolar";
    private static readonly Vector3 GrolarSpawnPosition = new(-0.489070356f, 7.18142701e-09f, 1.08038938f);
    private static readonly Quaternion GrolarSpawnRotation = Quaternion.Euler(0f, 180f, 0f);
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

    private static void EnsureGrolarRuntime()
    {
        GrolarController grolarController = FindAnyObjectByType<GrolarController>();
        if (grolarController != null)
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
    }

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Game;

        Debug.Log($"{InputDebug.Prefix} GameScene.Init SceneType={SceneType}");

        LoadManagers();
        EnsureTitanRuntime();
        EnsureGrolarRuntime();
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
