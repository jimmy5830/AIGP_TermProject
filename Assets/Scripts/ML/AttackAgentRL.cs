using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CombatActionController))]
[RequireComponent(typeof(CooldownSystem))]
public class AttackAgentRL : Agent
{
    [Header("References")]
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter opponent;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;
    [SerializeField] private EpisodeManager episodeManager;

    [Header("Movement")]
    [SerializeField] private float moveDecisionThreshold = 0.15f;

    [Header("Fallback Attack Events")]
    [SerializeField] private bool useAttackFallback = true;
    [SerializeField] private float fallbackAttackHitTime = 0.20f;
    [SerializeField] private float fallbackAttackEndTime = 0.55f;
    [SerializeField] private float maxEpisodeSeconds = 30f;

    [Header("Phase 1 Attack Training")]
    [SerializeField] private bool phase1AttackOnly = true;
    [SerializeField] private bool phase1ForwardOnlyMove = true; // true면 backward만 금지, left/right는 허용

    private float episodeStartTime;

    private int episodeMoveNoneCount;
    private int episodeMoveForwardCount;
    private int episodeMoveBackwardCount;
    private int episodeMoveLeftCount;
    private int episodeMoveRightCount;

    private int episodeSkillNoneCount;
    private int episodeSkillAttackCount;
    private int episodeSkillBlockCount;
    private int episodeSkillDodgeCount;

    private int episodeSkillAttackSelectedCount;
    private int episodeAttackCallCount;
    private int episodeAttackRejectedBusyCount;
    private int episodeAttackRejectedCooldownCount;
    private int episodeAttackStartedObservedCount;

    private int episodeAttackRejectedOutOfRangeCount;
    private int episodeAttackRejectedBadAngleCount;

    private float episodeAttackSelectedDistanceSum;
    private float episodeAttackSelectedDistanceMin;
    private float episodeAttackSelectedDistanceMax;

    private float episodeAttackRejectedOutOfRangeDistanceSum;
    private int episodeAttackRejectedOutOfRangeDistanceCount;

    private int previousMoveAction = MoveNone;
    private int previousSkillAction = SkillNone;

    private const int MoveNone = 0;
    private const int MoveForward = 1;
    private const int MoveBackward = 2;
    private const int MoveLeft = 3;
    private const int MoveRight = 4;

    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;

    private const float StepPenalty = -0.0005f;
    private const float WinReward = 1.5f;
    private const float LoseReward = -1.5f;

    private const float DamageToOpponentScale = 0.08f;
    private const float DamageToSelfScale = 0.02f;

    private const float ApproachRewardScale = 0.0f;
    private const float FacingRewardScale = 0.0f;
    private const float AttackOpportunityReward = 0.18f;

    private const float OutOfRangeAttackPenalty = -0.03f;
    private const float BadAngleAttackPenalty = 0.0f;
    private const float IdleFarPenalty = -0.002f;
    private const float AttackSpamPenalty = -0.1f;
    private const float BlockSpamPenalty = -0.005f;
    private const float DodgeSpamPenalty = -0.005f;
    private const float FarDodgePenalty = -0.003f;
    private const float RetreatPenaltyScale = 0.008f;
    private const float FarBackwardPenalty = -0.004f;

    private const float AttackStartedReward = 0.12f;
    private const float CloseRangeNonePenalty = -0.015f;
    private const float CloseRangeStrafeNonePenalty = -0.01f;
    private const float RepeatedLateralMovePenalty = -0.008f;

    private const float CloseRangeStayReward = 0.004f;
    private const float CloseRetreatPenalty = -0.02f;
    private const float VeryFarPenalty = -0.02f;

    private const float AttackIntoBlockPenalty = -0.015f;
    private const float IdleInCloseRangePenalty = -0.015f;
    private const float TimeoutPenalty = -0.3f;

    private const float AttackRangeThreshold = 3.5f;
    private const float GoodFacingThreshold = -0.2f;

    private const float RepeatedAttackSelectionPenalty = -0.04f;

    private float previousDistance;
    private float previousFacingDot;
    private float previousSelfHealth;
    private float previousOpponentHealth;

    private bool episodeEnded;
    private bool attackFallbackActive;
    private bool fallbackHitTriggered;
    private float attackStartTime = -999f;

    public CombatCharacter Self => self;
    public CombatCharacter Opponent => opponent;
    public CombatActionController ActionController => actionController;
    public CooldownSystem CooldownSystem => cooldownSystem;

