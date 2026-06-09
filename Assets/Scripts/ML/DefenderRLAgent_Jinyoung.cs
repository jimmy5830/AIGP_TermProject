using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
public class DefenderRLAgent_Jinyoung : Agent
{
    public CombatCharacter self;
    public CombatCharacter opponent;
    public CombatActionController actionController;
    public CooldownSystem cooldownSystem;
    public CooldownSystem opponentCooldownSystem;
    public EpisodeManager episodeManager;

    // Branch 0: movement (0=stop / 1=forward / 2=back / 3=left / 4=right)
    // Branch 1: combat  (0=none  / 1=block  / 2=dodge / 3=attack)
    private const int SkillNone   = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock  = 2;
    private const int SkillDodge  = 3;

    // Step rewards
    private const float RewardSurvivePerStep    =  0.002f;  // v2: 0.001 → 0.002
    private const float RewardBlockSuccess      =  0.4f;
    private const float RewardDodgeSuccess      =  0.3f;
    private const float RewardCounterAttack     =  0.5f;
    private const float PenaltyHit              = -0.3f;
    private const float PenaltyCooldownWaste    = -0.005f;  // v2: -0.05 → -0.005 (탐색 억제 해소)
    private const float RewardDefensiveAttempt  =  0.02f;   // v2: 상대 공격 중 Block/Dodge 시도 장려

    // V3: Zone-based distance rewards (BT Kill Zone 1.4~1.7f 전략 반영)
    private const float KillZoneMin       = 1.4f;
    private const float KillZoneMax       = 1.7f;
    private const float EnemyZoneMax      = 1.2f;
    private const float RewardKillZone    =  0.005f;  // Kill Zone 유지 소액 보상
    private const float PenaltyEnemyZone  = -0.02f;   // Enemy Zone 강제 이탈 페널티

    // V3: Poke & Retreat 시퀀스 보상 (BT Phase 1·2 핵심 패턴)
    private const float RewardPokeRetreat =  0.15f;

    // V3: Finisher 모드 — 양쪽 HP<30%일 때 반격 추가 보상 (BT Phase 3 예외)
    private const float RewardFinisher    =  0.3f;

    // Episode end rewards
    private const float RewardWin            =  1.0f;
    private const float PenaltyLoss          = -1.0f;
    private const float RewardSurviveTimeout =  0.3f;

    private bool  _didBlockLastStep;
    private bool  _didDodgeLastStep;
    private bool  _didAttackLastStep;  // V3: Poke & Retreat 추적
    private float _prevSelfHP;
    private float _prevTargetHP;

    // Block/Dodge 성공 감지용 — 상대 공격 종료 시점 단발 평가
    private bool _prevOpponentWasAttacking;
    private bool _wasBlockingDuringAttack;
    private bool _wasDodgingDuringAttack;
    private bool _tookHitDuringAttack;

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
        _didBlockLastStep         = false;
        _didDodgeLastStep         = false;
        _didAttackLastStep        = false;
        _prevOpponentWasAttacking = false;
        _wasBlockingDuringAttack  = false;
        _wasDodgingDuringAttack   = false;
        _tookHitDuringAttack      = false;
        episodeManager.ResetEpisode();
        _prevSelfHP   = self     != null ? self.CurrentHealth     : 0f;
        _prevTargetHP = opponent != null ? opponent.CurrentHealth : 0f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // [1] 내 HP 비율 — 긴급 회피 판단 기준
        sensor.AddObservation(self.CurrentHealthRatio);

        // [2] 상대 HP 비율 — 반격 타이밍
        sensor.AddObservation(opponent.CurrentHealthRatio);

        // [3] 상대까지 거리 (0~1 정규화, 10f 기준)
        float dist = Vector3.Distance(transform.position, opponent.transform.position);
        sensor.AddObservation(Mathf.Clamp01(dist / 10f));

