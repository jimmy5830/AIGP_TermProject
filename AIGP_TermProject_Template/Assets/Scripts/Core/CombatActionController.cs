using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CooldownSystem))]
// Common movement and action API used by manual input now, and BT/RL controllers later.
public class CombatActionController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float dodgeImpulse = 2f;
    [SerializeField] private float dodgeInvincibleDuration = 0.35f;
    [SerializeField] private float blockDuration = 0.75f;

    [SerializeField] private Rigidbody body;
    [SerializeField] private CooldownSystem cooldownSystem;
    [SerializeField] private CombatCharacter character;
    [SerializeField] private CombatHitDetector hitDetector;
    [SerializeField] private CombatAnimatorDriver animatorDriver;

    private Coroutine blockRoutine;
    private Coroutine dodgeRoutine;
    private bool attackHitResolved;
    private bool isRotationLocked;
    private Vector3 lockedFacingDirection;
    private int lastMoveFrame = -1;

    public float MoveSpeed
    {
        get => moveSpeed;
        set => moveSpeed = Mathf.Max(0f, value);
    }

    public float DodgeImpulse
    {
        get => dodgeImpulse;
        set => dodgeImpulse = Mathf.Max(0f, value);
    }

    public float DodgeInvincibleDuration
    {
        get => dodgeInvincibleDuration;
        set => dodgeInvincibleDuration = Mathf.Max(0f, value);
    }

    public float BlockDuration
    {
        get => blockDuration;
        set => blockDuration = Mathf.Max(0f, value);
    }

    public bool IsBlocking { get; private set; }
    public bool IsInvincible { get; private set; }
    public bool IsAttacking { get; private set; }
    public bool IsBusy => IsAttacking || IsBlocking || IsInvincible;

    private void Awake()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void LateUpdate()
    {
        animatorDriver?.SetMoving(lastMoveFrame == Time.frameCount && !IsBusy);
        ApplyRotationLock();
    }

    public void Move(Vector3 direction)
    {
        if (!TryGetMoveDirection(direction, out Vector3 horizontalDirection))
        {
            return;
        }

        body.MovePosition(body.position + horizontalDirection * moveSpeed * Time.deltaTime);
        Face(horizontalDirection);
        lastMoveFrame = Time.frameCount;
    }

    public void Face(Vector3 direction)
    {
        if (!CanAct() || IsBusy)
        {
            return;
        }

        Vector3 horizontalDirection = Flatten(direction);
        if (horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        horizontalDirection.Normalize();
        transform.rotation = Quaternion.LookRotation(horizontalDirection, Vector3.up);
    }

    public void Attack()
    {
        if (!CanAct() || IsBusy || !cooldownSystem.IsAttackReady())
        {
            return;
        }

        cooldownSystem.TriggerAttackCooldown();
        IsAttacking = true;
        attackHitResolved = false;
        LockRotation(transform.forward);
        if (!ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} started attack.");
        }

        animatorDriver?.PlayAttack();
    }

    public void Block()
    {
        Block(transform.forward);
    }

    public void Block(Vector3 facingDirection)
    {
        if (!CanAct() || IsBusy || !cooldownSystem.IsBlockReady())
        {
            return;
        }

        LockRotation(facingDirection);
        cooldownSystem.TriggerBlockCooldown();
        blockRoutine = StartCoroutine(BlockRoutine());
    }

    public void UpdateRotationLock(Vector3 facingDirection)
    {
        if (!isRotationLocked)
        {
            return;
        }

        LockRotation(facingDirection);
    }

    public void Dodge(Vector3 direction)
    {
        if (!CanAct() || IsBusy || !cooldownSystem.IsDodgeReady())
        {
            return;
        }

        Vector3 dodgeDirection = Flatten(direction);
        if (dodgeDirection.sqrMagnitude <= 0.0001f)
        {
            dodgeDirection = Flatten(transform.forward);
        }

        dodgeDirection.Normalize();
        cooldownSystem.TriggerDodgeCooldown();
        dodgeRoutine = StartCoroutine(DodgeRoutine(dodgeDirection));
    }

    public void ResetActionState()
    {
        StopRunningCoroutine(ref blockRoutine);
        StopRunningCoroutine(ref dodgeRoutine);

        EndAttack(logEnd: false);
        IsBlocking = false;
        IsInvincible = false;
        UnlockRotation();
        lastMoveFrame = -1;
        animatorDriver?.ResetAnimationState();
    }

    public void OnAttackHitFrame()
    {
        if (!IsAttacking || attackHitResolved)
        {
            return;
        }

        attackHitResolved = true;
        hitDetector?.TryHit();
    }

    public void OnAttackEnd()
    {
        EndAttack(logEnd: true);
    }

    public void PlayHitReaction()
    {
        EndAttack(logEnd: true);
        animatorDriver?.PlayHit();
    }

    private void EndAttack(bool logEnd)
    {
        if (!IsAttacking)
        {
            attackHitResolved = false;
            return;
        }

        IsAttacking = false;
        attackHitResolved = false;
        UnlockRotation();
        if (logEnd && !ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} ended attack.");
        }
    }

    private IEnumerator BlockRoutine()
    {
        IsBlocking = true;
        if (!ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} started block.");
        }

        animatorDriver?.SetBlocking(true);

        yield return new WaitForSeconds(blockDuration);

        IsBlocking = false;
        if (!ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} ended block.");
        }

        animatorDriver?.SetBlocking(false);
        UnlockRotation();
        blockRoutine = null;
    }

    private IEnumerator DodgeRoutine(Vector3 direction)
    {
        IsInvincible = true;
        if (!ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} started dodge.");
        }

        animatorDriver?.PlayDodge();
        body.AddForce(direction * dodgeImpulse, ForceMode.VelocityChange);

        yield return new WaitForSeconds(dodgeInvincibleDuration);

        IsInvincible = false;
        if (!ShouldSuppressCombatDebug())
        {
            Debug.Log($"{name} ended dodge.");
        }

        dodgeRoutine = null;
    }

    private bool ShouldSuppressCombatDebug()
    {
        StudentCombatAgent[] agents = FindObjectsByType<StudentCombatAgent>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (StudentCombatAgent agent in agents)
        {
            if (agent.enabled)
            {
                return true;
            }
        }

        return false;
    }

    private bool CanAct()
    {
        return character == null || !character.IsDead;
    }

    private bool TryGetMoveDirection(Vector3 direction, out Vector3 horizontalDirection)
    {
        horizontalDirection = Flatten(direction);
        if (!CanAct() || IsBusy || horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        horizontalDirection.Normalize();
        return true;
    }

    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction;
    }

    private void LockRotation(Vector3 direction)
    {
        Vector3 horizontalDirection = Flatten(direction);
        if (horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            horizontalDirection = Flatten(transform.forward);
        }

        if (horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        lockedFacingDirection = horizontalDirection.normalized;
        isRotationLocked = true;
        ApplyRotationLock();
    }

    private void UnlockRotation()
    {
        isRotationLocked = false;
        lockedFacingDirection = Vector3.zero;
    }

    private void ApplyRotationLock()
    {
        if (!isRotationLocked || lockedFacingDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(lockedFacingDirection, Vector3.up);
    }

    private void FillDefaultReferences()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (cooldownSystem == null)
        {
            cooldownSystem = GetComponent<CooldownSystem>();
        }

        if (character == null)
        {
            character = GetComponent<CombatCharacter>();
        }

        if (hitDetector == null)
        {
            hitDetector = GetComponent<CombatHitDetector>();
        }

        if (animatorDriver == null)
        {
            animatorDriver = GetComponent<CombatAnimatorDriver>();
        }
    }

    private void StopRunningCoroutine(ref Coroutine routine)
    {
        if (routine == null)
        {
            return;
        }

        StopCoroutine(routine);
        routine = null;
    }
}
