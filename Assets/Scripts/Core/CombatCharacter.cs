using UnityEngine;

// Stores health and component references shared by all combat controllers.
public class CombatCharacter : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    [SerializeField] private CombatActionController actionController;
    [SerializeField] private CooldownSystem cooldownSystem;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public bool IsDead => currentHealth <= 0f;
    public float CurrentHealthRatio => maxHealth <= 0f ? 0f : currentHealth / maxHealth;
    public CombatActionController ActionController => actionController;
    public CooldownSystem CooldownSystem => cooldownSystem;

    private void Awake()
    {
        FillDefaultReferences();

        if (currentHealth <= 0f)
        {
            currentHealth = maxHealth;
        }
    }

    private void Reset()
    {
        FillDefaultReferences();
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead)
        {
            return;
        }

        float safeAmount = Mathf.Max(0f, amount);
        currentHealth = Mathf.Clamp(currentHealth - safeAmount, 0f, maxHealth);
        actionController?.PlayHitReaction();
    }

    public void Heal(float amount)
    {
        if (IsDead)
        {
            return;
        }

        float safeAmount = Mathf.Max(0f, amount);
        currentHealth = Mathf.Clamp(currentHealth + safeAmount, 0f, maxHealth);
    }

    public void ResetCharacter()
    {
        currentHealth = maxHealth;
    }

    private void FillDefaultReferences()
    {
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