        // [4~5] 상대 방향 벡터 (x, z) — 방어 유효 각도 판단
        Vector3 toTarget = opponent.transform.position - transform.position;
        toTarget.y = 0f;
        Vector3 dirNorm = toTarget.normalized;
        sensor.AddObservation(dirNorm.x);
        sensor.AddObservation(dirNorm.z);

        // [6] Block 쿨타임 잔량 비율 (0=사용 가능, 1=방금 사용)
        sensor.AddObservation(cooldownSystem.GetBlockCooldownRatio());

        // [7] Dodge 쿨타임 잔량 비율
        sensor.AddObservation(cooldownSystem.GetDodgeCooldownRatio());

        // [8] Attack 쿨타임 잔량 비율 — 반격 타이밍 포착
        sensor.AddObservation(cooldownSystem.GetAttackCooldownRatio());

        // [9] 상대가 공격 모션 중인지 — 즉각 방어 트리거
        sensor.AddObservation(opponent.ActionController.IsAttacking ? 1f : 0f);

        // [10] 내가 Blocking 중인지 — 중복 Block 방지
        sensor.AddObservation(actionController.IsBlocking ? 1f : 0f);

        // [11] 내가 무적(Dodge) 중인지 — 중복 Dodge 방지
        sensor.AddObservation(actionController.IsInvincible ? 1f : 0f);

        // [12] 상대 공격 쿨타임 진행률 — Phase 3: 쿨다운 중에만 공격 타이밍 학습
        float oppAtkCooldown = opponentCooldownSystem != null
            ? opponentCooldownSystem.GetAttackCooldownRatio()
            : 0f;
        sensor.AddObservation(oppAtkCooldown);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        DetectHPChanges();

        int moveAction   = actions.DiscreteActions[0]; // Branch 0
        int combatAction = actions.DiscreteActions[1]; // Branch 1

        // Branch 0: 이동
        Vector3 moveDir = moveAction switch
        {
            1 => transform.forward,
            2 => -transform.forward,
            3 => -transform.right,
            4 => transform.right,
            _ => Vector3.zero
        };
        if (moveDir != Vector3.zero)
            actionController.Move(moveDir);

        // Branch 1: 전투
        Vector3 toTarget = DirectionToTarget();

        switch (combatAction)
        {
            case 1: // Block
                if (cooldownSystem.IsBlockReady() && !actionController.IsBusy)
                    actionController.Block(toTarget);
                break;

            case 2: // Dodge — 적 반대 방향으로 회피
                if (cooldownSystem.IsDodgeReady() && !actionController.IsBusy)
                    actionController.Dodge(-toTarget);
                break;

            case 3: // Attack (반격)
                if (cooldownSystem.IsAttackReady() && !actionController.IsBusy)
                {
                    actionController.Face(toTarget);
                    actionController.Attack();
                }
                break;
        }

        ApplyStepRewards(combatAction);

