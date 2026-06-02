using UnityEngine;

// Student template: replace BuildTree() with an attacker or defender BT strategy.
// NOTE: target must be assigned in Inspector.
public class Defense_StudentBTStrategy : MonoBehaviour
{
    [SerializeField] private CombatCharacter self;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    [Header("HP Thresholds")]
    [SerializeField] private float highHpThreshold = 0.6f;
    [SerializeField] private float lowHpThreshold = 0.3f;
    [SerializeField] private float enemyLowHpThreshold = 0.3f;

    [Header("Distance Thresholds")]
    [SerializeField] private float killZoneInner = 1.8f;
    [SerializeField] private float killZoneOuter = 2.0f;
    [SerializeField] private float enemyAggroRange = 1.8f;

    [Header("Combat Parameters")]
    [SerializeField] private float counterChance = 0.5f;
    [SerializeField] private float facingAngle = 45f;
    [SerializeField] private float maintainMoveSpeed = 3f;
    [SerializeField] private float pokeRetreatDodgeImpulse = 4f;
    [SerializeField] private float counterDecisionCooldown = 0.8f;
    [SerializeField] private float defenseDecisionCooldown = 0.8f;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;
    private string debugAction = "—";
    [SerializeField] private float escapeDodgeImpulse = 2f;

    private BTNode root;
    private float _counterTimer;
    private float _defenseTimer;

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

        if (_counterTimer > 0f) _counterTimer -= Time.deltaTime;
        if (_defenseTimer > 0f) _defenseTimer -= Time.deltaTime;

