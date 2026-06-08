# CLAUDE.md — 방어형 RL Agent 구현 작업 지시서

> **대상 파일**: `Assets/Scripts/ML/StudentCombatAgent.cs`
> **목표**: 수비형 전략(막기 → 회피 → 반격 → 생존)을 학습하는 RL Agent 완성
> **학습 상대**: `BaselineAttackerBT` (공격형 베이스라인)

---

## 1. 프로젝트 구조 & 핵심 파일 참조

```
Assets/Scripts/
├── BT/
│   ├── BTNode.cs                  # BT 노드 추상 기반 (BTNodeStatus 열거형)
│   ├── BaselineAttackerBT.cs      # RL 학습 상대 (공격형)
│   └── BaselineDefenderBT.cs      # 수비형 전략 참조용 (BT 레퍼런스)
├── Core/
│   ├── CombatActionController.cs  # 핵심 액션 API (Move/Attack/Block/Dodge)
│   ├── CombatCharacter.cs         # HP 상태 (CurrentHealthRatio 등)
│   ├── CooldownSystem.cs          # 쿨타임 조회 (IsAttackReady 등)
│   ├── CombatHitDetector.cs       # 피격 판정 (TryHit → CombatHitResult)
│   └── EpisodeManager.cs          # 에피소드 리셋/종료 (IsEpisodeDone)
└── ML/
    └── StudentCombatAgent.cs      # ← 이 파일만 수정
```

### 핵심 패턴
- 모든 행동은 반드시 `CombatActionController`만 호출 (직접 Rigidbody 조작 금지)
- `CombatAnimatorDriver`는 건드리지 않음 — `CombatActionController`가 내부에서 자동 처리
- `Trigger*Cooldown()`은 직접 호출 불필요 — `Attack()/Block()/Dodge()` 내부에서 자동 호출

---

## 2. Core API 레퍼런스 (수비형에서 사용하는 것만)

### CombatActionController
```csharp
void Move(Vector3 direction)             // IsBusy이면 무시. y축 자동 제거
void Block(Vector3 facingDirection)      // 지정 방향으로 방어 (blockDuration=0.75f 지속)
void Dodge(Vector3 direction)            // 지정 방향 회피 + 무적(0.35f)
void Attack()                            // 공격 (쿨타임·IsBusy 내부 체크)
void UpdateRotationLock(Vector3 dir)     // 방어 중 Update()에서 적 방향 추적 시 호출

bool IsBlocking  { get; }
bool IsInvincible { get; }
bool IsAttacking  { get; }
bool IsBusy       { get; }  // IsAttacking || IsBlocking || IsInvincible
```

### CombatCharacter
```csharp
float CurrentHealthRatio { get; }   // 0~1 (정규화된 HP)
float MaxHealth          { get; }   // 기본 100f
float CurrentHealth      { get; }
bool  IsDead             { get; }
```

### CooldownSystem
```csharp
bool IsAttackReady()             // 공격 가능 여부
bool IsBlockReady()              // 방어 가능 여부
bool IsDodgeReady()              // 회피 가능 여부
float GetAttackCooldownRatio()   // 쿨타임 진행률 (0=쿨중, 1=준비)
float GetBlockCooldownRatio()
float GetDodgeCooldownRatio()
```

### EpisodeManager
```csharp
bool IsEpisodeDone()   // 사망 or 타임아웃 여부
void ResetEpisode()    // 에피소드 리셋 (OnEpisodeBegin에서 호출)
```

---

## 3. Behavior Parameters 설정값 (Unity Inspector와 반드시 일치)

| 항목 | 값 |
|---|---|
| **Behavior Name** | `CombatAgent` (combat_ppo.yaml의 behavior_name과 일치) |
| **Space Size** | `11` (아래 Observation 11개 합산) |
| **Branch 0 크기** | `5` (이동: 정지/앞/뒤/좌/우) |
| **Branch 1 크기** | `4` (전투: 없음/Block/Dodge/Attack) |

> Inspector에서 Space Size, Branch 수를 코드와 다르게 설정하면 학습이 즉시 오류 남.

---

## 4. 구현 명세 — `CollectObservations()`

**Space Size = 11** (AddObservation 호출 총합이 정확히 11이어야 함)

