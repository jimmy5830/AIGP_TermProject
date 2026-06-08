using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
public class DefenderRLAgentCBS : Agent
{
    public CombatCharacter self;
    public CombatCharacter opponent;
    public CombatActionController actionController;
    public CooldownSystem cooldownSystem;
    public EpisodeManager episodeManager;

    // Branch 0: movement action
    // 0 = none, 1 = toward, 2 = back, 3 = left, 4 = right
    private const int MoveNone = 0;
    private const int MoveToward = 1;
    private const int MoveBack = 2;
    private const int MoveLeft = 3;
    private const int MoveRight = 4;

    // Branch 1: skill action
    // 0 = none, 1 = attack, 2 = block, 3 = dodge
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    // Observation count.
    // 기존 12개 + counter opportunity 여부 1개.
    private const int ObservationSize = 13;

    // Real combat ranges.
    // 공격형 사거리: 1.5
    // 방어형 사거리: 2.0
    private const float AttackerAttackRange = 1.5f;
    private const float DefenderAttackRange = 2.0f;

    // Defensive RL distance settings.
    // Enemy Zone: 공격형 사거리 안.
    // Kill Zone: 공격형 사거리 밖이면서 방어형 사거리 안.
    private const float EnemyZoneDistance = AttackerAttackRange;
    private const float ThreatDistance = AttackerAttackRange + 0.15f;

    // 살짝 여유를 둔 Kill Zone.
    // 너무 경계값에 붙으면 판정/이동 때문에 학습이 불안정해질 수 있음.
    private const float KillZoneMin = AttackerAttackRange + 0.08f; // 1.58
    private const float KillZoneMax = DefenderAttackRange - 0.05f; // 1.95

    private const float TooFarDistance = DefenderAttackRange + 0.35f; // 2.35
    private const float MaxObservationDistance = 5.0f;

    // Reward and episode settings.
    private const float FacingRewardThreshold = 0.7f;
    private const float MaxTrainingEpisodeTime = 60f;

    // Counter settings.
    private const float CounterRewardWindow = 2.0f;
    private const float CounterAttackRequestWindow = 1.5f;

    // Reward tuning.
    private const float SurvivalReward = 0.0002f;
    private const float KillZoneReward = 0.006f;
    private const float TooFarPenalty = -0.012f;

    private const float BlockResponseReward = 0.15f;
    private const float DodgeResponseReward = 0.10f;

    private const float AttackAttemptReward = 0.035f;
    private const float KillZoneAttackAttemptReward = 0.05f;
    private const float CounterAttackChoiceReward = 0.25f;
    private const float CounterHitBonus = 0.75f;

    private const float PassiveInKillZonePenalty = -0.008f;
    private const float BackFromKillZonePenalty = -0.01f;
    private const float CounterSkipPenalty = -0.035f;

    // Previous health values used for reward calculation.
    private float previousSelfHealth;
    private float previousOpponentHealth;

    private float episodeStartTime;

    private bool counterOpportunityActive;
    private float counterOpportunityEndTime = -999f;
    private float lastDefensiveActionTime = -999f;
    private float lastCounterAttackRequestTime = -999f;

    public override void Initialize()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    private void Update()
    {
        // Block 중에도 상대 방향을 계속 바라보게 하여 방어 판정과 후속 카운터 방향을 안정화한다.
        if (actionController != null && actionController.IsBlocking)
        {
            actionController.UpdateRotationLock(DirectionToOpponent());
        }
    }

