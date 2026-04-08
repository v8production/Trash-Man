using UnityEngine;

public class RangerController : MonoBehaviour
{
    [SerializeField] private bool _isLocalPlayer = true;
    [SerializeField] private Vector3 _nicknameOffset = new(0f, 2.2f, 0f);

    private UI_Nickname _nicknameUI;
    private Transform _nicknameAnchor;
    private string _userId = string.Empty;

    public string UserId => _userId;

    private void Awake()
    {
        _nicknameAnchor = FindNicknameAnchor();
    }

    private void Start()
    {
        EnsureNicknameUI();

        if (_isLocalPlayer)
            SetIdentity(Managers.Discord.LocalUserId, Managers.Discord.LocalDisplayName, true);
    }

    private void LateUpdate()
    {
        if (_nicknameUI == null)
            return;

        _nicknameUI.transform.localPosition = _nicknameOffset;

        Camera camera = Camera.main;
        if (camera != null)
            _nicknameUI.transform.forward = camera.transform.forward;
    }

    public void SetIdentity(string userId, string displayName, bool isLocalPlayer)
    {
        _isLocalPlayer = isLocalPlayer;
        _userId = userId;
        EnsureNicknameUI();
        _nicknameUI?.SetNickname(displayName);
    }

    private void EnsureNicknameUI()
    {
        if (_nicknameUI != null)
            return;

        if (_nicknameAnchor == null)
            _nicknameAnchor = transform;

        _nicknameUI = Managers.UI.CreateWorldSpaceUI<UI_Nickname>(_nicknameAnchor, "UI_Nickname");
        if (_nicknameUI != null)
            _nicknameUI.transform.localPosition = _nicknameOffset;
    }

    private Transform FindNicknameAnchor()
    {
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null)
                return head;
        }

        return transform;
    }
}