        debugAction = "—";
        root.Tick();
    }

    private void BuildTree()
    {
        // ── Phase 3 body: Finisher decorator nests Standard kill zone when enemy HP is low ──
        BTNode finisherKillZone = new DecoratorNode(
            new SequenceNode(
                new ConditionNode(() => target.CurrentHealthRatio <= enemyLowHpThreshold),
                new SequenceNode(new ConditionNode(InKillZone), BuildStandardKillZoneAction())
            ),
            status => status
        );

        BTNode phase3KillZone = new SelectorNode(
            finisherKillZone,
            new SequenceNode(new ConditionNode(InKillZone), BuildRestrictedKillZoneAction())
        );

        BTNode phase3Body = new SelectorNode(
            BuildPhase3Defense(),
            new SequenceNode(new ConditionNode(InEnemyZone), BuildEnemyZoneEscapeWithDodge()),
            phase3KillZone,
            new ActionNode(DoApproachKillZone)
        );

        // ── HP-gated phase decorators ────────────────────────────────────────
        // Each DecoratorNode wraps a SequenceNode whose first child is the HP gate.
        // The gate ConditionNode short-circuits the branch so no actions fire
        // in the wrong phase.

        BTNode phase1Gate = new DecoratorNode(
            new SequenceNode(
                new ConditionNode(() => self.CurrentHealthRatio >= highHpThreshold),
                BuildPhase1Branch()
            ),
            status => status
        );

        BTNode phase2Gate = new DecoratorNode(
            new SequenceNode(
                new ConditionNode(() => self.CurrentHealthRatio >= lowHpThreshold),
                BuildPhase2Branch()
            ),
            status => status
        );

        BTNode phase3Gate = new DecoratorNode(
            new SequenceNode(
                new ConditionNode(() => self.CurrentHealthRatio < lowHpThreshold),
                phase3Body
            ),
            status => status
        );

        root = new SelectorNode(phase1Gate, phase2Gate, phase3Gate);
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

    private Vector3 DirectionToTarget()
    {
        if (target == null)
        {
            return transform.forward;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= 0.0001f ? transform.forward : offset.normalized;
    }

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

    private bool IsFacingTarget(float maxAngle)
    {
        Vector3 direction = DirectionToTarget();
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, direction) <= maxAngle;
    }

    private bool InKillZone()
    {
        float d = DistanceToTarget();
        return d >= killZoneInner && d <= killZoneOuter;
    }

    private bool InEnemyZone()
    {
        return DistanceToTarget() < killZoneInner;
    }

    private bool InSafeZone()
    {
        return DistanceToTarget() > killZoneOuter;
    }

    private bool InSelfAttackRange()
    {
        return DistanceToTarget() <= killZoneOuter;
    }

    private void DoBlockTowardEnemy()
    {
        debugAction = "Block";
        actionController.Block(DirectionToTarget());
    }

    private void DoFaceAndAttack()
    {
        debugAction = "Attack";
        actionController.Face(DirectionToTarget());
        actionController.Attack();
    }

    private void DoDodgeBack()
    {
        debugAction = "Dodge (escape)";
        actionController.DodgeImpulse = escapeDodgeImpulse;
        actionController.Face(DirectionToTarget());
        actionController.Dodge(-DirectionToTarget());
    }

    private void DoDodgeBackFar()
    {
        debugAction = "Dodge (retreat)";
        actionController.DodgeImpulse = pokeRetreatDodgeImpulse;
        actionController.Face(DirectionToTarget());
        actionController.Dodge(-DirectionToTarget());
    }

    private void DoMoveBack()
    {
        debugAction = "Move Back";
        actionController.Move(-DirectionToTarget());
        actionController.Face(DirectionToTarget());
    }

    private void DoMaintainKillZone()
    {
        float d = DistanceToTarget();
        if (d < killZoneInner)
        {
            debugAction = "Maintain (back)";
            actionController.MoveSpeed = maintainMoveSpeed;
            actionController.Move(-DirectionToTarget());
            actionController.Face(DirectionToTarget());
        }
        else if (d > killZoneOuter)
        {
            debugAction = "Maintain (fwd)";
            actionController.MoveSpeed = maintainMoveSpeed;
            actionController.Move(DirectionToTarget());
        }
        else
        {
            debugAction = "Hold";
            actionController.Face(DirectionToTarget());
        }
    }

    private void DoFaceTarget()
    {
        debugAction = "Face";
        actionController.Face(DirectionToTarget());
    }

    private void DoApproachKillZone()
    {
        debugAction = "Approach";
        actionController.Move(DirectionToTarget());
    }

    // ── Shared kill-zone sub-trees ───────────────────────────────────────────

    private BTNode BuildStandardKillZoneAction()
    {
        return new SelectorNode(
            new SequenceNode(
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsAttackReady()),
                new ConditionNode(() => IsFacingTarget(facingAngle)),
                new ActionNode(DoFaceAndAttack),
                new ActionNode(DoDodgeBackFar)
            ),
            new ParallelNode(1, 2,
                new ActionNode(DoMaintainKillZone),
                new ActionNode(DoFaceTarget)
            )
        );
    }

    private BTNode BuildRestrictedKillZoneAction()
    {
        return new SelectorNode(
            new SequenceNode(
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsAttackReady()),
                new ConditionNode(() => target.CooldownSystem != null && !target.CooldownSystem.IsAttackReady()),
                new ConditionNode(() => IsFacingTarget(facingAngle)),
                new ActionNode(DoFaceAndAttack),
                new ActionNode(DoDodgeBackFar)
            ),
            new ParallelNode(1, 2,
                new ActionNode(DoMaintainKillZone),
                new ActionNode(DoFaceTarget)
            )
        );
    }

    // ── Enemy-zone escape variants ───────────────────────────────────────────

    private BTNode BuildEnemyZoneEscapeNoDodge()
    {
        return new ActionNode(DoMoveBack);
    }

    private BTNode BuildEnemyZoneEscapeWithDodge()
    {
        return new SelectorNode(
            new SequenceNode(
                new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                new ActionNode(DoDodgeBack)
            ),
            new ActionNode(DoMoveBack)
        );
    }

    // ── Phase branch builders (no HP gate — applied in BuildTree) ────────────

    private BTNode BuildPhase1Branch()
    {
        // Defense: block first; when block is on cooldown, counter if in range + ready + facing.
        BTNode defenseResponse = new SequenceNode(
            new ConditionNode(() => target.ActionController != null && target.ActionController.IsAttacking),
            new SelectorNode(
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsBlockReady()),
                    new ActionNode(DoBlockTowardEnemy)
                ),
                new SequenceNode(
                    new ConditionNode(InSelfAttackRange),
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsAttackReady()),
                    new ConditionNode(() => IsFacingTarget(facingAngle)),
                    new ActionNode(DoFaceAndAttack)
                )
            )
        );

        return new SelectorNode(
            defenseResponse,
            new SequenceNode(new ConditionNode(InEnemyZone), BuildEnemyZoneEscapeNoDodge()),
            new SequenceNode(new ConditionNode(InKillZone),  BuildStandardKillZoneAction()),
            new ActionNode(DoApproachKillZone)
        );
    }

    private BTNode BuildPhase2Branch()
    {
        // Defense: timer-gated so only one defense action fires per defenseDecisionCooldown window.
        // When both block and dodge are ready, RandomSelector picks non-deterministically.
        BTNode defenseChoice = new SequenceNode(
            new ConditionNode(() => _defenseTimer <= 0f),
            new ActionNode(() =>
            {
                _defenseTimer = defenseDecisionCooldown;
                return BTNodeStatus.Success;
            }),
            new SelectorNode(
                // Both ready: random pick
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsBlockReady()),
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                    new RandomSelectorNode(
                        new ActionNode(() =>
                        {
                            Debug.Log("[random] defense: Block selected (random, both ready)");
                            DoBlockTowardEnemy();
                            return BTNodeStatus.Success;
                        }),
                        new ActionNode(() =>
                        {
                            Debug.Log("[random] defense: Dodge selected (random, both ready)");
                            DoDodgeBack();
                            return BTNodeStatus.Success;
                        })
                    )
                ),
                // Only block available
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsBlockReady()),
                    new ActionNode(() =>
                    {
                        Debug.Log("[random] defense: Block selected (dodge on cooldown)");
                        DoBlockTowardEnemy();
                        return BTNodeStatus.Success;
                    })
                ),
                // Only dodge available
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                    new ActionNode(() =>
                    {
                        Debug.Log("[random] defense: Dodge selected (block on cooldown)");
                        DoDodgeBack();
                        return BTNodeStatus.Success;
                    })
                )
            )
        );

        BTNode counterDecision = new SequenceNode(
            new ConditionNode(() => _counterTimer <= 0f),
            new ActionNode(() =>
            {
                _counterTimer = counterDecisionCooldown;
                return BTNodeStatus.Success;
            }),
            new RandomSelectorNode(
                new SequenceNode(
                    new ConditionNode(InSelfAttackRange),
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsAttackReady()),
                    new ConditionNode(() => IsFacingTarget(facingAngle)),
                    new ActionNode(() =>
                    {
                        Debug.Log("[random] counter: Attack selected");
                        DoFaceAndAttack();
                        return BTNodeStatus.Success;
                    })
                ),
                new ActionNode(() =>
                {
                    Debug.Log("[random] counter: No-op selected");
                    return BTNodeStatus.Success;
                })
            )
        );

        BTNode defenseResponse = new SequenceNode(
            new ConditionNode(() => target.ActionController != null && target.ActionController.IsAttacking),
            defenseChoice,
            counterDecision
        );

        return new SelectorNode(
            defenseResponse,
            new SequenceNode(new ConditionNode(InEnemyZone), BuildEnemyZoneEscapeWithDodge()),
            new SequenceNode(new ConditionNode(InKillZone),  BuildStandardKillZoneAction()),
            new ActionNode(DoApproachKillZone)
        );
    }

    private BTNode BuildPhase3Defense()
    {
        return new SequenceNode(
            new ConditionNode(() => target.ActionController != null && target.ActionController.IsAttacking),
            new SelectorNode(
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsBlockReady()),
                    new ActionNode(DoBlockTowardEnemy)
                ),
                new SequenceNode(
                    new ConditionNode(() => cooldownSystem != null && cooldownSystem.IsDodgeReady()),
                    new ActionNode(DoDodgeBack)
                )
            )
        );
    }

    private BTNode BuildPhase3Branch()
    {
        return new SelectorNode(
            BuildPhase3Defense(),
            new SequenceNode(new ConditionNode(InEnemyZone), BuildEnemyZoneEscapeWithDodge()),
            new SequenceNode(new ConditionNode(InKillZone),  BuildRestrictedKillZoneAction()),
            new ActionNode(DoApproachKillZone)
        );
    }

    // ── Debug visualization (Scene view only) ───────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebug)
        {
            return;
        }

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, killZoneInner);
        Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, killZoneOuter);

        if (!Application.isPlaying || self == null || target == null)
        {
            return;
        }

        float selfHp  = self.CurrentHealthRatio;
        float enemyHp = target.CurrentHealthRatio;
        float dist    = DistanceToTarget();

        string phase;
        if (selfHp < lowHpThreshold)
        {
            phase = enemyHp <= enemyLowHpThreshold ? "Phase 3 +Finisher" : "Phase 3";
        }
        else if (selfHp < highHpThreshold)
        {
            phase = "Phase 2";
        }
        else
        {
            phase = "Phase 1";
        }

        string zone = InKillZone() ? "KILL ZONE" : InEnemyZone() ? "ENEMY ZONE" : "SAFE ZONE";

        string label = $"[StudentBT]\n"
                     + $"Phase : {phase}\n"
                     + $"Action: {debugAction}\n"
                     + $"HP    : {selfHp:P0}  Enemy: {enemyHp:P0}\n"
                     + $"Dist  : {dist:F2}  [{zone}]";

        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f, label);
    }
#endif

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