```csharp
public override void CollectObservations(VectorSensor sensor)
{
    // [1] 내 HP 비율 — 긴급 회피 판단 기준
    sensor.AddObservation(self.CurrentHealthRatio);

    // [2] 상대 HP 비율 — 상대 피로도 파악, 반격 타이밍
    sensor.AddObservation(target.CurrentHealthRatio);

    // [3] 상대까지 거리 (0~1 정규화) — 안전거리 판단
    float dist = Vector3.Distance(transform.position, target.transform.position);
    sensor.AddObservation(Mathf.Clamp01(dist / 10f));  // 10f = arenaSize 기준

    // [4~5] 상대 방향 벡터 (x, z) — 방어 유효 각도 판단
    Vector3 toTarget = (target.transform.position - transform.position);
    toTarget.y = 0f;
    Vector3 dirNorm = toTarget.normalized;
    sensor.AddObservation(dirNorm.x);
    sensor.AddObservation(dirNorm.z);

    // [6] Block 쿨타임 진행률 — Block 가능 여부
    sensor.AddObservation(selfCooldown.GetBlockCooldownRatio());

    // [7] Dodge 쿨타임 진행률 — Dodge 가능 여부
    sensor.AddObservation(selfCooldown.GetDodgeCooldownRatio());

    // [8] Attack 쿨타임 진행률 — 반격 타이밍 포착
    sensor.AddObservation(selfCooldown.GetAttackCooldownRatio());

    // [9] 상대가 현재 공격 모션 중인지 (0 or 1) — 즉각 방어 트리거
    sensor.AddObservation(target.ActionController.IsAttacking ? 1f : 0f);

    // [10] 내가 현재 Blocking 중인지 (0 or 1) — 중복 Block 방지
    sensor.AddObservation(selfController.IsBlocking ? 1f : 0f);

    // [11] 내가 현재 Dodging 중인지 (0 or 1) — 중복 Dodge 방지
    sensor.AddObservation(selfController.IsInvincible ? 1f : 0f);
}
```

> **주의**: 추후 Observation을 추가/삭제하면 Inspector의 Space Size도 반드시 동기화.

---

## 5. 구현 명세 — `OnActionReceived()`

### Action Branch 구조
```
Branch 0 (이동, 5가지):  0=정지 / 1=앞 / 2=뒤 / 3=좌 / 4=우
Branch 1 (전투, 4가지):  0=없음 / 1=Block / 2=Dodge / 3=Attack
```

### 기존 상수 활용 (StudentCombatAgent.cs:21-24에 이미 정의됨)
```csharp
private const int SkillNone   = 0;
private const int SkillAttack = 1;   // → Branch 1에서 값 3으로 재배치 (아래 참고)
private const int SkillBlock  = 2;   // → Branch 1에서 값 1
private const int SkillDodge  = 3;   // → Branch 1에서 값 2
```

### 구현 코드
```csharp
public override void OnActionReceived(ActionBuffers actions)
{
    int moveAction   = actions.DiscreteActions[0];  // Branch 0
    int combatAction = actions.DiscreteActions[1];  // Branch 1

    // ── 이동 처리 (Branch 0) ──
    Vector3 moveDir = moveAction switch {
        1 => transform.forward,
        2 => -transform.forward,
        3 => -transform.right,
        4 => transform.right,
        _ => Vector3.zero
    };
    if (moveDir != Vector3.zero)
        selfController.Move(moveDir);

    // ── 전투 처리 (Branch 1) ──
    Vector3 toTarget = DirectionToTarget();   // 헬퍼 메서드 활용

    switch (combatAction)
    {
        case 1: // Block
            if (selfCooldown.IsBlockReady() && !selfController.IsBusy)
                selfController.Block(toTarget);
            break;

        case 2: // Dodge
            if (selfCooldown.IsDodgeReady() && !selfController.IsBusy)
                selfController.Dodge(-toTarget);  // 적 반대방향으로 회피
            break;

        case 3: // Attack (반격)
            if (selfCooldown.IsAttackReady() && !selfController.IsBusy)
            {
                selfController.Face(toTarget);
                selfController.Attack();
            }
            break;
    }

    // ── 보상 처리 (매 step) ──
    ApplyStepRewards(combatAction);

    // ── 에피소드 종료 체크 ──
    if (episodeManager.IsEpisodeDone())
    {
        if (self.IsDead)
            SetReward(-1f);
        else if (target.IsDead)
            SetReward(1f);
        else
            SetReward(0.3f);  // 시간초과 무승부 — 수비형은 생존도 성공

        EndEpisode();
    }
}
```

---

## 6. 구현 명세 — Reward Function

### 설계 철학
수비형 에이전트의 이상적 행동 패턴: **"Block/Dodge → 반격(Attack)"**
이 순서를 가장 높은 보상으로 유도하고, 맞는 것을 강하게 억제한다.

