using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
// Simple reference defender for BT/RL comparison. It favors block/dodge over attacking.
public class BaselineDefenderBT : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;
    [SerializeField] private float closeDistance = 2.0f;
    [SerializeField] private float preferredDistance = 3.0f;
    [SerializeField] private float lowHealthRatio = 0.3f;

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

        if (actionController.IsBlocking)
        {
            actionController.UpdateRotationLock(GetDirectionToTarget());
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
                new ConditionNode(CanBlockIncomingAttack),
                new ActionNode(Block)),
            new SequenceNode(
                new ConditionNode(CanDodgeCloseTarget),
                new ActionNode(DodgeAway)),
            new SequenceNode(
                new ConditionNode(CanCounterAttack),
                new ActionNode(Attack)),
            new ActionNode(MaintainDistance));
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

    private bool CanBlockIncomingAttack()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsBlockReady()
            && (IsTargetAttacking() || IsTargetAttackReady());
    }

    private bool CanDodgeCloseTarget()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady();
    }

    private bool CanCounterAttack()
    {
        return IsTargetClose()
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady();
    }

    private BTNodeStatus Block()
    {
        actionController.Block(GetDirectionToTarget());
        return BTNodeStatus.Success;
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

    private BTNodeStatus MaintainDistance()
    {
        Vector3 offset = GetHorizontalOffsetToTarget();
        if (offset.magnitude < preferredDistance)
        {
            actionController.Move(-GetDirectionToTarget());
        }

        return BTNodeStatus.Success;
    }

    private bool IsTargetClose()
    {
        return GetHorizontalOffsetToTarget().magnitude <= closeDistance;
    }

    private bool IsTargetAttacking()
    {
        CombatActionController targetAction = target.ActionController;
        return targetAction != null && targetAction.IsAttacking;
    }

    private bool IsTargetAttackReady()
    {
        CooldownSystem targetCooldown = target.CooldownSystem;
        return targetCooldown != null && targetCooldown.IsAttackReady();
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
