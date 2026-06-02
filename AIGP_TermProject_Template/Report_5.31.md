# 방어형 RL Agent 설계 및 학습 경과 보고서

**작성일**: 2026-05-31  
**대상 파일**: `Assets/Scripts/ML/StudentCombatAgent.cs`  
**학습 ID**: `defender_v1`  
**현재 진행 step**: 20,000 / 3,000,000

---

## 1. 작업 개요

이 보고서는 ML-Agents PPO 기반의 **방어형 전투 RL Agent**(`StudentCombatAgent`)를 설계하고 Unity 환경에 배치하여 학습을 시작한 과정을 정리한다.

학습 상대는 `BaselineAttackerBT`(공격형 행동 트리)이며, RL Agent는 이에 맞서 **Block → Dodge → 반격 → 생존** 전략을 스스로 발견하도록 설계되었다.

---

## 2. 프로젝트 구조

```
Assets/Scripts/
├── BT/
│   ├── BTNode.cs                  # BT 기반 클래스
│   ├── BaselineAttackerBT.cs      # 공격형 BT (학습 상대)
│   └── BaselineDefenderBT.cs      # 수비형 BT (전략 참조용)
├── Core/
│   ├── CombatActionController.cs  # 행동 API (Move/Attack/Block/Dodge)
│   ├── CombatCharacter.cs         # HP 관리
│   ├── CooldownSystem.cs          # 쿨타임 조회
│   ├── CombatHitDetector.cs       # 피격 판정
│   └── EpisodeManager.cs          # 에피소드 리셋/종료
└── ML/
    └── StudentCombatAgent.cs      # RL Agent (수정 대상)
```

---

## 3. BT 전략 요약 (설계 참조 기반)

조원이 설계한 `Defense_strategy_RL.txt`의 **Outranging Phase Defender** 전략을 RL 설계의 철학적 기반으로 활용하였다.

### 거리 구역 개념

| 구역 | 거리 | 의미 |
|---|---|---|
| Kill Zone | 1.4 ~ 1.7f | 자신만 일방적으로 공격 가능한 비대칭 구역 |
| Enemy Zone | < 1.2f | 양쪽 모두 공격 가능한 위험 구역 |
| Safe Zone | > 1.8f | 전투 휴지, 거리 재정렬 구역 |

### HP 구간별 행동 (Phase)

| Phase | HP | 행동 기조 |
|---|---|---|
| Phase 1 | ≥ 60% | 적극 압박, Poke & Retreat, Block → 반격 |
| Phase 2 | 30~60% | 보수적 운영, Block/Dodge 랜덤, 확률적 반격 |
| Phase 3 | < 30% | 생존 우선, Block → 이탈 (상대 저체력 시 예외) |

> **RL 설계 방침**: BT처럼 Phase를 명시적으로 구현하지 않고, Observation에 HP 비율을 포함시켜 PPO가 Phase별 패턴을 **스스로 발견**하도록 유도하였다.

---

## 4. RL Agent 설계

### 4-1. Observation 설계 (Space Size = 11)

| # | 항목 | 값 범위 | 선택 이유 |
|---|---|---|---|
| 1 | 자신 HP 비율 | 0~1 | 긴급 회피 판단 기준, Phase 발견 유도 |
| 2 | 상대 HP 비율 | 0~1 | 반격/마무리 타이밍 판단 |
| 3 | 상대까지 거리 (정규화) | 0~1 | Kill Zone / Enemy Zone 구분 |
| 4 | 상대 방향 벡터 x | -1~1 | 방어 유효 각도 계산 |
| 5 | 상대 방향 벡터 z | -1~1 | 방어 유효 각도 계산 |
| 6 | Block 쿨타임 진행률 | 0~1 | Block 가능 여부 판단 |
| 7 | Dodge 쿨타임 진행률 | 0~1 | Dodge 가능 여부 판단 |
| 8 | Attack 쿨타임 진행률 | 0~1 | 반격 타이밍 포착 |
| 9 | 상대 공격 모션 중 여부 | 0 or 1 | 즉각 방어 트리거 |
| 10 | 자신 Blocking 중 여부 | 0 or 1 | 중복 Block 방지 |
| 11 | 자신 Invincible(Dodge) 중 여부 | 0 or 1 | 중복 Dodge 방지 |

