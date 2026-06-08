using UnityEngine;

public enum CombatHitResult
{
    NoTarget,
    OutOfRange,
    OutsideAngle,
    Invincible,
    Blocked,
    Hit
}

// Resolves one attack attempt from the owner against its assigned target.
public class CombatHitDetector : MonoBehaviour
{
    [SerializeField] private CombatCharacter owner;
    [SerializeField] private CombatCharacter target;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackDamage = 20f;
    [SerializeField] private float attackAngle = 90f;
    [SerializeField] private bool drawAttackRays = true;

    private void Awake()
    {
        FillDefaultReferences();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public CombatHitResult TryHit()
    {
        bool suppressCombatDebug = ShouldSuppressCombatDebug();

        if (target == null)
        {
            if (drawAttackRays && !suppressCombatDebug)
            {
                DrawAttackDebugRays(Vector3.zero, false);
            }

            return CombatHitResult.NoTarget;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        float distance = toTarget.magnitude;

        if (drawAttackRays && !suppressCombatDebug)
        {
            DrawAttackDebugRays(toTarget, true);
        }

        if (distance > attackRange)
        {
            if (!suppressCombatDebug)
            {
                Debug.Log($"{name} attack out of range. Target: {target.name}");
            }

            return CombatHitResult.OutOfRange;
        }

        if (!IsInsideAttackAngle(toTarget))
        {
            if (!suppressCombatDebug)
            {
                Debug.Log($"{name} attack missed outside angle. Target: {target.name}");
            }

            return CombatHitResult.OutsideAngle;
        }

        CombatActionController targetAction = target.ActionController;
        if (targetAction != null && targetAction.IsInvincible)
        {
            if (!suppressCombatDebug)
            {
                Debug.Log($"{name} attack dodged by {target.name}.");
            }

            return CombatHitResult.Invincible;
        }

        if (targetAction != null && targetAction.IsBlocking)
        {
            if (!suppressCombatDebug)
            {
                Debug.Log($"{name} attack blocked by {target.name}.");
            }

            return CombatHitResult.Blocked;
        }

        target.TakeDamage(attackDamage);
        if (!suppressCombatDebug)
        {
            Debug.Log($"{name} hit {target.name} for {attackDamage} damage. HP: {target.CurrentHealth}/{target.MaxHealth}");
        }

        return CombatHitResult.Hit;
    }

    private bool IsInsideAttackAngle(Vector3 toTarget)
    {
        if (attackAngle >= 360f)
        {
            return true;
        }

        Vector3 flatToTarget = toTarget;
        flatToTarget.y = 0f;

        if (flatToTarget.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;

        return Vector3.Angle(flatForward, flatToTarget) <= attackAngle * 0.5f;
    }

    private void DrawAttackDebugRays(Vector3 toTarget, bool hasTarget)
    {
        const float duration = 1.5f;
        const int arcSegments = 12;

        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();

        // Blue: current attack forward. Red: direction from attacker to target.
        Debug.DrawRay(origin, forward * attackRange, Color.blue, duration);

        if (hasTarget)
        {
            Debug.DrawRay(origin, toTarget, Color.red, duration);
        }

        if (attackAngle <= 0f || attackAngle >= 360f)
        {
            DrawDebugArc(origin, forward, 360f, attackRange, Color.yellow, duration, arcSegments);
            return;
        }

        float halfAngle = attackAngle * 0.5f;
        Vector3 leftBoundary = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 rightBoundary = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

        Debug.DrawRay(origin, leftBoundary * attackRange, Color.yellow, duration);
        Debug.DrawRay(origin, rightBoundary * attackRange, Color.yellow, duration);

        DrawDebugArc(origin, forward, attackAngle, attackRange, Color.yellow, duration, arcSegments);
    }

    private void DrawDebugArc(
        Vector3 origin,
        Vector3 forward,
        float angle,
        float radius,
        Color color,
        float duration,
        int segments)
    {
        float halfAngle = angle * 0.5f;
        Vector3 previousDirection = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 previousPoint = origin + previousDirection * radius;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = -halfAngle + angle * i / segments;
            Vector3 currentDirection = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;
            Vector3 currentPoint = origin + currentDirection * radius;

            Debug.DrawLine(previousPoint, currentPoint, color, duration);
            previousPoint = currentPoint;
        }
    }

    private void FillDefaultReferences()
    {
        if (owner == null)
        {
            owner = GetComponent<CombatCharacter>();
        }
    }

    private bool ShouldSuppressCombatDebug()
    {
        StudentCombatAgent[] agents = FindObjectsByType<StudentCombatAgent>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (StudentCombatAgent agent in agents)
        {
            if (agent.enabled)
            {
                return true;
            }
        }

        return false;
    }
}