    public override void OnEpisodeBegin()
    {
        FillDefaultReferences();

        if (episodeManager != null)
        {
            episodeManager.ResetEpisode();
        }

        episodeStartTime = Time.time;
        ResetCounterState();

        if (self != null)
        {
            previousSelfHealth = self.CurrentHealth;
        }

        if (opponent != null)
        {
            previousOpponentHealth = opponent.CurrentHealth;
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        FillDefaultReferences();

        if (self == null || opponent == null)
        {
            for (int i = 0; i < ObservationSize; i++)
            {
                sensor.AddObservation(0f);
            }

            return;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;

        float distance = offset.magnitude;
        Vector3 directionToOpponent = distance <= 0.0001f
            ? transform.forward
            : offset.normalized;

        Vector3 localDirection = transform.InverseTransformDirection(directionToOpponent);

        Vector3 selfForward = transform.forward;
        selfForward.y = 0f;

        if (selfForward.sqrMagnitude <= 0.0001f)
        {
            selfForward = Vector3.forward;
        }

        selfForward.Normalize();

        Vector3 opponentForward = opponent.transform.forward;
        opponentForward.y = 0f;

        if (opponentForward.sqrMagnitude <= 0.0001f)
        {
            opponentForward = Vector3.forward;
        }

        opponentForward.Normalize();

        float selfFacingOpponent = Vector3.Dot(selfForward, directionToOpponent);
        float opponentFacingSelf = Vector3.Dot(opponentForward, -directionToOpponent);

        CooldownSystem opponentCooldown = opponent.CooldownSystem;
        CombatActionController opponentAction = opponent.ActionController;

        // 1. My HP ratio
        sensor.AddObservation(self.CurrentHealthRatio);

        // 2. Opponent HP ratio
        sensor.AddObservation(opponent.CurrentHealthRatio);

        // 3. Normalized distance
        sensor.AddObservation(Mathf.Clamp01(distance / MaxObservationDistance));

        // 4. Opponent local direction x
        sensor.AddObservation(Mathf.Clamp(localDirection.x, -1f, 1f));

        // 5. Opponent local direction z
        sensor.AddObservation(Mathf.Clamp(localDirection.z, -1f, 1f));

        // 6. How much I am facing the opponent
        sensor.AddObservation(Mathf.Clamp(selfFacingOpponent, -1f, 1f));

        // 7. How much the opponent is facing me
        sensor.AddObservation(Mathf.Clamp(opponentFacingSelf, -1f, 1f));

        // 8. My attack cooldown ratio
        sensor.AddObservation(cooldownSystem != null ? cooldownSystem.GetAttackCooldownRatio() : 1f);

        // 9. My block cooldown ratio
        sensor.AddObservation(cooldownSystem != null ? cooldownSystem.GetBlockCooldownRatio() : 1f);

        // 10. My dodge cooldown ratio
        sensor.AddObservation(cooldownSystem != null ? cooldownSystem.GetDodgeCooldownRatio() : 1f);

        // 11. Opponent attack cooldown ratio
        sensor.AddObservation(opponentCooldown != null ? opponentCooldown.GetAttackCooldownRatio() : 1f);

        // 12. Whether opponent is attacking
        sensor.AddObservation(opponentAction != null && opponentAction.IsAttacking ? 1f : 0f);

        // 13. Whether counter opportunity is active
        sensor.AddObservation(IsCounterOpportunityActive() ? 1f : 0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        FillDefaultReferences();

        if (self == null || opponent == null || actionController == null)
        {
            AddReward(-0.01f);
            return;
        }

        // 이전 decision 이후 발생한 피격/타격 결과 보상.
        ApplyHealthChangeRewards();

        if (TryEndTrainingEpisode())
        {
            return;
        }

        Vector3 directionToOpponent = DirectionToOpponent();
        float distance = DistanceToOpponent();

        CombatActionController opponentAction = opponent.ActionController;
        bool opponentIsAttacking = opponentAction != null && opponentAction.IsAttacking;

        ActionSegment<int> discreteActions = actions.DiscreteActions;

        int moveAction = discreteActions.Length > 0
            ? discreteActions[0]
            : MoveNone;

        int skillAction = discreteActions.Length > 1
            ? discreteActions[1]
            : SkillNone;

        ApplySkillChoicePenalty(skillAction, distance);

        // 상대 공격에 대한 방어 반응 보상.
        ApplyDefensiveResponseReward(skillAction, distance, opponentIsAttacking);

        // 방어 이후 카운터 선택 보상.
        ApplyCounterOpportunityReward(moveAction, skillAction, distance);

        // 일반 공격 시도 보상.
        ApplyAttackAttemptReward(skillAction, distance);

        // Attack을 선택한 decision에서는 이동을 생략한다.
        // 이동 후 공격하면 Kill Zone 밖으로 밀려나거나 회전이 꼬여 공격이 실패할 수 있기 때문이다.
        if (skillAction != SkillAttack)
        {
            ExecuteMovementAction(moveAction, directionToOpponent);
        }

        ExecuteSkillAction(skillAction, directionToOpponent);

        ApplyStepRewards(moveAction, skillAction);

        if (TryEndTrainingEpisode())
        {
            return;
        }

        previousSelfHealth = self.CurrentHealth;
        previousOpponentHealth = opponent.CurrentHealth;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;

        discreteActions[0] = MoveNone;
        discreteActions[1] = SkillNone;

        // Movement branch
        // W: move toward opponent
        // S: move back
        // A: move left
        // D: move right
        if (Input.GetKey(KeyCode.W))
        {
            discreteActions[0] = MoveToward;
        }
        else if (Input.GetKey(KeyCode.S))
        {
            discreteActions[0] = MoveBack;
        }
        else if (Input.GetKey(KeyCode.A))
        {
            discreteActions[0] = MoveLeft;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            discreteActions[0] = MoveRight;
        }

        // Skill branch
        // J: attack
        // K: block
        // L: dodge back
        if (Input.GetKey(KeyCode.J))
        {
            discreteActions[1] = SkillAttack;
        }
        else if (Input.GetKey(KeyCode.K))
        {
            discreteActions[1] = SkillBlock;
        }
        else if (Input.GetKey(KeyCode.L))
        {
            discreteActions[1] = SkillDodge;
        }
    }

    private void ExecuteMovementAction(int moveAction, Vector3 directionToOpponent)
    {
        if (actionController == null)
        {
            return;
        }

        Vector3 rightDirection = Vector3.Cross(Vector3.up, directionToOpponent);

        if (rightDirection.sqrMagnitude <= 0.0001f)
        {
            rightDirection = transform.right;
        }
        else
        {
            rightDirection.Normalize();
        }

        switch (moveAction)
        {
            case MoveToward:
                actionController.Move(directionToOpponent);
                break;

            case MoveBack:
                actionController.Move(-directionToOpponent);
                break;

            case MoveLeft:
                actionController.Move(-rightDirection);
                break;

            case MoveRight:
                actionController.Move(rightDirection);
                break;

            case MoveNone:
            default:
                break;
        }
    }

    private void ExecuteSkillAction(int skillAction, Vector3 directionToOpponent)
    {
        if (actionController == null)
        {
            return;
        }

        switch (skillAction)
        {
            case SkillAttack:
                actionController.Face(directionToOpponent);
                actionController.Attack();
                break;

            case SkillBlock:
                actionController.Block(directionToOpponent);
                break;

            case SkillDodge:
                actionController.Face(directionToOpponent);
                actionController.Dodge(-directionToOpponent);
                break;

            case SkillNone:
            default:
                break;
        }
    }

    private void ApplyDefensiveResponseReward(
        int skillAction,
        float distance,
        bool opponentIsAttacking)
    {
        if (!opponentIsAttacking || actionController == null || actionController.IsBusy)
        {
            return;
        }

        // 실제 공격형 사거리 1.5보다 약간 넓은 threat 구역에서만 방어 반응을 강화한다.
        if (distance > ThreatDistance)
        {
            return;
        }

        if (skillAction == SkillBlock
            && cooldownSystem != null
            && cooldownSystem.IsBlockReady())
        {
            AddReward(BlockResponseReward);
            StartCounterOpportunity();
        }
        else if (skillAction == SkillDodge
            && cooldownSystem != null
            && cooldownSystem.IsDodgeReady())
        {
            AddReward(DodgeResponseReward);
            StartCounterOpportunity();
        }
        else if (skillAction == SkillNone)
        {
            AddReward(-0.03f);
        }
    }

    private void ApplyCounterOpportunityReward(
        int moveAction,
        int skillAction,
        float distance)
    {
        if (!IsCounterOpportunityActive())
        {
            return;
        }

        if (actionController == null || cooldownSystem == null)
        {
            return;
        }

        bool canCounterNow =
            !actionController.IsBusy
            && cooldownSystem.IsAttackReady()
            && distance <= DefenderAttackRange
            && FacingOpponentScore() >= FacingRewardThreshold;

        if (!canCounterNow)
        {
            return;
        }

        if (skillAction == SkillAttack)
        {
            AddReward(CounterAttackChoiceReward);
            lastCounterAttackRequestTime = Time.time;

            if (moveAction == MoveBack)
            {
                AddReward(-0.02f);
            }

            return;
        }

        // 방어 후 공격 가능한 상황인데 또 물러나거나 아무것도 안 하면 카운터를 배울 수 없다.
        if (skillAction == SkillNone
            || skillAction == SkillBlock
            || skillAction == SkillDodge
            || moveAction == MoveBack)
        {
            AddReward(CounterSkipPenalty);
        }
    }

    private void ApplyAttackAttemptReward(int skillAction, float distance)
    {
        if (skillAction != SkillAttack)
        {
            return;
        }

        if (actionController == null || cooldownSystem == null)
        {
            return;
        }

        // 방어 직후 busy 상태에서 Attack을 누르는 것은 흔한 탐색 과정이다.
        // 실제 공격은 안 나가지만, 여기서 강한 페널티를 주면 카운터 학습이 막힐 수 있다.
        if (actionController.IsBusy)
        {
            return;
        }

        if (!cooldownSystem.IsAttackReady())
        {
            return;
        }

        if (distance <= DefenderAttackRange
            && FacingOpponentScore() >= FacingRewardThreshold)
        {
            AddReward(AttackAttemptReward);

            if (IsInKillZone(distance))
            {
                AddReward(KillZoneAttackAttemptReward);
            }
        }
    }

    private void ApplyStepRewards(int moveAction, int skillAction)
    {
        if (self == null || opponent == null)
        {
            return;
        }

        float distance = DistanceToOpponent();
        bool inKillZone = IsInKillZone(distance);

        AddReward(SurvivalReward);

        if (inKillZone)
        {
            AddReward(KillZoneReward);
        }

        if (distance < EnemyZoneDistance)
        {
            float penalty = self.CurrentHealthRatio < 0.3f ? -0.035f : -0.02f;
            AddReward(penalty);

            if (moveAction == MoveBack)
            {
                AddReward(0.004f);
            }
        }

        if (distance > TooFarDistance)
        {
            AddReward(TooFarPenalty);
        }

        float facing = FacingOpponentScore();
        if (facing >= FacingRewardThreshold)
        {
            AddReward(0.001f);
        }

        // Kill Zone에 있는데 계속 뒤로 빠지면 공격 기회를 잃는다.
        if (inKillZone && moveAction == MoveBack)
        {
            AddReward(BackFromKillZonePenalty);
        }

        // Kill Zone + 공격 가능 + 정면인데 아무것도 안 하면 소극적 패턴으로 굳을 수 있다.
        if (inKillZone
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady()
            && facing >= FacingRewardThreshold
            && skillAction == SkillNone)
        {
            AddReward(PassiveInKillZonePenalty);
        }

        CombatActionController opponentAction = opponent.ActionController;
        bool opponentIsAttacking = opponentAction != null && opponentAction.IsAttacking;

        if (opponentIsAttacking
            && distance <= ThreatDistance
            && skillAction != SkillBlock
            && skillAction != SkillDodge)
        {
            AddReward(-0.03f);
        }
    }

    private void ApplyHealthChangeRewards()
    {
        if (self == null || opponent == null)
        {
            return;
        }

        float selfDamage = previousSelfHealth - self.CurrentHealth;
        if (selfDamage > 0.001f)
        {
            float hitPenalty = self.CurrentHealthRatio < 0.3f ? -0.45f : -0.30f;
            AddReward(hitPenalty);
        }

        float opponentDamage = previousOpponentHealth - opponent.CurrentHealth;
        if (opponentDamage > 0.001f)
        {
            float damageReward = opponent.CurrentHealthRatio <= 0.3f ? 0.45f : 0.35f;
            AddReward(damageReward);

            bool wasCounter =
                IsCounterOpportunityActive()
                || Time.time - lastDefensiveActionTime <= CounterRewardWindow
                || Time.time - lastCounterAttackRequestTime <= CounterAttackRequestWindow;

            if (wasCounter)
            {
                AddReward(CounterHitBonus);
                ClearCounterOpportunity();
            }
        }
    }

    private void ApplySkillChoicePenalty(int skillAction, float distance)
    {
        if (skillAction == SkillNone || actionController == null || cooldownSystem == null)
        {
            return;
        }

        if (actionController.IsBusy)
        {
            // 방어 직후 Attack 선택을 무조건 벌주면 카운터 학습이 막힌다.
            if (skillAction == SkillAttack && IsCounterOpportunityActive())
            {
                return;
            }

            AddReward(-0.03f);
            return;
        }

        if (skillAction == SkillAttack && !cooldownSystem.IsAttackReady())
        {
            AddReward(-0.04f);
        }
        else if (skillAction == SkillBlock && !cooldownSystem.IsBlockReady())
        {
            AddReward(-0.04f);
        }
        else if (skillAction == SkillDodge && !cooldownSystem.IsDodgeReady())
        {
            AddReward(-0.04f);
        }

        if (skillAction == SkillAttack)
        {
            if (distance > DefenderAttackRange + 0.05f)
            {
                AddReward(-0.025f);
            }
            else if (distance < EnemyZoneDistance && !IsCounterOpportunityActive())
            {
                AddReward(-0.015f);
            }
        }
    }

    private bool TryEndTrainingEpisode()
    {
        if (self == null || opponent == null)
        {
            return false;
        }

        bool selfDead = self.IsDead;
        bool opponentDead = opponent.IsDead;

        if (selfDead && opponentDead)
        {
            AddReward(0f);
            EndEpisode();
            return true;
        }

        if (opponentDead)
        {
            AddReward(1.0f);
            EndEpisode();
            return true;
        }

        if (selfDead)
        {
            AddReward(-1.0f);
            EndEpisode();
            return true;
        }

        if (Time.time - episodeStartTime >= MaxTrainingEpisodeTime)
        {
            AddReward(0.25f);
            EndEpisode();
            return true;
        }

        return false;
    }

    private void StartCounterOpportunity()
    {
        counterOpportunityActive = true;
        counterOpportunityEndTime = Time.time + CounterRewardWindow;
        lastDefensiveActionTime = Time.time;
    }

    private void ClearCounterOpportunity()
    {
        counterOpportunityActive = false;
        counterOpportunityEndTime = -999f;
    }

    private void ResetCounterState()
    {
        counterOpportunityActive = false;
        counterOpportunityEndTime = -999f;
        lastDefensiveActionTime = -999f;
        lastCounterAttackRequestTime = -999f;
    }

    private bool IsCounterOpportunityActive()
    {
        if (!counterOpportunityActive)
        {
            return false;
        }

        if (Time.time > counterOpportunityEndTime)
        {
            ClearCounterOpportunity();
            return false;
        }

        return true;
    }

    private bool IsInKillZone(float distance)
    {
        return distance >= KillZoneMin && distance <= KillZoneMax;
    }

    private Vector3 DirectionToOpponent()
    {
        if (opponent == null)
        {
            return transform.forward;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= 0.0001f)
        {
            return transform.forward;
        }

        return offset.normalized;
    }

    private float DistanceToOpponent()
    {
        if (opponent == null)
        {
            return MaxObservationDistance;
        }

        Vector3 offset = opponent.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private float FacingOpponentScore()
    {
        Vector3 directionToOpponent = DirectionToOpponent();

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        forward.Normalize();

        return Mathf.Clamp(Vector3.Dot(forward, directionToOpponent), -1f, 1f);
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

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }

        if (opponent == null)
        {
            string opponentName = gameObject.name == "Agent_A" ? "Agent_B" : "Agent_A";
            GameObject found = GameObject.Find(opponentName);

            if (found != null)
            {
                opponent = found.GetComponent<CombatCharacter>();
            }
        }
    }
}