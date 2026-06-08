# 방어형 RL Agent 설계 및 학습 경과 보고서

**최초 작성일**: 2026-05-31  
**업데이트**: 2026-06-02 (1M Steps 분석 추가)  
**대상 파일**: `Assets/Scripts/ML/StudentCombatAgent.cs`  
**학습 ID**: `defender_v1`  
**현재 진행 step**: 1,000,000 / 3,000,000

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

## 7. 학습 경과 — 전체 로그 (20k ~ 1M Steps)

| Step | Mean Reward | Std | 비고 |
|---|---|---|---|
| 10,000 | -11.928 | 12.804 | 최초 관측 |
| 20,000 | -7.040 | 11.220 | 41% 개선 |
| 30,000 | -0.709 | 6.181 | 급속 상승 |
| 40,000 | +4.317 | 2.271 | **양수 돌파** |
| 50,000 | +0.963 | 7.219 | 일시 후퇴 |
| 70,000 | +5.386 | 0.391 | 안정화 |
| 100,000 | +5.792 | 0.244 | 고원 진입 |
| 150,000 | +5.952 | 0.365 | 고원 유지 |
| 200,000 | +6.003 | 0.211 | 고원 유지 |
| 300,000 | +5.986 | 0.267 | 고원 유지 |
| 330,000 | +7.158 | 3.051 | **전략 전환 시작** |
| 350,000 | +8.427 | 3.581 | 상승 가속 |
| 380,000 | +11.397 | 2.651 | 반격 패턴 발현 |
| 420,000 | +14.891 | 2.108 | 큰 폭 상승 |
| 500,000 | +13.668 | 2.833 | 안정적 상승 |
| 570,000 | +15.705 | 2.895 | 지속 성장 |
| 600,000 | +19.626 | 8.940 | 일시 피크 |
| 700,000 | +15.658 | 2.367 | 변동성 증가 |
| 730,000 | +34.779 | 21.417 | 고위험 전략 탐색 |
| 800,000 | +20.284 | 17.668 | 불안정 구간 |
| 820,000 | **-8.032** | 27.576 | **Policy Collapse** |
| 830,000 | -8.135 | 20.584 | 최저점 |
| 870,000 | +29.629 | 29.125 | 급격한 회복 |
| 910,000 | +62.526 | 30.082 | 고위험 고보상 전략 |
| 950,000 | +68.027 | 36.762 | 최고점 |
| 1,000,000 | +34.842 | 41.517 | 높은 분산 유지 |

---

## 8. 학습 경과 분석 — 5단계 서사

### 1단계 — 급속 초기 학습 (0 ~ 50k)

10k만에 Reward가 -7 → -0.7, 40k에 이미 양수 돌파. PPO가 가장 명백히 불리한 행동(쿨타임 중 헛동작, 적 바로 앞에 서있기)을 빠르게 억제한 결과다. 초기 학습 속도가 예상보다 빠르게 나타났다.

### 2단계 — 보수적 고원 (100k ~ 300k)

Mean Reward ≈ 6.0, Std ≈ 0.25로 극도로 안정적인 수렴. **에이전트가 "살아남기"는 터득했지만 적극적 반격을 아직 발견하지 못한 상태**다. 타임아웃 생존 보상(+0.3)과 생존 누적 보상(+0.001/step)을 일관되게 확보하는 소극적 전략에 정착한 것으로 추정된다. Std가 0.2~0.3으로 매우 낮아 거의 매 에피소드가 동일한 결과를 내고 있다.

### 3단계 — 전략 전환 및 반격 패턴 발현 (300k ~ 600k)

330k를 기점으로 Reward가 흔들리기 시작하고 Std가 증가한다. 이는 PPO가 기존 안전 전략에서 벗어나 새로운 행동을 탐색하고 있다는 신호다. 350k~600k에서 Reward가 8 → 15 → 19로 꾸준히 상승하며, **Block 직후 즉각 반격 패턴이 발현**되기 시작한 것으로 분석된다. 반격 성공 보상(+0.5)이 누적되면서 생존 중심 전략을 대체하기 시작했다.

