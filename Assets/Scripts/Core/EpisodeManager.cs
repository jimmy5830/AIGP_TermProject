using System.Collections;
using UnityEngine;

// Resets both combatants and reports the episode result for later BT/RL training loops.
public class EpisodeManager : MonoBehaviour
{
    [SerializeField] private CombatCharacter agentA;
    [SerializeField] private CombatCharacter agentB;
    [SerializeField] private Transform spawnPointA;
    [SerializeField] private Transform spawnPointB;
    [SerializeField] private float resetDelay = 1.5f;
    [SerializeField] private float maxEpisodeTime = 60f;

    private bool episodeDone;
    private float episodeStartTime;
    private Coroutine delayedResetRoutine;

    private void Awake()
    {
        FillDefaultReferences();
    }

    private void Start()
    {
        ResetEpisode();
    }

    private void Update()
    {
        CheckEpisodeEnd();
    }

    private void Reset()
    {
        FillDefaultReferences();
    }

    public void ResetEpisode()
    {
        if (delayedResetRoutine != null)
        {
            StopCoroutine(delayedResetRoutine);
            delayedResetRoutine = null;
        }

        episodeDone = false;
        episodeStartTime = Time.time;

        ResetAgent(agentA, spawnPointA);
        ResetAgent(agentB, spawnPointB);

        Debug.Log("Episode reset.");
    }

    public bool CheckEpisodeEnd()
    {
        if (episodeDone)
        {
            return true;
        }

        bool agentADead = agentA != null && agentA.IsDead;
        bool agentBDead = agentB != null && agentB.IsDead;

        if (agentADead || agentBDead)
        {
            if (agentADead && agentBDead)
            {
                EndEpisode("draw");
            }
            else if (agentBDead)
            {
                EndEpisode($"{agentA.name} wins");
            }
            else
            {
                EndEpisode($"{agentB.name} wins");
            }

            return true;
        }

        if (maxEpisodeTime > 0f && Time.time - episodeStartTime >= maxEpisodeTime)
        {
            EndEpisode("timeout draw");
            return true;
        }

        return false;
    }

    public bool IsEpisodeDone()
    {
        return episodeDone;
    }

    private void EndEpisode(string result)
    {
        episodeDone = true;
        Debug.Log($"Episode ended: {result}.");

        if (delayedResetRoutine == null)
        {
            delayedResetRoutine = StartCoroutine(ResetAfterDelay());
        }
    }

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);

        delayedResetRoutine = null;
        ResetEpisode();
    }

    private void ResetAgent(CombatCharacter agent, Transform spawnPoint)
    {
        if (agent == null)
        {
            return;
        }

        CombatActionController actionController = agent.ActionController;
        CooldownSystem cooldownSystem = agent.CooldownSystem;

        actionController?.ResetActionState();
        cooldownSystem?.ResetCooldowns();
        agent.ResetCharacter();

        Rigidbody body = agent.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (spawnPoint != null)
        {
            agent.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
        }
    }

    private void FillDefaultReferences()
    {
        if (agentA == null)
        {
            GameObject found = GameObject.Find("Agent_A");
            agentA = found != null ? found.GetComponent<CombatCharacter>() : null;
        }

        if (agentB == null)
        {
            GameObject found = GameObject.Find("Agent_B");
            agentB = found != null ? found.GetComponent<CombatCharacter>() : null;
        }
    }
}
