using UnityEngine;

public class TitanController : MonoBehaviour
{
    [Header("Auto Attach")]
    [SerializeField] private bool attachControllersOnAwake = true;

    [Header("References")]
    [SerializeField] private TitanRigRuntime rigRuntime;
    [SerializeField] private TitanBodyRoleController bodyController;
    [SerializeField] private TitanLeftArmRoleController leftArmController;
    [SerializeField] private TitanRightArmRoleController rightArmController;
    [SerializeField] private TitanLeftLegRoleController leftLegController;
    [SerializeField] private TitanRightLegRoleController rightLegController;

    private void Awake()
    {
        if (attachControllersOnAwake)
        {
            EnsureInitialized();
        }
    }

    public void EnsureInitialized()
    {
        rigRuntime = RequireComponent(rigRuntime);
        if (rigRuntime != null)
            Managers.TitanRig.Bind(rigRuntime);

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