### 보상 상수 (클래스 상단 선언)
```csharp
// ── Step Rewards ──
private const float RewardSurvivePerStep      =  0.001f;  // 생존 자체 보상 (작게)
private const float RewardBlockSuccess        =  0.4f;    // Block으로 공격 막기
private const float RewardDodgeSuccess        =  0.3f;    // Dodge로 피격 회피
private const float RewardCounterAttack       =  0.5f;    // Block/Dodge 직후 공격 성공
private const float PenaltyHit               = -0.3f;    // 피격
private const float PenaltyCooldownWaste     = -0.05f;   // 쿨타임 중 행동 시도
private const float PenaltyTooClose         = -0.01f;   // 위험 근접 유지 (per step)

// ── Episode End Rewards ──
private const float RewardWin               =  1.0f;
private const float PenaltyLoss             = -1.0f;
private const float RewardSurviveTimeout    =  0.3f;   // 시간초과 생존

// ── 내부 상태 추적 ──
private bool _didBlockLastStep  = false;
private bool _didDodgeLastStep  = false;
private float _safeDistance     = 1.5f;  // 이 거리 이하면 위험 근접
```

### ApplyStepRewards() 구현
```csharp
private void ApplyStepRewards(int combatAction)
{
    // [1] 생존 보상 (매 step)
    AddReward(RewardSurvivePerStep);

    // [2] 위험 근접 페널티
    float dist = Vector3.Distance(transform.position, target.transform.position);
    if (dist < _safeDistance)
        AddReward(PenaltyTooClose);

    // [3] 쿨타임 중 헛동작 페널티
    if (combatAction == 1 && !selfCooldown.IsBlockReady())
        AddReward(PenaltyCooldownWaste);
    if (combatAction == 2 && !selfCooldown.IsDodgeReady())
        AddReward(PenaltyCooldownWaste);
    if (combatAction == 3 && !selfCooldown.IsAttackReady())
        AddReward(PenaltyCooldownWaste);

    // [4] 반격 보상: 이전 step에 Block/Dodge 성공 직후 Attack 명중
    //     → CombatHitResult를 이벤트/콜백으로 수신하거나
    //        OnAttackHitConnected 이벤트가 있다면 연동
    if (combatAction == 3 && (_didBlockLastStep || _didDodgeLastStep))
    {
        // 실제 명중 여부는 OnAttackConnected 이벤트에서 처리 (아래 섹션 참고)
        // 여기서는 반격 시도 자체에 소액 보상 (선택적)
        AddReward(0.1f);
    }

    // [5] 이전 step 상태 갱신
    _didBlockLastStep = selfController.IsBlocking;
    _didDodgeLastStep = selfController.IsInvincible;
}
```

### 이벤트 기반 보상 — OnEpisodeBegin/피격/방어 성공 처리
```csharp
public override void OnEpisodeBegin()
{
    // [에피소드 시작] 상태 초기화
    _didBlockLastStep = false;
    _didDodgeLastStep = false;
    episodeManager.ResetEpisode();
}

// CombatCharacter.TakeDamage()가 호출될 때 구독 (이벤트 방식)
// 또는 OnActionReceived 내에서 HP 변화량으로 감지 (폴링 방식)
private float _prevSelfHP;
private float _prevTargetHP;

// OnActionReceived 시작부에 추가
private void DetectHPChanges()
{
    float selfHPNow   = self.CurrentHealth;
    float targetHPNow = target.CurrentHealth;

    // 내 HP가 줄었으면 피격
    if (selfHPNow < _prevSelfHP)
        AddReward(PenaltyHit);

    // 상대 HP가 줄었고, 이전 step에 Block/Dodge 했으면 반격 보상
    if (targetHPNow < _prevTargetHP && (_didBlockLastStep || _didDodgeLastStep))
        AddReward(RewardCounterAttack);

    // Block 성공: 내 HP는 줄지 않았는데 상대가 IsAttacking이었고 내가 IsBlocking이었던 경우
    // → CombatHitResult.Blocked 이벤트 연동이 가능하면 그 방식을 우선 사용

    _prevSelfHP   = selfHPNow;
    _prevTargetHP = targetHPNow;
}
```

> **팁**: `CombatHitDetector.TryHit()`이 `CombatHitResult.Blocked`를 반환할 때 event/Action을 등록하면
> 더 정확한 Block 성공 감지 가능. Core 코드 수정 없이 가능하다면 이벤트 방식 권장.

---

## 7. YAML 수정 포인트 (`Assets/Config/combat_ppo.yaml`)

수비형 학습에 맞게 기본값에서 아래 항목만 조정.

