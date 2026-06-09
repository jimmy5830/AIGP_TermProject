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

    // Defensive RL distance settings.
    // Baseline attacker starts attacking around 1.8,
    // defender attack range is around 2.0.
    // Therefore, the useful outranging zone is narrow.
    private const float EnemyZoneDistance = 1.8f;
    private const float ThreatDistance = 1.9f;

    private const float KillZoneMin = 1.85f;
    private const float KillZoneMax = 1.98f;

    private const float MaxObservationDistance = 5.0f;

    // Reward and episode settings.
    private const float TooFarDistance = 2.25f;
    private const float FacingRewardThreshold = 0.7f;
    private const float MaxTrainingEpisodeTime = 60f;
    private const float CounterRewardWindow = 2.0f;

    // Reward tuning.
    private const float SurvivalReward = 0.0002f;
    private const float KillZoneReward = 0.006f;
    private const float TooFarPenalty = -0.012f;

    private const float AttackAttemptReward = 0.04f;
    private const float BlockResponseReward = 0.15f;
    private const float DodgeResponseReward = 0.10f;

    private const float PassiveInKillZonePenalty = -0.008f;
    private const float BackFromKillZonePenalty = -0.01f;

    // Previous health values used for reward calculation.
    private float previousSelfHealth;
    private float previousOpponentHealth;

    private float episodeStartTime;
    private float lastDefensiveActionTime = -999f;

    public override void Initialize()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public override void OnEpisodeBegin()
    {
        FillDefaultReferences();

        // Keep the ML-Agents episode reset synchronized with the combat scene reset.
        if (episodeManager != null)
        {
            episodeManager.ResetEpisode();
        }

        episodeStartTime = Time.time;
        lastDefensiveActionTime = -999f;

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

        // If references are missing, add zero observations to keep the observation size fixed.
        if (self == null || opponent == null)
        {
            for (int i = 0; i < 12; i++)
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

        // Convert opponent direction into local space.
        // localDirection.z > 0 means opponent is in front.
        // localDirection.x > 0 means opponent is to the right.
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

        // 1 means I am facing the opponent.
        // -1 means I am facing away from the opponent.
        float selfFacingOpponent = Vector3.Dot(selfForward, directionToOpponent);

        // 1 means the opponent is facing me.
        // -1 means the opponent is facing away from me.
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
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        FillDefaultReferences();

        if (self == null || opponent == null || actionController == null)
        {
            AddReward(-0.01f);
            return;
        }

        // 1. Reward from health changes that happened since the last decision.
        ApplyHealthChangeRewards();

        // 2. End immediately if the episode is already over.
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

        // 3. Penalize impossible or wasteful skill choices.
        ApplySkillChoicePenalty(skillAction);

        // 4. Reward defensive reaction when the opponent attacks.
        if (opponentIsAttacking && !actionController.IsBusy)
        {
            if (skillAction == SkillBlock && cooldownSystem != null && cooldownSystem.IsBlockReady())
            {
                AddReward(BlockResponseReward);
                lastDefensiveActionTime = Time.time;
            }
            else if (skillAction == SkillDodge && cooldownSystem != null && cooldownSystem.IsDodgeReady())
            {
                AddReward(DodgeResponseReward);
                lastDefensiveActionTime = Time.time;
            }
            else if (skillAction == SkillNone)
            {
                AddReward(-0.03f);
            }
        }

        // 5. Reward attack attempt in the correct Kill Zone.
        if (skillAction == SkillAttack
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady()
            && distance >= KillZoneMin
            && distance <= KillZoneMax
            && FacingOpponentScore() >= FacingRewardThreshold
            && !actionController.IsBusy)
        {
            AddReward(AttackAttemptReward);
        }

        // 6. Execute movement action.
        ExecuteMovementAction(moveAction, directionToOpponent);

        // 7. Execute combat action.
        ExecuteSkillAction(skillAction, directionToOpponent);

        // 8. Apply distance / position based rewards.
        ApplyStepRewards(moveAction, skillAction);

        // 9. End after executing action if needed.
        if (TryEndTrainingEpisode())
        {
            return;
        }

        // 10. Store health values for the next decision.
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

        rightDirection.Normalize();

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

    private void ApplyStepRewards(int moveAction, int skillAction)
    {
        if (self == null || opponent == null)
        {
            return;
        }

        float distance = DistanceToOpponent();
        bool inKillZone = distance >= KillZoneMin && distance <= KillZoneMax;

        // Very small survival reward.
        AddReward(SurvivalReward);

        // Kill Zone reward.
        if (inKillZone)
        {
            AddReward(KillZoneReward);
        }

        // Enemy Zone penalty. More severe when HP is low.
        if (distance < EnemyZoneDistance)
        {
            float penalty = self.CurrentHealthRatio < 0.3f ? -0.035f : -0.02f;
            AddReward(penalty);

            // Moving back is good only when too close.
            if (moveAction == MoveBack)
            {
                AddReward(0.004f);
            }
        }

        // Stronger penalty for running too far away.
        if (distance > TooFarDistance)
        {
            AddReward(TooFarPenalty);
        }

        // Small reward for facing the opponent.
        float facing = FacingOpponentScore();
        if (facing >= FacingRewardThreshold)
        {
            AddReward(0.001f);
        }

        // Do not back away from the ideal attack zone every time.
        if (inKillZone && moveAction == MoveBack)
        {
            AddReward(BackFromKillZonePenalty);
        }

        // If attack is ready in Kill Zone, doing nothing is bad.
        if (inKillZone
            && cooldownSystem != null
            && cooldownSystem.IsAttackReady()
            && facing >= FacingRewardThreshold
            && skillAction == SkillNone)
        {
            AddReward(PassiveInKillZonePenalty);
        }

        // If the opponent is attacking within threat distance and the agent does not block/dodge, penalize.
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
            float damageReward = opponent.CurrentHealthRatio <= 0.3f ? 0.9f : 0.65f;
            AddReward(damageReward);

            // Extra reward for counterattacking shortly after block/dodge.
            if (Time.time - lastDefensiveActionTime <= CounterRewardWindow)
            {
                AddReward(0.45f);
            }
        }
    }

    private void ApplySkillChoicePenalty(int skillAction)
    {
        if (skillAction == SkillNone || actionController == null || cooldownSystem == null)
        {
            return;
        }

        if (actionController.IsBusy)
        {
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
            // Defensive survival still matters, but timeout should not dominate over fighting.
            AddReward(0.05f);
            EndEpisode();
            return true;
        }

        return false;
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
    }
}