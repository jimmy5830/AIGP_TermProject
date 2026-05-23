using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

[RequireComponent(typeof(CombatCharacter))]
[RequireComponent(typeof(CooldownSystem))]
[RequireComponent(typeof(CombatActionController))]
public class StudentCombatAgent : Agent
{
    public CombatCharacter self;
    public CombatCharacter opponent;
    public CombatActionController actionController;
    public CooldownSystem cooldownSystem;
    public EpisodeManager episodeManager;

    // Action constants for a clear RL action space.
    // Branch 0: action type (0 = none, 1 = attack, 2 = block, 3 = dodge)
    // Add more branches if more complex behaviors are needed.
    // Make sure the Behavior Parameters action space in Unity Editor matches these constants.
    private const int SkillNone = 0;
    private const int SkillAttack = 1;
    private const int SkillBlock = 2;
    private const int SkillDodge = 3;


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
        // TODO: Reset or initialize values needed at the start of each episode.
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // TODO: Add observations for the agent to learn from.
        // Example:
        // sensor.AddObservation(self.CurrentHealthRatio);
        // Make sure the Behavior Parameters observation space in Unity Editor matches the number of observations added here.
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // TODO: Convert actions into movement or combat commands.
        // TODO: Add rewards or penalties based on the result.
        // TODO: End the episode when needed.
    }

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

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }
    }
}