### 4-2. Action 설계

**Branch 0 — 이동 (5가지)**

| 값 | 행동 |
|---|---|
| 0 | 정지 |
| 1 | 앞으로 |
| 2 | 뒤로 |
| 3 | 왼쪽 |
| 4 | 오른쪽 |

**Branch 1 — 전투 (4가지)**

| 값 | 행동 |
|---|---|
| 0 | 없음 |
| 1 | Block (적 방향으로) |
| 2 | Dodge (적 반대 방향으로) |
| 3 | Attack (방향 정렬 후) |

### 4-3. Reward 설계

**Step Reward (매 스텝 누적)**

| 항목 | 값 | 목적 |
|---|---|---|
| 생존 보상 | +0.001 | 오래 살아남기 유도 |
| 위험 근접 페널티 (< 1.5f) | -0.01 | Kill Zone 거리 유지 유도 |
| 쿨타임 중 헛동작 페널티 | -0.05 | 효율적 행동 선택 유도 |
| 반격 시도 소액 보상 (Block/Dodge 직후 Attack) | +0.1 | 반격 패턴 초기 탐색 촉진 |

**이벤트 Reward (상태 변화 감지)**

| 항목 | 값 | 감지 방법 |
|---|---|---|
| 피격 페널티 | -0.3 | HP 감소 폴링 |
| Block 성공 보상 | +0.4 | 상대 공격 모션 종료 + 내 HP 미감소 + Blocking 상태였음 |
| Dodge 성공 보상 | +0.3 | 상대 공격 모션 종료 + 내 HP 미감소 + Invincible 상태였음 |
| 반격 성공 보상 | +0.5 | 상대 HP 감소 + 이전 step에 Block/Dodge 성공 |

**에피소드 종료 Reward**

| 결과 | 값 |
|---|---|
| 승리 (상대 사망) | +1.0 |
| 패배 (자신 사망) | -1.0 |
| 무승부 (타임아웃) | +0.3 (생존 자체 성공으로 간주) |

### 4-4. 내부 상태 추적 변수

Block/Dodge 성공을 에피소드 내에서 정확히 감지하기 위해 단발 평가 로직을 구현하였다.

```
_prevOpponentWasAttacking  : 이전 step에서 상대가 공격 중이었는지
_wasBlockingDuringAttack   : 상대 공격 중 Block 상태였는지 누적
_wasDodgingDuringAttack    : 상대 공격 중 Invincible 상태였는지 누적
_tookHitDuringAttack       : 상대 공격 중 피격이 있었는지 누적
→ 상대 공격 모션 종료 시점에 단발 평가 후 플래그 초기화
```

---

## 5. Unity Editor 설정

### Agent A (RL 수비형)

| 항목 | 설정 |
|---|---|
| 오브젝트 | `Agent_A_TEST` (AgentA 역할) |
| 활성 컴포넌트 | StudentCombatAgent, BehaviorParameters, DecisionRequester |
| 비활성 컴포넌트 | BaselineAttackerBT, BaselineDefenderBT |
| Behavior Name | `CombatAgent` |
| Behavior Type | `Default` |
| Vector Observation Space Size | `11` |
| Discrete Branch 0 | `5` |
| Discrete Branch 1 | `4` |
| Decision Period | `5` |
| Take Actions Between Decisions | `true` |

### Agent B (BT 공격형 상대)

| 항목 | 설정 |
|---|---|
| 활성 컴포넌트 | BaselineAttackerBT |
| 비활성 컴포넌트 | StudentCombatAgent, BehaviorParameters, DecisionRequester |

### GameManager

| 항목 | 설정 |
|---|---|
| EpisodeManager.agentA | Agent_A_TEST의 CombatCharacter |
| EpisodeManager.agentB | AgentB의 CombatCharacter |
| SpawnPointA, SpawnPointB | 배치 완료 |

---

