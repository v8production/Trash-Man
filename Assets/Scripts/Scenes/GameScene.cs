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
                _titanController = titanObject.AddComponent<TitanController>();
        }

        // Ensure physics + controllers exist even if the bootstrap ran before Titan was instantiated.
        TitanCoopBootstrap.EnsureRuntime(_titanController.gameObject);

        _titanRoleDriver = _titanController.GetComponent<TitanRoleNetworkDriver>();
        if (_titanRoleDriver == null)
            _titanRoleDriver = _titanController.gameObject.AddComponent<TitanRoleNetworkDriver>();
    }

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Game;

        Debug.Log($"{InputDebug.Prefix} GameScene.Init SceneType={SceneType}");

        LoadManagers();

        EnsureFloorCollision();
        EnsureTitanRuntime();
        CleanupLobbyRangers();
        Managers.Input.SetMode(Define.InputMode.Player);
    }

    private void EnsureFloorCollision()
    {
        // A Plane MeshCollider is effectively zero-thickness and can be tunneled through.
        // Add a BoxCollider with a tiny thickness so dynamic bodies reliably collide.
        GameObject plane = GameObject.Find("Plane");
        if (plane == null)
            return;

        if (plane.GetComponent<Collider>() is BoxCollider)
            return;

        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        Bounds bounds = renderer != null ? renderer.bounds : new Bounds(plane.transform.position, new Vector3(10f, 0.1f, 10f));

        BoxCollider box = plane.AddComponent<BoxCollider>();
        Vector3 localCenter = plane.transform.InverseTransformPoint(bounds.center);
        Vector3 localSize = bounds.size;
        // Ensure some thickness.
        localSize.y = Mathf.Max(localSize.y, 0.2f);

        box.center = localCenter;
        box.size = localSize;
        box.isTrigger = false;

        InputDebug.Log($"Floor BoxCollider added: name={plane.name} center={box.center} size={box.size} layer={plane.layer}");
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
