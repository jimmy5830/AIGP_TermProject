using UnityEngine;

// Thin animation bridge used by BT/RL through CombatActionController.
public class CombatAnimatorDriver : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int IsBlockingHash = Animator.StringToHash("IsBlocking");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DodgeHash = Animator.StringToHash("Dodge");
    private static readonly int HitHash = Animator.StringToHash("Hit");

    [SerializeField] private Animator animator;

    private void Awake()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public void SetMoving(bool isMoving)
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(IsMovingHash, isMoving);
    }

    public void PlayAttack()
    {
        SetTrigger(AttackHash);
    }

    public void SetBlocking(bool isBlocking)
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(IsBlockingHash, isBlocking);
    }

    public void PlayDodge()
    {
        SetTrigger(DodgeHash);
    }

    public void PlayHit()
    {
        SetTrigger(HitHash);
    }

    public void ResetAnimationState()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetBool(IsMovingHash, false);
        animator.SetBool(IsBlockingHash, false);
        animator.ResetTrigger(AttackHash);
        animator.ResetTrigger(DodgeHash);
        animator.ResetTrigger(HitHash);
    }

    private void SetTrigger(int triggerHash)
    {
        if (animator == null)
        {
            return;
        }

        animator.SetTrigger(triggerHash);
    }

    private void FillDefaultReferences()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }
}
