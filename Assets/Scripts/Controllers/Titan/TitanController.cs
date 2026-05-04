using UnityEngine;

public class TitanController : MonoBehaviour
{
    [Header("Auto Attach")]
    [SerializeField] private bool attachControllersOnAwake = true;

    [Header("References")]
    [SerializeField] private TitanRigRuntime rigRuntime;
    [SerializeField] private TitanLegAnchorResolver legAnchorResolver;
    [SerializeField] private TitanBodyRoleController bodyController;
    [SerializeField] private TitanLeftArmRoleController leftArmController;
    [SerializeField] private TitanRightArmRoleController rightArmController;
    [SerializeField] private TitanLeftLegRoleController leftLegController;
    [SerializeField] private TitanRightLegRoleController rightLegController;

    TitanStat _stat;
    public TitanStat Stat { get { return _stat; } }

    bool _guard = false;

    public bool Guard { get { return _guard; } set { _guard = value; } }

    private void Awake()
    {
        if (attachControllersOnAwake)
        {
            EnsureInitialized();
        }
        _stat = gameObject.GetComponent<TitanStat>();
    }

    public void EnsureInitialized()
    {
        rigRuntime = RequireComponent(rigRuntime);
        if (rigRuntime != null)
            Managers.TitanRig.Bind(rigRuntime);

        legAnchorResolver = RequireComponent(legAnchorResolver);
        bodyController = RequireComponent(bodyController);
        leftArmController = RequireComponent(leftArmController);
        rightArmController = RequireComponent(rightArmController);
        leftLegController = RequireComponent(leftLegController);
        rightLegController = RequireComponent(rightLegController);
    }

    private T RequireComponent<T>(T existing) where T : Component
    {
        if (existing != null)
        {
            return existing;
        }

        T found = GetComponent<T>();
        if (found != null)
        {
            return found;
        }

        Debug.LogError($"[TitanController] Missing required component '{typeof(T).Name}' on '{gameObject.name}'. Add it to the Titan prefab.", this);
        return null;
    }
}
