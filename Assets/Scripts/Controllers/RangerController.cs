using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class RangerController : MonoBehaviour
{
    [Header("Actions (Player Map)")]
    [SerializeField] private string moveActionName = "Move";


    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float rotateLerpSpeed = 12f;

    private CharacterController _characterController;
    private LobbyCameraController _cameraController;
    private InputAction _moveAction;
    private Vector2 _moveInput;
    private bool _initialized;

    Animator Anim;

    private LocomotionState _animState;

    public LocomotionState AnimState
    {
        get { return _animState; }
        set
        {
            if (EqualityComparer<LocomotionState>.Default.Equals(_animState, value))
                return;
            _animState = value;

            if (Anim != null)
                Anim.CrossFade(_animState.ToString(), 0.1f);
        }
    }

    public enum LocomotionState
    {
        idle,
        walk
    }

    private void Awake()
    {
        Init();
    }

    private void Init()
    {
        if (_initialized)
            return;

        _characterController = GetComponent<CharacterController>();
        Anim = GetComponentInChildren<Animator>();

        InputActionMap playerMap = Managers.Input.PlayerMap;
        if (playerMap != null)
            _moveAction = playerMap.FindAction(moveActionName, false);

        AnimState = LocomotionState.idle;
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized)
            Init();

        if (Managers.Input.Mode != Define.InputMode.Player)
        {
            _moveInput = Vector2.zero;
            return;
        }

        UpdateInput();

        Vector3 moveDirection = GetCameraRelativeDirectionOnPlane(_moveInput);
        Vector3 planarVelocity = moveDirection * moveSpeed;
        _characterController.Move(planarVelocity * Time.deltaTime);

        UpdateRotation(moveDirection);

        AnimState = _moveInput.sqrMagnitude > 0.0001f ? LocomotionState.walk : LocomotionState.idle;
    }

    private void UpdateInput()
    {
        _moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        _moveInput = Vector2.ClampMagnitude(_moveInput, 1f);
    }

    private void UpdateRotation(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 currentForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (currentForward.sqrMagnitude <= 0.0001f)
            currentForward = moveDirection;

        Vector3 lerpedForward = Vector3.Lerp(currentForward.normalized, moveDirection.normalized, rotateLerpSpeed * Time.deltaTime);
        if (lerpedForward.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(lerpedForward.normalized, Vector3.up);
    }

    private Vector3 GetCameraRelativeDirectionOnPlane(Vector2 moveInput)
    {
        if (_cameraController == null)
            _cameraController = GetMainCameraController();

        Vector3 forward;
        Vector3 right;

        if (_cameraController != null)
        {
            forward = _cameraController.transform.forward;
            right = _cameraController.transform.right;
        }
        else
        {
            forward = transform.forward;
            right = transform.right;
        }

        Vector3 direction = (right * moveInput.x) + (forward * moveInput.y);
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private static LobbyCameraController GetMainCameraController()
    {
        LobbyCameraController cameraController = Object.FindFirstObjectByType<LobbyCameraController>();
        if (cameraController != null)
            return cameraController;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.GetComponent<LobbyCameraController>() : null;
    }
}
