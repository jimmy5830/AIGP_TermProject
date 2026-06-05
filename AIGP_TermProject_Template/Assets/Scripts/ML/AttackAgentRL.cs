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

    private const float DamageToOpponentScale = 0.02f;
    private const float DamageToSelfScale = 0.025f;

    private const float ApproachRewardScale = 0.003f;
    private const float FacingRewardScale = 0.002f;
    private const float AttackOpportunityReward = 0.01f;

    private const float OutOfRangeAttackPenalty = -0.01f;
    private const float BadAngleAttackPenalty = -0.008f;
    private const float AttackSpamPenalty = -0.01f;
    private const float BlockSpamPenalty = -0.005f;
    private const float DodgeSpamPenalty = -0.005f;
    private const float FarDodgePenalty = -0.003f;

    // Reward v1.5
    private const float AttackIntoBlockPenalty = -0.015f;
    private const float StrafeVsBlockingReward = 0.006f;
    private const float StrafeWhileAttackCooldownReward = 0.004f;
    private const float IdleInCloseRangePenalty = -0.002f;

    private const float AttackRangeThreshold = 2.0f;
    private const float GoodFacingThreshold = 0.75f;

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
    }

    public override void OnEpisodeBegin()
    {
        FillDefaultReferences();
        ResetInternalEpisodeState();
    }

    private void Update()
    {
        if (episodeEnded)
            return;

        if (self == null || opponent == null || actionController == null || cooldownSystem == null)
            return;

        if (self.IsDead)
        {
            AddReward(LoseReward);
            EndCombatEpisode();
            return;
        }

        if (opponent.IsDead)
        {
            AddReward(WinReward);
            EndCombatEpisode();
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

        float selfHealthBefore = self.CurrentHealthRatio;
        float opponentHealthBefore = opponent.CurrentHealthRatio;

        float currentDistance = Vector3.Distance(transform.position, opponent.transform.position);
        float currentFacingDot = ComputeFacingDot();

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

        switch (skillAction)
        {
            case SkillAttack:
                if (!attackReady)
                {
                    AddReward(AttackSpamPenalty);
                }
                else
                {
                    if (!inAttackRange)
                        AddReward(OutOfRangeAttackPenalty);

                    if (!goodFacing)
                        AddReward(BadAngleAttackPenalty);

                    if (inAttackRange && goodFacing)
                        AddReward(AttackOpportunityReward);

                    if (opponentBlocking && inAttackRange)
                        AddReward(AttackIntoBlockPenalty);

                    actionController.Attack();

                    if (useAttackFallback)
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

        // Reward v1.5: close-range reposition shaping
        if (inCloseRange)
        {
            if (opponentBlocking && isStrafing)
            {
                AddReward(StrafeVsBlockingReward);
            }

            if (!attackReady && isStrafing)
            {
                AddReward(StrafeWhileAttackCooldownReward);
            }

            if ((opponentBlocking || !attackReady) && isIdle)
            {
                AddReward(IdleInCloseRangePenalty);
            }
        }

        if (currentDistance > AttackRangeThreshold)
        {
            float distanceDelta = previousDistance - currentDistance;
            if (distanceDelta > 0f)
            {
                AddReward(distanceDelta * ApproachRewardScale);
            }
        }

        if (currentDistance <= AttackRangeThreshold * 1.3f)
        {
            float facingDelta = currentFacingDot - previousFacingDot;
            if (facingDelta > 0f)
            {
                AddReward(facingDelta * FacingRewardScale);
            }
        }

        previousDistance = currentDistance;
        previousFacingDot = currentFacingDot;
        previousSelfHealth = selfHealthAfter;
        previousOpponentHealth = opponentHealthAfter;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = MoveNone;
        discrete[1] = SkillNone;

        if (Input.GetKey(KeyCode.W)) discrete[0] = MoveForward;
        else if (Input.GetKey(KeyCode.S)) discrete[0] = MoveBackward;
        else if (Input.GetKey(KeyCode.A)) discrete[0] = MoveLeft;
        else if (Input.GetKey(KeyCode.D)) discrete[0] = MoveRight;

        if (Input.GetKey(KeyCode.J)) discrete[1] = SkillAttack;
        else if (Input.GetKey(KeyCode.K)) discrete[1] = SkillBlock;
        else if (Input.GetKey(KeyCode.L)) discrete[1] = SkillDodge;
    }

    private void EndCombatEpisode()
    {
        if (episodeEnded)
            return;

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