        if (episodeManager.IsEpisodeDone())
        {
            if (self.IsDead)
                SetReward(PenaltyLoss);
            else if (opponent.IsDead)
                SetReward(RewardWin);
            else
                SetReward(RewardSurviveTimeout); // 시간초과 무승부 — 수비형은 생존도 성공

            EndEpisode();
        }
    }

    // HP 변화량으로 피격 및 반격 성공 감지 (OnActionReceived 시작부에서 호출)
    private void DetectHPChanges()
    {
        float selfHPNow   = self.CurrentHealth;
        float targetHPNow = opponent.CurrentHealth;
        bool selfHPDecreased   = selfHPNow < _prevSelfHP;
        bool targetHPDecreased = targetHPNow < _prevTargetHP;
        bool opponentIsAttacking = opponent.ActionController.IsAttacking;

        if (selfHPDecreased)
            AddReward(PenaltyHit);

        if (targetHPDecreased && (_didBlockLastStep || _didDodgeLastStep))
        {
            AddReward(RewardCounterAttack);
            // V3: Finisher 모드 — 양쪽 HP<30%일 때 추가 보상 (BT Phase 3 예외)
            if (self.CurrentHealthRatio < 0.3f && opponent.CurrentHealthRatio < 0.3f)
                AddReward(RewardFinisher);
        }

        // 상대 공격 진행 중: 내 상태 누적 기록
        if (opponentIsAttacking)
        {
            if (actionController.IsBlocking)   _wasBlockingDuringAttack = true;
            if (actionController.IsInvincible) _wasDodgingDuringAttack  = true;
            if (selfHPDecreased)               _tookHitDuringAttack     = true;
        }

        // 상대 공격 종료 시점 — Block/Dodge 성공 여부를 단발 평가
        if (_prevOpponentWasAttacking && !opponentIsAttacking)
        {
            if (!_tookHitDuringAttack)
            {
                if (_wasBlockingDuringAttack)
                    AddReward(RewardBlockSuccess);
                else if (_wasDodgingDuringAttack)
                    AddReward(RewardDodgeSuccess);
            }
            _wasBlockingDuringAttack = false;
            _wasDodgingDuringAttack  = false;
            _tookHitDuringAttack     = false;
        }

        _prevSelfHP   = selfHPNow;
        _prevTargetHP = targetHPNow;
        _prevOpponentWasAttacking = opponentIsAttacking;
    }

    private void ApplyStepRewards(int combatAction)
    {
        // 생존 보상 (매 step)
        AddReward(RewardSurvivePerStep);

        // V3: 3-Zone 거리 보상 (Kill Zone 유지 유도)
        float dist = Vector3.Distance(transform.position, opponent.transform.position);
        if (dist >= KillZoneMin && dist <= KillZoneMax)
            AddReward(RewardKillZone);       // Kill Zone 유지 소액 보상
        else if (dist < EnemyZoneMax)
            AddReward(PenaltyEnemyZone);     // Enemy Zone 강제 이탈 페널티

        // 쿨타임 중 헛동작 페널티
        if (combatAction == 1 && !cooldownSystem.IsBlockReady())
            AddReward(PenaltyCooldownWaste);
        if (combatAction == 2 && !cooldownSystem.IsDodgeReady())
            AddReward(PenaltyCooldownWaste);
        if (combatAction == 3 && !cooldownSystem.IsAttackReady())
            AddReward(PenaltyCooldownWaste);

        // v2: 상대 공격 중 Block/Dodge 시도 장려 (준비 상태일 때만) — 탐색 유도
        bool oppIsAttacking = opponent.ActionController.IsAttacking;
        if (oppIsAttacking && combatAction == 1 && cooldownSystem.IsBlockReady())
            AddReward(RewardDefensiveAttempt);
        if (oppIsAttacking && combatAction == 2 && cooldownSystem.IsDodgeReady())
            AddReward(RewardDefensiveAttempt);

        // Block/Dodge 직후 반격 시도 소액 보상 (실제 명중은 DetectHPChanges에서 처리)
        if (combatAction == 3 && (_didBlockLastStep || _didDodgeLastStep))
            AddReward(0.1f);

        // V3: Poke & Retreat — 공격 직후 Dodge 시퀀스 보상 (BT Phase 1·2 핵심 패턴)
        if (combatAction == 2 && _didAttackLastStep && cooldownSystem.IsDodgeReady())
            AddReward(RewardPokeRetreat);

        // 이전 step 상태 갱신
        _didBlockLastStep  = actionController.IsBlocking;
        _didDodgeLastStep  = actionController.IsInvincible;
        _didAttackLastStep = (combatAction == 3 && cooldownSystem.IsAttackReady());
    }

    private Vector3 DirectionToTarget()
    {
        Vector3 dir = opponent.transform.position - transform.position;
        dir.y = 0f;
        return dir.normalized;
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

        if (opponentCooldownSystem == null && opponent != null)
            opponentCooldownSystem = opponent.GetComponent<CooldownSystem>();
    }
}