## 6. 학습 설정 (combat_ppo.yaml)

| 항목 | 값 | 기본값 대비 변경 이유 |
|---|---|---|
| beta | 0.01 | 5.0e-3 → 상향: 초반 Block/Dodge 탐색 촉진 |
| max_steps | 3,000,000 | 500,000 → 상향: 수비 패턴은 탐색 시간 더 필요 |
| gamma | 0.99 | 유지: 장기 생존 중시 |
| batch_size | 64 | 유지 |
| buffer_size | 2,048 | 유지 |
| learning_rate | 3.0e-4 | 유지 |
| hidden_units | 128 | 유지 |
| num_layers | 2 | 유지 |

---

## 7. 학습 경과 및 경향 분석

### 수집된 학습 로그

| Step | Time Elapsed | Mean Reward | Std of Reward | 비고 |
|---|---|---|---|---|
| 10,000 | 84.6 s | -11.928 | 12.804 | 최초 관측 (이전 1,524 step에서 resume) |
| 20,000 | 59.5 s | -7.040 | 11.220 | 41% 개선, Std 감소 시작 |

### 경향 해석

**Mean Reward: -11.928 → -7.040 (약 41% 개선)**

초반 10k step 만에 큰 폭의 개선이 관찰되었다. 이는 PPO가 명백히 불리한 행동(헛동작, 근접 유지)을 빠르게 억제하고 있음을 시사한다.

**Std of Reward: 12.804 → 11.220 (감소)**

에피소드 결과의 분산이 줄어들기 시작했다. 랜덤 정책에서 벗어나 일관된 패턴으로 수렴 중이라는 신호이다.

**Mean Reward가 음수인 이유**

에피소드당 누적 페널티 구조 분석:
- `PenaltyTooClose(-0.01)` × 수백 step ≈ -3 ~ -5
- `PenaltyHit(-0.3)` × 다수 피격 ≈ -3 ~ -6
- 쿨타임 중 헛동작 ≈ -0.5 ~ -1
- 에피소드 종료 패배(-1.0) 포함

→ 현재 단계에서 에피소드당 -7 ~ -12는 **정상 범위**이다.

### 단계별 예상 경향

| Step 구간 | 예상 Mean Reward | 관찰 포인트 |
|---|---|---|
| ~100k | -5 ~ -2 | Block/Dodge 남발 감소, 쿨타임 페널티 수렴 |
| ~500k | -2 ~ 0 | 안전 거리 유지 학습, 피격 감소 |
| ~1M | 0 ~ +0.5 | Block 성공률 상승, 생존 시간 증가 |
| ~2M+ | +0.5 이상 | "Block/Dodge → 즉각 반격" 패턴 자연 발생 여부 (핵심 관찰) |

---

## 8. 발표용 BT vs RL 비교 설계 (예정)

| 비교 항목 | BT (BaselineDefenderBT) | RL (StudentCombatAgent) |
|---|---|---|
| Phase 전환 | HP 임계값 명시 (60%, 30%) | 스스로 발견 여부 관찰 |
| Block vs Dodge 선택 | Phase 2에서 랜덤, 나머지는 결정론 | 확률적 정책으로 학습 |
| Kill Zone 유지 | 명시적 거리 조건 코드화 | PenaltyTooClose로 암묵적 유도 |
| 반격 타이밍 | 조건문으로 명시 | Observation으로 자율 학습 |

**수집 예정 비교 지표 (각 50게임 이상)**

- 승률 / 평균 생존 시간 / Block 사용 횟수 / Dodge 사용 횟수 / 반격 성공 횟수

---

## 9. 다음 단계

1. **학습 계속 진행** — 100k step 단위로 Mean Reward 추이 기록
2. **TensorBoard 모니터링** — `tensorboard --logdir results`
3. **50k step 체크포인트** — `results/defender_v1/` 자동 저장 확인
4. **학습 완료 후** — `.onnx` 파일을 Unity에 import, Behavior Type을 `Inference Only`로 변경하여 실전 테스트
5. **BT vs RL 대전** — 동일 상대로 50게임 이상 실시, 비교 CSV 수집
