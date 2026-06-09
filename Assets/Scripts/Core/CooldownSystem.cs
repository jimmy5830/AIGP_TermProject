using UnityEngine;

// Tracks skill readiness using Time.time so any controller can query the same cooldown API.
public class CooldownSystem : MonoBehaviour
{
    [SerializeField] private float attackCooldown = 2.5f;
    [SerializeField] private float blockCooldown = 2.5f;
    [SerializeField] private float dodgeCooldown = 5.0f;

    private float attackReadyTime;
    private float blockReadyTime;
    private float dodgeReadyTime;

    public bool IsAttackReady()
    {
        return Time.time >= attackReadyTime;
    }

    public bool IsBlockReady()
    {
        return Time.time >= blockReadyTime;
    }

    public bool IsDodgeReady()
    {
        return Time.time >= dodgeReadyTime;
    }

    public void TriggerAttackCooldown()
    {
        attackReadyTime = Time.time + Mathf.Max(0f, attackCooldown);
    }

    public void TriggerBlockCooldown()
    {
        blockReadyTime = Time.time + Mathf.Max(0f, blockCooldown);
    }

    public void TriggerDodgeCooldown()
    {
        dodgeReadyTime = Time.time + Mathf.Max(0f, dodgeCooldown);
    }

    public void ResetCooldowns()
    {
        attackReadyTime = 0f;
        blockReadyTime = 0f;
        dodgeReadyTime = 0f;
    }

    public float GetAttackCooldownRatio()
    {
        return GetCooldownRatio(attackReadyTime, attackCooldown);
    }

    public float GetBlockCooldownRatio()
    {
        return GetCooldownRatio(blockReadyTime, blockCooldown);
    }

    public float GetDodgeCooldownRatio()
    {
        return GetCooldownRatio(dodgeReadyTime, dodgeCooldown);
    }

    private float GetCooldownRatio(float readyTime, float duration)
    {
        if (duration <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01((readyTime - Time.time) / duration);
    }
}
