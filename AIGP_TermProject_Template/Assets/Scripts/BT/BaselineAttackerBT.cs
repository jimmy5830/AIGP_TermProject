using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
// Simple reference attacker for BT/RL comparison. It only calls CombatActionController APIs.
public class BaselineAttackerBT : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;
    [SerializeField] private float attackDistance = 1.8f;
    [SerializeField] private float facingAngle = 45f;
    [SerializeField] private float lowHealthRatio = 0.35f;

    private BTNode root;

    private void Awake()
    {
        FillDefaultReferences();
        BuildTree();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void Update()
    {
        if (!CanTick())
        {
            return;
        }

        root.Tick();
    }

    private void BuildTree()
    {
        root = new SelectorNode(
            new SequenceNode(
                new ConditionNode(ShouldDodge),
                new ActionNode(DodgeAway)),
            new SequenceNode(
                new ConditionNode(CanAttackNow),
                new ActionNode(Attack)),
            new ActionNode(MoveTowardTarget));
    }

    private bool CanTick()
    {
        return root != null
            && self != null
            && target != null
            && actionController != null
            && !self.IsDead
            && !target.IsDead;
    }

    private bool ShouldDodge()
    {
        return self.CurrentHealthRatio <= lowHealthRatio
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    private bool CanAttackNow()
    {
        return cooldownSystem != null
            && cooldownSystem.IsAttackReady()
            && IsTargetInAttackDistance()
            && IsFacingTarget();
    }

    private BTNodeStatus DodgeAway()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Dodge(-GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private BTNodeStatus Attack()
    {
        actionController.Face(GetDirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    private BTNodeStatus MoveTowardTarget()
    {
        actionController.Move(GetDirectionToTarget());
        return BTNodeStatus.Success;
    }

    private bool IsTargetInAttackDistance()
    {
        return GetHorizontalOffsetToTarget().magnitude <= attackDistance;
    }

    private bool IsFacingTarget()
    {
        Vector3 direction = GetDirectionToTarget();
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= facingAngle;
    }

    private Vector3 GetDirectionToTarget()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

    private Vector3 GetHorizontalOffsetToTarget()
    {
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset;
    }

    private void FillDefaultReferences()
    {
        if (self == null)
        {
            self = GetComponent<CombatCharacter>();
        }

        if (actionController == null)
        {
            actionController = GetComponent<CombatActionController>();
        }

        if (cooldownSystem == null)
        {
            cooldownSystem = GetComponent<CooldownSystem>();
        }
    }
}