    public override void Initialize()
    {
        FillDefaultReferences();
        ResetInternalEpisodeState();
        episodeStartTime = Time.time;
    }

    public override void OnEpisodeBegin()
    {
        FillDefaultReferences();
        ResetInternalEpisodeState();
        episodeStartTime = Time.time;
    }

    private void Update()
    {
        if (episodeEnded)
            return;

        if (self == null || opponent == null || actionController == null || cooldownSystem == null)
            return;

        if (Time.time - episodeStartTime >= maxEpisodeSeconds)
        {
            AddReward(TimeoutPenalty);
            EndCombatEpisode("timeout");
            return;
        }

        if (self.IsDead)
        {
            AddReward(LoseReward);
            EndCombatEpisode("self_dead");
            return;
        }

        if (opponent.IsDead)
        {
            AddReward(WinReward);
            EndCombatEpisode("opponent_dead");
            return;
        }

        if (useAttackFallback && attackFallbackActive)
        {
            float elapsed = Time.time - attackStartTime;

            if (!fallbackHitTriggered && elapsed >= fallbackAttackHitTime)
            {
                fallbackHitTriggered = true;
                actionController.OnAttackHitFrame();
            }

            if (elapsed >= fallbackAttackEndTime)
            {
                attackFallbackActive = false;
                fallbackHitTriggered = false;
                actionController.OnAttackEnd();
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (self == null || opponent == null || cooldownSystem == null)
        {
            for (int i = 0; i < 14; i++)
                sensor.AddObservation(0f);
            return;
        }

        Vector3 toOpponent = opponent.transform.position - transform.position;
        Vector3 local = transform.InverseTransformDirection(toOpponent);

        float distance = toOpponent.magnitude;
        float normalizedDistance = Mathf.Clamp01(distance / 10f);
        float facingDot = ComputeFacingDot();

        sensor.AddObservation(self.CurrentHealthRatio);
        sensor.AddObservation(opponent.CurrentHealthRatio);

        sensor.AddObservation(local.x / 10f);
        sensor.AddObservation(local.z / 10f);

        sensor.AddObservation(normalizedDistance);
        sensor.AddObservation(facingDot);

        sensor.AddObservation(cooldownSystem.GetAttackCooldownRatio());
        sensor.AddObservation(cooldownSystem.GetBlockCooldownRatio());
        sensor.AddObservation(cooldownSystem.GetDodgeCooldownRatio());

        sensor.AddObservation(actionController.IsAttacking ? 1f : 0f);
        sensor.AddObservation(actionController.IsBlocking ? 1f : 0f);
        sensor.AddObservation(0f);

        sensor.AddObservation(opponent.ActionController != null && opponent.ActionController.IsBlocking ? 1f : 0f);
        sensor.AddObservation(0f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (episodeEnded)
            return;

        if (self == null || opponent == null || actionController == null || cooldownSystem == null)
            return;

        AddReward(StepPenalty);

        int moveAction = actions.DiscreteActions[0];
        int skillAction = actions.DiscreteActions[1];

        // Phase 1: block/dodge 제거하고 None/Attack만 학습

        float selfHealthBefore = self.CurrentHealthRatio;
        float opponentHealthBefore = opponent.CurrentHealthRatio;

        float currentDistance = Vector3.Distance(transform.position, opponent.transform.position);
        float currentFacingDot = ComputeFacingDot();

        bool opponentAttacking = opponent.ActionController != null && opponent.ActionController.IsAttacking;
        bool allowReactiveBlock = opponentAttacking && currentDistance <= AttackRangeThreshold * 1.2f;



        if (phase1AttackOnly)
        {
            if (skillAction == SkillDodge)
            {
                skillAction = SkillNone;
            }
            else if (skillAction == SkillBlock && !allowReactiveBlock)
            {
                skillAction = SkillNone;
            }
        }

        // Phase 1: 이동도 None/Forward만 허용
        if (phase1ForwardOnlyMove)
        {
            if (moveAction == MoveBackward)
            {
                moveAction = MoveForward;
            }
        }

        switch (moveAction)
        {
            case MoveNone: episodeMoveNoneCount++; break;
            case MoveForward: episodeMoveForwardCount++; break;
            case MoveBackward: episodeMoveBackwardCount++; break;
            case MoveLeft: episodeMoveLeftCount++; break;
            case MoveRight: episodeMoveRightCount++; break;
        }

        switch (skillAction)
        {
            case SkillNone:
                episodeSkillNoneCount++;
                break;
            case SkillAttack:
                episodeSkillAttackCount++;
                episodeSkillAttackSelectedCount++;
                break;
            case SkillBlock:
                episodeSkillBlockCount++;
                break;
            case SkillDodge:
                episodeSkillDodgeCount++;
                break;
        }

        if (skillAction == SkillAttack)
        {
            episodeAttackSelectedDistanceSum += currentDistance;
            if (currentDistance < episodeAttackSelectedDistanceMin) episodeAttackSelectedDistanceMin = currentDistance;
            if (currentDistance > episodeAttackSelectedDistanceMax) episodeAttackSelectedDistanceMax = currentDistance;
        }

        Vector3 moveDirection = GetMoveDirection(moveAction);
        if (moveDirection != Vector3.zero)
        {
            actionController.Move(moveDirection);
        }

        bool inAttackRange = currentDistance <= AttackRangeThreshold;
        bool goodFacing = currentFacingDot >= GoodFacingThreshold;
        bool attackReady = cooldownSystem.IsAttackReady();
        bool blockReady = cooldownSystem.IsBlockReady();
        bool dodgeReady = cooldownSystem.IsDodgeReady();



        bool opponentBlocking = opponent.ActionController != null && opponent.ActionController.IsBlocking;
        bool isStrafing = moveAction == MoveLeft || moveAction == MoveRight;
        bool isIdle = moveAction == MoveNone;
        bool inCloseRange = currentDistance <= AttackRangeThreshold * 1.05f;
        bool isFar = currentDistance > AttackRangeThreshold * 1.5f;
        bool isVeryFar = currentDistance > AttackRangeThreshold * 2.5f;
        bool isRetreating = moveAction == MoveBackward;
        bool hasGoodAttackWindow = inAttackRange && attackReady && !actionController.IsBusy;
        bool hasCooldownReadyAttackWindow = inAttackRange && attackReady;

        bool repeatedAttackSelection = skillAction == SkillAttack && previousSkillAction == SkillAttack;

        switch (skillAction)
        {
            case SkillAttack:
                if (!attackReady)
                {
                    episodeAttackRejectedCooldownCount++;
                    AddReward(AttackSpamPenalty);

                    if (repeatedAttackSelection)
                    {
                        AddReward(RepeatedAttackSelectionPenalty);
                    }

                    skillAction = SkillNone;
                }
                else if (!inAttackRange)
                {
                    episodeAttackRejectedOutOfRangeCount++;
                    episodeAttackRejectedOutOfRangeDistanceSum += currentDistance;
                    episodeAttackRejectedOutOfRangeDistanceCount++;
                    AddReward(OutOfRangeAttackPenalty);
                    skillAction = SkillNone;
                }
                else
                {
                    if (hasCooldownReadyAttackWindow)
                        AddReward(AttackOpportunityReward);

                    if (opponentBlocking)
                        AddReward(AttackIntoBlockPenalty);

                    if (actionController.IsBusy)
                    {
                        episodeAttackRejectedBusyCount++;
                        AddReward(AttackSpamPenalty);

                        if (repeatedAttackSelection)
                        {
                            AddReward(RepeatedAttackSelectionPenalty);
                        }

                        skillAction = SkillNone;
                        break;
                    }

                    episodeAttackCallCount++;
                    actionController.Attack();

                    if (actionController.IsAttacking)
                    {
                        episodeAttackStartedObservedCount++;
                        AddReward(AttackStartedReward);
                    }
                    else
                    {
                        skillAction = SkillNone;
                    }

                    if (useAttackFallback && actionController.IsAttacking)
                    {
                        attackFallbackActive = true;
                        fallbackHitTriggered = false;
                        attackStartTime = Time.time;
                    }
                }
                break;

            case SkillBlock:
                if (!blockReady)
                {
                    AddReward(BlockSpamPenalty);
                }
                else
                {
                    if (currentDistance > AttackRangeThreshold * 1.4f)
                        AddReward(BlockSpamPenalty);

                    actionController.Block(opponent.transform.position - transform.position);
                }
                break;

            case SkillDodge:
                if (!dodgeReady)
                {
                    AddReward(DodgeSpamPenalty);
                }
                else
                {
                    if (currentDistance > AttackRangeThreshold * 1.6f)
                        AddReward(FarDodgePenalty);

                    Vector3 dodgeDirection = moveDirection != Vector3.zero
                        ? moveDirection.normalized
                        : -transform.forward;

                    actionController.Dodge(dodgeDirection);
                }
                break;
        }

        float selfHealthAfter = self.CurrentHealthRatio;
        float opponentHealthAfter = opponent.CurrentHealthRatio;

        float damageToOpponent = Mathf.Max(0f, opponentHealthBefore - opponentHealthAfter);
        float damageToSelf = Mathf.Max(0f, selfHealthBefore - selfHealthAfter);

        AddReward(damageToOpponent * DamageToOpponentScale);
        AddReward(damageToSelf * -DamageToSelfScale);

        if (isFar && isIdle)
        {
            AddReward(IdleFarPenalty);
        }

        if (isFar && isRetreating)
        {
            AddReward(FarBackwardPenalty);
        }

        if (isVeryFar)
        {
            AddReward(VeryFarPenalty);
        }

        if (inCloseRange)
        {
            AddReward(CloseRangeStayReward);
        }

        if (inCloseRange && isRetreating)
        {
            AddReward(CloseRetreatPenalty);
        }

        if (inCloseRange && skillAction == SkillNone)
        {
            AddReward(CloseRangeNonePenalty);
        }

        if (inCloseRange && isStrafing && skillAction == SkillNone)
        {
            AddReward(CloseRangeStrafeNonePenalty);
        }

        if (inCloseRange && hasCooldownReadyAttackWindow && skillAction != SkillAttack)
        {
            AddReward(IdleInCloseRangePenalty);
        }

        if (currentDistance > AttackRangeThreshold)
        {
            float distanceDelta = previousDistance - currentDistance;

            if (distanceDelta < 0f)
            {
                AddReward(distanceDelta * RetreatPenaltyScale);
            }
        }

        bool repeatedSameLateralMove =
            (moveAction == MoveLeft || moveAction == MoveRight) &&
            moveAction == previousMoveAction;

        if (inCloseRange && repeatedSameLateralMove && skillAction == SkillNone)
        {
            AddReward(RepeatedLateralMovePenalty);
        }

        previousDistance = currentDistance;
        previousFacingDot = currentFacingDot;
        previousSelfHealth = selfHealthAfter;
        previousOpponentHealth = opponentHealthAfter;
        previousMoveAction = moveAction;
        previousSkillAction = skillAction;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = MoveNone;
        discrete[1] = SkillNone;

        if (Input.GetKey(KeyCode.W)) discrete[0] = MoveForward;
        else if (Input.GetKey(KeyCode.A)) discrete[0] = MoveLeft;
        else if (Input.GetKey(KeyCode.D)) discrete[0] = MoveRight;
        else discrete[0] = MoveNone;

        if (Input.GetKey(KeyCode.J)) discrete[1] = SkillAttack;
        else if (Input.GetKey(KeyCode.K)) discrete[1] = SkillBlock;
        else discrete[1] = SkillNone;
    }

    private void EndCombatEpisode(string reason = "unknown")
    {
        if (episodeEnded)
            return;

        LogEpisodeActionStats(reason);

        episodeEnded = true;
        attackFallbackActive = false;
        fallbackHitTriggered = false;
        attackStartTime = -999f;
        EndEpisode();
    }

    private void ResetInternalEpisodeState()
    {
        episodeEnded = false;
        attackFallbackActive = false;
        fallbackHitTriggered = false;
        attackStartTime = -999f;
        episodeStartTime = Time.time;
        previousMoveAction = MoveNone;
        previousSkillAction = SkillNone;

        if (self != null)
            previousSelfHealth = self.CurrentHealthRatio;
        else
            previousSelfHealth = 0f;

        if (opponent != null)
            previousOpponentHealth = opponent.CurrentHealthRatio;
        else
            previousOpponentHealth = 0f;

        if (opponent != null)
        {
            previousDistance = Vector3.Distance(transform.position, opponent.transform.position);
            previousFacingDot = ComputeFacingDot();
        }
        else
        {
            previousDistance = 0f;
            previousFacingDot = 0f;
        }

        episodeMoveNoneCount = 0;
        episodeMoveForwardCount = 0;
        episodeMoveBackwardCount = 0;
        episodeMoveLeftCount = 0;
        episodeMoveRightCount = 0;

        episodeSkillNoneCount = 0;
        episodeSkillAttackCount = 0;
        episodeSkillBlockCount = 0;
        episodeSkillDodgeCount = 0;

        episodeSkillAttackSelectedCount = 0;
        episodeAttackCallCount = 0;
        episodeAttackRejectedBusyCount = 0;
        episodeAttackRejectedCooldownCount = 0;
        episodeAttackStartedObservedCount = 0;

        episodeAttackRejectedOutOfRangeCount = 0;
        episodeAttackRejectedBadAngleCount = 0;

        episodeAttackSelectedDistanceSum = 0f;
        episodeAttackSelectedDistanceMin = float.MaxValue;
        episodeAttackSelectedDistanceMax = 0f;

        episodeAttackRejectedOutOfRangeDistanceSum = 0f;
        episodeAttackRejectedOutOfRangeDistanceCount = 0;
    }

    private void LogEpisodeActionStats(string endReason)
    {
        float avgAttackSelectedDistance = episodeSkillAttackSelectedCount > 0
            ? episodeAttackSelectedDistanceSum / episodeSkillAttackSelectedCount
            : 0f;

        float minAttackSelectedDistance = episodeSkillAttackSelectedCount > 0
            ? episodeAttackSelectedDistanceMin
            : 0f;

        float maxAttackSelectedDistance = episodeSkillAttackSelectedCount > 0
            ? episodeAttackSelectedDistanceMax
            : 0f;

        float avgOutOfRangeRejectDistance = episodeAttackRejectedOutOfRangeDistanceCount > 0
            ? episodeAttackRejectedOutOfRangeDistanceSum / episodeAttackRejectedOutOfRangeDistanceCount
            : 0f;

        Debug.Log(
            $"[AttackAgentRL][EpisodeEnd:{endReason}] " +
            $"Move(None/Fwd/Back/Left/Right)=({episodeMoveNoneCount}/{episodeMoveForwardCount}/{episodeMoveBackwardCount}/{episodeMoveLeftCount}/{episodeMoveRightCount}), " +
            $"Skill(None/Atk/Block/Dodge)=({episodeSkillNoneCount}/{episodeSkillAttackCount}/{episodeSkillBlockCount}/{episodeSkillDodgeCount}), " +
            $"AtkDiag(Selected/Call/BusyReject/CooldownReject/OutOfRangeReject/BadAngleReject/Started)=({episodeSkillAttackSelectedCount}/{episodeAttackCallCount}/{episodeAttackRejectedBusyCount}/{episodeAttackRejectedCooldownCount}/{episodeAttackRejectedOutOfRangeCount}/{episodeAttackRejectedBadAngleCount}/{episodeAttackStartedObservedCount}), " +
            $"CumReward={GetCumulativeReward():F3}" +
            $"AtkDist(Avg/Min/Max)=({avgAttackSelectedDistance:F2}/{minAttackSelectedDistance:F2}/{maxAttackSelectedDistance:F2}), " +
            $"OutRangeAvgDist={avgOutOfRangeRejectDistance:F2}, "
        );
    }

    private void FillDefaultReferences()
    {
        if (self == null)
            self = GetComponent<CombatCharacter>();

        if (actionController == null)
            actionController = GetComponent<CombatActionController>();

        if (cooldownSystem == null)
            cooldownSystem = GetComponent<CooldownSystem>();

        if (episodeManager == null)
            episodeManager = FindFirstObjectByType<EpisodeManager>();

        if (opponent == null)
        {
            CombatCharacter[] allCharacters = FindObjectsByType<CombatCharacter>(FindObjectsSortMode.None);
            foreach (var character in allCharacters)
            {
                if (character != self)
                {
                    opponent = character;
                    break;
                }
            }
        }
    }

    private float ComputeFacingDot()
    {
        if (opponent == null)
            return 0f;

        Vector3 toOpponent = (opponent.transform.position - transform.position).normalized;
        return Vector3.Dot(transform.forward, toOpponent);
    }

    private Vector3 GetMoveDirection(int moveAction)
    {
        if (opponent == null)
            return Vector3.zero;

        Vector3 toOpponent = opponent.transform.position - transform.position;
        toOpponent.y = 0f;

        if (toOpponent.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        toOpponent.Normalize();

        Vector3 orbitLeft = Vector3.Cross(Vector3.up, toOpponent).normalized;
        Vector3 orbitRight = Vector3.Cross(toOpponent, Vector3.up).normalized;

        switch (moveAction)
        {
            case MoveForward:
                return toOpponent;
            case MoveBackward:
                return -toOpponent;
            case MoveLeft:
                return orbitLeft;
            case MoveRight:
                return orbitRight;
            default:
                return Vector3.zero;
        }
    }
}