### 4단계 — Policy Collapse (800k ~ 850k)

800k에서 20.284이던 Reward가 820k에서 갑자기 -8.032로 추락, Std도 27→34로 폭발. 이는 PPO의 고질적 문제인 **정책 붕괴(Policy Collapse)** 다. 고보상 전략을 과도하게 강화하다가 정책 업데이트 폭이 epsilon 한계를 벗어나 불안정해진 것이다. 학습률(3.0e-4)이나 epsilon(0.2)을 더 보수적으로 설정했다면 예방 가능했을 수 있다.

### 5단계 — 회복 및 고위험 전략 (850k ~ 1M)

붕괴 직후 870k부터 급반등하여 910k에 62.526, 950k에 68.027 달성. 그러나 **Std가 30~42로 극도로 높다.** 이는 에이전트가 매우 공격적인 반격 전략을 취하되 일관성이 없음을 의미한다. 어떤 에피소드는 고보상(100+), 어떤 에피소드는 대패(-20~-30)하는 극단적 분산이 나타나고 있다.

---

## 9. BT 전략과의 비교 — 설계 의도 vs 실제 학습

| 항목 | BT 설계 의도 | RL이 실제 학습한 것 |
|---|---|---|
| 100k~300k | Kill Zone 거리 유지 + Poke & Retreat | **타임아웃 생존 루프** (소극적 정착) |
| 300k~600k | Phase별 Block/Dodge 전환 | **Block → 즉각 반격** 패턴 자연 발현 |
| 820k 붕괴 | — | 과도한 반격 편향으로 정책 불안정 |
| 900k+ | HP 구간별 전략 전환 | **고위험 반격 일변도** (Std 극대) |

**핵심 발견**: RL은 BT가 설계한 "거리 유지 → 안전 공격" 전략보다 **"Block → 즉각 반격" 루프**를 더 강하게 학습했다. 이는 보상 구조상 반격 성공(+0.5)이 생존 누적(+0.001/step)보다 훨씬 크기 때문으로, 설계 단계에서 예견한 **보상 충돌 문제가 실제로 발현**된 것이다.

이 차이는 BT의 명시적 규칙 설계와 RL의 보상 기반 학습 간의 본질적 차이를 보여주는 좋은 발표 논점이 된다.

---

## 10. 발표용 BT vs RL 비교 설계

| 비교 항목 | BT (BaselineDefenderBT) | RL (StudentCombatAgent) |
|---|---|---|
| Phase 전환 | HP 임계값 명시 (60%, 30%) | 스스로 발견 여부 관찰 |
| Block vs Dodge 선택 | Phase 2에서 랜덤, 나머지는 결정론 | 확률적 정책으로 학습 |
| Kill Zone 유지 | 명시적 거리 조건 코드화 | PenaltyTooClose로 암묵적 유도 |
| 반격 타이밍 | 조건문으로 명시 | Observation으로 자율 학습 |
| 전략 일관성 | 높음 (결정론적) | 낮음 (Std 크고 Policy Collapse 발생) |
| 보상 편향 | 설계대로 거리 유지 중심 | Block→반격 루프로 수렴 |

**수집 예정 비교 지표 (각 50게임 이상)**

- 승률 / 평균 생존 시간 / Block 사용 횟수 / Dodge 사용 횟수 / 반격 성공 횟수

---

## 11. 다음 단계

1. **1M 체크포인트 Inference 테스트** — `CombatAgent-999986.onnx`를 Unity에 import, Behavior Type → `Inference Only`로 변경 후 실제 행동 패턴 육안 확인
2. **3M까지 학습 계속** 또는 **Std 안정화를 위해 epsilon 0.2 → 0.1 하향** 고려
3. **BT vs RL 대전** — 현재 1M 모델로 50게임 승률 측정 가능
4. **보고서 추가 업데이트** — 3M step 완료 후 최종 분석 및 BT 비교 결과 반영
