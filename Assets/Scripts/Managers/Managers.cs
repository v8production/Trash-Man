using UnityEngine;

public class Managers : MonoBehaviour
{
    static Managers _instance;
    static Managers Instance { get { Init(); return _instance; } }

    #region Contents
    GameStateManager _gameStateManager = new();
    ChatMessageManager _chatMessageManager = new();
    TitanRigManager _titanRigManager = TitanRigManager.Instance;
    public static GameStateManager GameState { get { return Instance._gameStateManager; } }
    public static ChatMessageManager Chat { get { return Instance._chatMessageManager; } }
    public static TitanRigManager TitanRig { get { return Instance._titanRigManager; } }
    #endregion

    #region  Core
    DataManager _dataManager = new();
    InputManager _inputManager = new();
    PoolManager _poolManager = new();
    ResourceManager _resourceManager = new();
    SceneManagerEx _sceneManager = new();
    SoundManager _soundManager = new();
    UIManager _uiManager = new();
    DiscordManager _discordManager = new();
    public static DataManager Data { get { return Instance._dataManager; } }
    public static InputManager Input { get { return Instance._inputManager; } }
    public static PoolManager Pool { get { return Instance._poolManager; } }
    public static ResourceManager Resource { get { return Instance._resourceManager; } }
    public static SceneManagerEx Scene { get { return Instance._sceneManager; } }
    public static SoundManager Sound { get { return Instance._soundManager; } }
    public static UIManager UI { get { return Instance._uiManager; } }
    public static DiscordManager Discord { get { return Instance._discordManager; } }
    #endregion

    void Start()
    {
        Init();
    }

    void Update()
    {
        _chatMessageManager.OnUpdate();
    }

    static void Init()
    {
        if (_instance == null)
        {
            GameObject go = GameObject.Find("@Manager");
            if (go == null)
            {
                go = new GameObject { name = "@Manager" };
                go.AddComponent<Managers>();
            }
            DontDestroyOnLoad(go);
            _instance = go.GetComponent<Managers>();

            _instance._inputManager.Init();
            _instance._dataManager.Init();
            _instance._gameStateManager.Init();
            _instance._chatMessageManager.Init();
            _instance._poolManager.Init();
            _instance._soundManager.Init();
            _instance._titanRigManager.Init();
            _instance._discordManager.Init();
        }
    }

    public static void Clear()
    {
        Sound.Clear();
        Scene.Clear();
        UI.Clear();

        GameState.Clear();
        Chat.Clear();
        Pool.Clear();
        TitanRig.Clear();
        Discord.Clear();
    }
}
