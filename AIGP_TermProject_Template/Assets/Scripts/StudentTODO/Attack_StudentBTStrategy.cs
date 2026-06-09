using UnityEngine;

public class Attack_StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    // 공격 가능 거리, 근접 판단 거리, 공격 각도, 저체력 기준값입니다.
    [SerializeField] private float attackDistance = 1.4f;
    [SerializeField] private float closeDistance = 1.8f;
    [SerializeField] private float facingAngle = 60f;
    [SerializeField] private float lowHealthRatio = 0.25f;

    // 좌우 이동 방향을 일정 시간 유지하여 매 프레임 방향이 바뀌는 흔들림을 줄입니다.
    [SerializeField] private float strafeChangeInterval = 0.8f;

    private BTNode root;

    private float strafeDirection = 1f;
    private float nextStrafeChangeTime = 0f;

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
            // 1. 상대가 Blocking 중이면 공격하지 않고 좌우 이동으로 기다립니다.
            // 수비형 Agent의 Block에 공격이 막히는 문제를 줄이기 위한 분기입니다.
            new SequenceNode(
                new ConditionNode(IsTargetClose),
                new ConditionNode(IsTargetBlocking),
                new ActionNode(StrafeAroundTarget)
            ),

            // 2. 저체력이어도 공격 조건이 확실하면 먼저 공격합니다.
            // 마지막 20 HP 차이로 지는 상황을 줄이기 위해 Execute Attack을 Low HP Survival보다 앞에 둡니다.
            new SequenceNode(
                new ConditionNode(IsTargetInAttackRange),
                new ConditionNode(IsAttackReady),
                new ConditionNode(IsFacingTargetForAttack),
                new ConditionNode(IsTargetNotBlocking),
                new ActionNode(Attack)
            ),

            // 3. 저체력이고 공격이 준비되지 않았을 때만 생존 행동을 수행합니다.
            // DecoratorNode와 RandomSelectorNode를 사용하여 BT 요구사항을 만족합니다.
            new DecoratorNode(
                new SequenceNode(
                    new ConditionNode(IsLowHealth),
                    new ConditionNode(IsAttackNotReady),
                    new RandomSelectorNode(
                        new SequenceNode(
                            new ConditionNode(IsDodgeReady),
                            new ActionNode(DodgeAway)
                        ),
                        new ActionNode(StrafeAroundTarget)
                    )
                ),
                status => status
            ),

            // 4. 공격 쿨타임 중이고 상대가 가까우면 좌우 이동으로 위치를 조정합니다.
            new SequenceNode(
                new ConditionNode(IsTargetClose),
                new ConditionNode(IsAttackNotReady),
                new ActionNode(StrafeAroundTarget)
            ),

            // 5. 그 외 상황에서는 기본적으로 적에게 접근합니다.
            new ActionNode(MoveTowardTarget)
        );
    }

    // BT 실행 가능 여부를 확인합니다.
    private bool CanTick()
    {
        return root != null
            && self != null
            && target != null
            && actionController != null
            && cooldownSystem != null
            && !self.IsDead
            && !target.IsDead;
    }

    // 현재 체력이 저체력 기준 이하인지 확인합니다.
    private bool IsLowHealth()
    {
        return self.CurrentHealthRatio <= lowHealthRatio;
    }

    // Dodge 쿨타임이 준비되었는지 확인합니다.
    private bool IsDodgeReady()
    {
        return cooldownSystem.IsDodgeReady();
    }

    // 공격 쿨타임이 준비되었는지 확인합니다.
    private bool IsAttackReady()
    {
        return cooldownSystem.IsAttackReady();
    }

    // 공격 쿨타임이 아직 준비되지 않았는지 확인합니다.
    private bool IsAttackNotReady()
    {
        return !cooldownSystem.IsAttackReady();
    }

    // 상대가 공격 가능 거리 안에 있는지 확인합니다.
    private bool IsTargetInAttackRange()
    {
        return DistanceToTarget() <= attackDistance;
    }

    // 상대가 가까운 거리 안에 있는지 확인합니다.
    private bool IsTargetClose()
    {
        return DistanceToTarget() <= closeDistance;
    }

    // 상대를 공격 가능한 각도로 바라보고 있는지 확인합니다.
    private bool IsFacingTargetForAttack()
    {
        return IsFacingTarget(facingAngle);
    }

    // 상대가 현재 Blocking 중인지 확인합니다.
    private bool IsTargetBlocking()
    {
        if (target == null || target.ActionController == null)
        {
            return false;
        }

        return target.ActionController.IsBlocking;
    }

    // 상대가 Blocking 중이 아닌지 확인합니다.
    private bool IsTargetNotBlocking()
    {
        return !IsTargetBlocking();
    }

    // 공격 행동입니다. 공격 전에 상대 방향을 바라보도록 합니다.
    private BTNodeStatus Attack()
    {
        actionController.Face(DirectionToTarget());
        actionController.Attack();
        return BTNodeStatus.Success;
    }

    // 상대 반대 방향으로 Dodge를 수행합니다.
    private BTNodeStatus DodgeAway()
    {
        actionController.Face(DirectionToTarget());
        actionController.Dodge(-DirectionToTarget());
        return BTNodeStatus.Success;
    }

    // 상대 방향으로 접근합니다.
    private BTNodeStatus MoveTowardTarget()
    {
        actionController.Face(DirectionToTarget());
        actionController.Move(DirectionToTarget());
        return BTNodeStatus.Success;
    }

    // 상대 주변에서 좌우 이동을 수행합니다.
    // 일정 시간 동안 같은 방향을 유지하여 움직임이 너무 흔들리지 않도록 합니다.
    private BTNodeStatus StrafeAroundTarget()
    {
        actionController.Face(DirectionToTarget());

        if (Time.time >= nextStrafeChangeTime)
        {
            strafeDirection = Random.value < 0.5f ? -1f : 1f;
            nextStrafeChangeTime = Time.time + strafeChangeInterval;
        }

        Vector3 directionToTarget = DirectionToTarget();

        // 상대를 기준으로 좌우 방향을 계산합니다.
        Vector3 strafeRight = Vector3.Cross(directionToTarget, Vector3.up).normalized;

        // 현재 선택된 방향으로 이동합니다.
        Vector3 strafeMove = strafeRight * strafeDirection;

        actionController.Move(strafeMove);

        return BTNodeStatus.Success;
    }

    // 자기 위치에서 상대를 향하는 수평 방향 벡터를 계산합니다.
    private Vector3 DirectionToTarget()
    {
        if (target == null)
        {
            return transform.forward;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= 0.0001f)
        {
            return transform.forward;
        }

        return offset.normalized;
    }

    // 자기와 상대 사이의 수평 거리를 계산합니다.
    private float DistanceToTarget()
    {
        if (target == null)
        {
            return float.MaxValue;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;

        return offset.magnitude;
    }

    // 현재 Agent가 상대 방향을 일정 각도 안에서 바라보고 있는지 확인합니다.
    private bool IsFacingTarget(float maxAngle)
    {
        Vector3 direction = DirectionToTarget();

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return Vector3.Angle(forward.normalized, direction) <= maxAngle;
    }

    // 필요한 컴포넌트 참조를 자동으로 채웁니다.
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