```yaml
behaviors:
  CombatAgent:
    trainer_type: ppo
    hyperparameters:
      batch_size: 64
      buffer_size: 2048
      learning_rate: 3.0e-4
      beta: 0.01          # 기본 5.0e-3 → 0.01 상향: 초반 탐색 촉진 (Block/Dodge 다양하게 시도)
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      hidden_units: 128
      num_layers: 2
    reward_signals:
      extrinsic:
        gamma: 0.99        # 0.99 유지: 장기 생존 중시
        strength: 1.0
    max_steps: 3000000     # 기본 500000 → 3000000: 수비 패턴은 탐색 시간 더 필요
    time_horizon: 64
    summary_freq: 10000
    checkpoint_interval: 50000
```

---

## 8. 구현 체크리스트

### 필수 구현 (채점 직결)
- [ ] `OnEpisodeBegin()` — `episodeManager.ResetEpisode()` + 상태 변수 초기화
- [ ] `CollectObservations()` — AddObservation 11개, Space Size 합산 검증
- [ ] `OnActionReceived()` Branch 0 이동 — 5방향 Move 처리
- [ ] `OnActionReceived()` Branch 1 전투 — Block/Dodge/Attack 쿨타임 체크 포함
- [ ] 에피소드 종료 보상 — 승/패/무승부 3케이스 처리 + `EndEpisode()`
- [ ] 피격 감지 + 페널티 적용
- [ ] Block 성공 + 보상 적용
- [ ] 반격(Block/Dodge 직후 Attack 명중) 보상 적용

### Unity Inspector 설정 (코드 완성 후)
- [ ] Behavior Parameters > Space Size = `11`
- [ ] Behavior Parameters > Discrete Branch 0 크기 = `5`
- [ ] Behavior Parameters > Discrete Branch 1 크기 = `4`
- [ ] Behavior Parameters > Behavior Name = `CombatAgent`
- [ ] 학습 상대로 `BaselineAttackerBT` Agent가 Arena에 배치되어 있는지 확인

### 학습 실행
```bash
# ML-Agents 학습 시작
mlagents-learn Assets/Config/combat_ppo.yaml --run-id=defender_v1

# 결과 확인 위치
# result/defender_v1/CombatAgent.onnx → Unity에 import 후 Behavior Parameters > Model에 연결
# Behavior Type → Inference Only 로 변경 후 실행
```

---

## 9. 발표용 분석 포인트 (구현 중 기록해둘 것)

### RL 설계 파트
- Observation 11개 각각의 **선택 이유** 1줄씩 메모
- Reward 값(`+0.4`, `-0.3` 등) **수치 근거** 기록 (BT 조건 함수와의 연결)
- `max_steps` 및 `beta` 수정 이유 기록

### 학습 과정 관찰 포인트
- 초반 (~300k step): Block/Dodge 남발 → 쿨타임 패널티로 수렴 과정
- 중반 (~1M step): Block 성공률 상승 여부
- 후반 (~2M+ step): "Block 직후 반격" 패턴이 자연 발생하는지 (핵심 관찰)
- **실패 사례**: step reward가 너무 크면 무조건 도망만 → 값 조정 실험 결과 기록

### BT vs RL 비교 준비
- 동일 baseline(공격형)과 각 50게임 이상 대전
- CSV 수집 항목: **승률 / 평균 생존 시간 / Block 사용 횟수 / Dodge 사용 횟수 / 반격 성공 횟수**
- BT는 명시적 `ShouldDodge(HP ≤ 30%)` 규칙이 있지만, RL은 이 임계값을 **스스로 발견하는지** 관찰

---

## 10. 주의사항 & 자주 나오는 오류

| 상황 | 원인 | 해결 |
|---|---|---|
| `UnityAgentsException: mismatch` | Space Size와 AddObservation 수 불일치 | CollectObservations의 Add 횟수 재계산 후 Inspector 동기화 |
| Agent가 아무 행동도 안 함 | Branch 크기 설정 누락 | Inspector에서 Branch 0=5, Branch 1=4 확인 |
| 학습 중 reward가 계속 음수 | 피격 패널티가 너무 강하거나 step reward 너무 작음 | PenaltyHit을 -0.1로 낮추거나 RewardSurvivePerStep을 0.002로 상향 |
| Dodge가 제자리에서만 발생 | Dodge Impulse = 0으로 설정됨 | Inspector의 CombatActionController > Dodge Impulse 값 확인 (기본 2f) |
| Block이 막히지 않음 | Block(Vector3) 미사용, 방향 미전달 | `selfController.Block(toTarget)` — 반드시 방향 인자 전달 |
