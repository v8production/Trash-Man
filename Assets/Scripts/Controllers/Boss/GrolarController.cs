using System.Collections;
using UnityEngine;

public class GrolarController : BossController
{
    // TEMP: Debug-only helper to cycle through animation states every few seconds.
    // Remove this whole region once animation verification is done.
    [Header("TEMP - Animation Cycle (remove anytime)")]
    [SerializeField] private bool enableTempAnimCycle = true;
    [SerializeField, Min(0.1f)] private float tempAnimIntervalSeconds = 3f;
    [SerializeField] private bool tempUseCrossFade = true;
    [SerializeField, Min(0f)] private float tempCrossFadeDuration = 0.05f;

    private static readonly string[] TempAnimNames =
    {
        "Idle_00",
        "Run_00",
        "Walk_00",
        "Alert_Roar_00",
        "Alert_00",
        "Alert_Idle_00",
        "Hit_00",
        "Attack_Hit_00",
        "Attack_Rebound_00",
        "Attack_Swing_00",
    };

    private Animator _animator;
    private Coroutine _tempAnimRoutine;
    private int _tempAnimIndex;
    private BossStat _stat;

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>(true);
        _stat = gameObject.GetComponent<BossStat>();
    }

    private void OnEnable()
    {
        Debug.Log($"[GrolarController][TEMP] OnEnable (enableTempAnimCycle={enableTempAnimCycle})");

        if (enableTempAnimCycle)
            Temp_StartAnimationCycle();
    }

    private void OnDisable()
    {
        Temp_StopAnimationCycle();
    }

    [ContextMenu("TEMP/Start Animation Cycle")]
    private void Temp_StartAnimationCycle()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GrolarController][TEMP] Start Animation Cycle is PlayMode-only.");
            return;
        }

        if (_tempAnimRoutine != null)
            return;

        if (_animator == null)
            _animator = GetComponentInChildren<Animator>(true);

        if (_animator == null)
        {
            Debug.LogWarning("[GrolarController][TEMP] Animator not found. Attach an Animator to this object or its children.");
            return;
        }

        _tempAnimRoutine = StartCoroutine(Temp_AnimationCycleRoutine());
    }

    [ContextMenu("TEMP/Stop Animation Cycle")]
    private void Temp_StopAnimationCycle()
    {
        if (!Application.isPlaying)
            return;

        if (_tempAnimRoutine == null)
            return;

        StopCoroutine(_tempAnimRoutine);
        _tempAnimRoutine = null;
    }

    private IEnumerator Temp_AnimationCycleRoutine()
    {
        _tempAnimIndex = 0;

        while (true)
        {
            string animName = TempAnimNames[_tempAnimIndex];
            Temp_PlayAnimation(animName);

            _tempAnimIndex = (_tempAnimIndex + 1) % TempAnimNames.Length;
            yield return new WaitForSeconds(tempAnimIntervalSeconds);
        }
    }

    private void Temp_PlayAnimation(string stateName)
    {
        if (tempUseCrossFade)
            _animator.CrossFade(stateName, tempCrossFadeDuration);
        else
            _animator.Play(stateName, 0, 0f);

        Debug.Log($"[GrolarController][TEMP] Play '{stateName}'");
    }
}
