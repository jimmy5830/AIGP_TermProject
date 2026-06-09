using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CombatCsvLogger : MonoBehaviour
{
    public enum AnalyzeTarget
    {
        AgentA,
        AgentB
    }

    private enum ActionState
    {
        Idle,
        MoveToward,
        MoveBack,
        MoveSide,
        Attack,
        Block,
        Dodge
    }

    [Header("Run Info")]
    [SerializeField] private string runId = "defender_bt_v1";
    [SerializeField] private string agentType = "BT";

    [Header("Output Settings")]
    [Tooltip("Unity 프로젝트 루트 기준 상대 경로입니다. 예: CombatCsvLogs, Logs/CombatCsvLogs")]
    [SerializeField] private string relativeLogFolder = "CombatCsvLogs";

    [Header("Match Settings")]
    [SerializeField] private AnalyzeTarget analyzeTarget = AnalyzeTarget.AgentB;
    [SerializeField] private int targetEpisodeCount = 100;

    [Tooltip("true이면 제한 시간 도달도 1판 종료로 기록합니다. 무한 대치 방지를 위해 true 권장.")]
    [SerializeField] private bool countTimeoutAsEpisodeEnd = true;

    [SerializeField] private float maxEpisodeTime = 60f;

    [Tooltip("목표 episode 수가 끝나면 Unity Play Mode를 자동 종료합니다.")]
    [SerializeField] private bool stopPlayModeWhenFinished = true;

    [Header("Runtime Status")]
    [SerializeField] private int currentEpisodeForInspector;
    [SerializeField] private int completedEpisodesForInspector;
    [SerializeField] private string currentMatchStateForInspector = "Not Started";
    [SerializeField] private string lastWinnerForInspector = "";
    [SerializeField] private float currentEpisodeElapsedTimeForInspector;

    [Header("Speed Settings")]
    [Tooltip("게임 배속입니다. 1 = 기본 속도, 3 = 3배속, 5 = 5배속")]
    [SerializeField] private float timeScale = 1f;

    [Tooltip("실험 종료 또는 Play 중단 시 Time.timeScale을 원래 값으로 되돌립니다.")]
    [SerializeField] private bool restoreTimeScaleOnEnd = true;

    [Tooltip("true이면 fixedDeltaTime도 timeScale에 맞춰 키웁니다. CPU 부담은 줄지만 물리 정확도는 낮아질 수 있습니다. 비교 실험에서는 false 권장.")]
    [SerializeField] private bool adjustFixedDeltaTimeWithTimeScale = false;

    private float originalTimeScale;
    private float originalFixedDeltaTime;
    private bool timeScaleApplied;
    private bool timeScaleRestored;

    [Header("Reset Settings")]
    [Tooltip("각 판 종료 후 EpisodeManager.ResetEpisode()를 호출합니다.")]
    [SerializeField] private bool resetEpisodeByLogger = true;

    [SerializeField] private float interEpisodeDelay = 0.2f;

    [Header("Scene References")]
    [SerializeField] private CombatCharacter agentA;
    [SerializeField] private CombatCharacter agentB;
    [SerializeField] private EpisodeManager episodeManager;

    [Header("Movement Detection")]
    [Tooltip("이 값보다 적게 움직이면 Idle로 봅니다.")]
    [SerializeField] private float movementDistanceThreshold = 0.005f;

    [Tooltip("상대와의 거리 변화가 이 값보다 크면 MoveToward / MoveBack으로 분류합니다.")]
    [SerializeField] private float distanceChangeThreshold = 0.005f;

    [Tooltip("Dodge 직후 밀려나는 움직임을 MoveBack으로 중복 집계하지 않기 위한 무시 시간입니다.")]
    [SerializeField] private float dodgeMovementIgnoreSeconds = 0.25f;

    private CombatCharacter analyzedAgent;
    private CombatCharacter opponentAgent;
    private CombatActionController analyzedAction;

    private string sessionId;
    private string logFolderPath;
    private string csvPath;
    private StreamWriter writer;

    private bool episodeActive;
    private bool waitingForNextEpisode;
    private bool matchFinished;

    private int episodeIndex;
    private float episodeStartTime;

    private Vector3 previousPosition;
    private float previousDistance;
    private ActionState previousActionState = ActionState.Idle;
    private float ignoreMovementUntil;

    private int episodeAttackCount;
    private int episodeBlockCount;
    private int episodeDodgeCount;
    private int episodeMoveTowardCount;
    private int episodeMoveBackCount;
    private int episodeMoveSideCount;

    private int completedEpisodes;
    private int analyzedWinCount;
    private int timeoutCount;

    private float totalDuration;
    private float totalAgentAFinalHp;
    private float totalAgentBFinalHp;

    private int totalAttackCount;
    private int totalBlockCount;
    private int totalDodgeCount;
    private int totalMoveTowardCount;
    private int totalMoveBackCount;
    private int totalMoveSideCount;

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private void Start()
    {
        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;

        ApplyTimeScale();

        FillDefaultReferences();
        SelectAnalyzedAgent();
        PrepareCsvFile();

        currentMatchStateForInspector = "Started";

        BeginEpisode();

        Debug.Log($"[CombatCsvLogger] CSV 저장 위치: {csvPath}");
    }

    private void Update()
    {
        if (matchFinished || waitingForNextEpisode)
        {
            UpdateRuntimeStatusForInspector();
            return;
        }

        FillDefaultReferences();
        SelectAnalyzedAgent();

        if (agentA == null ||
            agentB == null ||
            analyzedAgent == null ||
            opponentAgent == null ||
            analyzedAction == null)
        {
            currentMatchStateForInspector = "Missing References";
            return;
        }

        if (!episodeActive)
        {
            BeginEpisode();
        }

        UpdateActionCounts();
        UpdateRuntimeStatusForInspector();
        CheckEpisodeEnd();
    }

    private void OnDestroy()
    {
        CloseWriter();
        RestoreTimeScale();
    }

    private void OnValidate()
    {
        targetEpisodeCount = Mathf.Max(1, targetEpisodeCount);
        maxEpisodeTime = Mathf.Max(0.1f, maxEpisodeTime);
        timeScale = Mathf.Max(0.1f, timeScale);
        interEpisodeDelay = Mathf.Max(0f, interEpisodeDelay);
        movementDistanceThreshold = Mathf.Max(0f, movementDistanceThreshold);
        distanceChangeThreshold = Mathf.Max(0f, distanceChangeThreshold);
        dodgeMovementIgnoreSeconds = Mathf.Max(0f, dodgeMovementIgnoreSeconds);
    }

    private void ApplyTimeScale()
    {
        timeScale = Mathf.Max(0.1f, timeScale);

        Time.timeScale = timeScale;

        if (adjustFixedDeltaTimeWithTimeScale)
        {
            Time.fixedDeltaTime = originalFixedDeltaTime * timeScale;
        }
        else
        {
            Time.fixedDeltaTime = originalFixedDeltaTime;
        }

        timeScaleApplied = true;
        timeScaleRestored = false;

        Debug.Log($"[CombatCsvLogger] Time Scale 적용: {timeScale}x");
    }

    private void RestoreTimeScale()
    {
        if (!restoreTimeScaleOnEnd || !timeScaleApplied || timeScaleRestored)
        {
            return;
        }

        Time.timeScale = originalTimeScale;
        Time.fixedDeltaTime = originalFixedDeltaTime;

        timeScaleRestored = true;

        Debug.Log("[CombatCsvLogger] Time Scale 원복 완료");
    }

    private void FillDefaultReferences()
    {
        if (agentA == null)
        {
            GameObject found = GameObject.Find("Agent_A");
            if (found != null)
            {
                agentA = found.GetComponent<CombatCharacter>();
            }
        }

        if (agentB == null)
        {
            GameObject found = GameObject.Find("Agent_B");
            if (found != null)
            {
                agentB = found.GetComponent<CombatCharacter>();
            }
        }

        if (episodeManager == null)
        {
            episodeManager = FindFirstObjectByType<EpisodeManager>();
        }
    }

    private void SelectAnalyzedAgent()
    {
        if (analyzeTarget == AnalyzeTarget.AgentA)
        {
            analyzedAgent = agentA;
            opponentAgent = agentB;
            analyzedAction = agentA != null ? agentA.ActionController : null;
        }
        else
        {
            analyzedAgent = agentB;
            opponentAgent = agentA;
            analyzedAction = agentB != null ? agentB.ActionController : null;
        }
    }

    private void PrepareCsvFile()
    {
        sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        logFolderPath = GetProjectRelativeLogFolderPath();

        if (!Directory.Exists(logFolderPath))
        {
            Directory.CreateDirectory(logFolderPath);
        }

        string safeRunId = MakeSafeFileName(runId);
        string fileName = $"{safeRunId}_{analyzeTarget}_{sessionId}_match_result.csv";
        csvPath = Path.Combine(logFolderPath, fileName);

        writer = new StreamWriter(csvPath, append: false, new UTF8Encoding(true));
        writer.AutoFlush = true;

        WriteCsvLine(
            "row_type",
            "session_id",
            "run_id",
            "agent_type",
            "analyze_target",
            "episode",
            "winner",
            "result_for_analyzed",
            "duration_sec",
            "agent_a_final_hp",
            "agent_b_final_hp",
            "attack_count",
            "block_count",
            "dodge_count",
            "move_toward_count",
            "move_back_count",
            "move_side_count",
            "total_action_count",
            "attack_ratio_percent",
            "block_ratio_percent",
            "dodge_ratio_percent",
            "move_toward_ratio_percent",
            "move_back_ratio_percent",
            "move_side_ratio_percent"
        );
    }

    private void BeginEpisode()
    {
        if (agentA == null ||
            agentB == null ||
            analyzedAgent == null ||
            opponentAgent == null)
        {
            return;
        }

        episodeIndex++;
        episodeActive = true;

        currentEpisodeForInspector = episodeIndex;
        completedEpisodesForInspector = completedEpisodes;
        currentMatchStateForInspector = "Running";
        currentEpisodeElapsedTimeForInspector = 0f;

        episodeStartTime = Time.time;
        previousPosition = analyzedAgent.transform.position;
        previousDistance = GetDistanceBetweenAnalyzedAndOpponent();
        previousActionState = ActionState.Idle;
        ignoreMovementUntil = 0f;

        ResetEpisodeActionCounts();

        Debug.Log($"[CombatCsvLogger] Episode {episodeIndex}/{targetEpisodeCount} 시작 - 분석 대상: {analyzeTarget}");
    }

    private void ResetEpisodeActionCounts()
    {
        episodeAttackCount = 0;
        episodeBlockCount = 0;
        episodeDodgeCount = 0;
        episodeMoveTowardCount = 0;
        episodeMoveBackCount = 0;
        episodeMoveSideCount = 0;
    }

    private void UpdateActionCounts()
    {
        ActionState currentState = InferCurrentActionState();

        if (currentState == ActionState.Dodge)
        {
            ignoreMovementUntil = Time.time + dodgeMovementIgnoreSeconds;
        }

        if (currentState != previousActionState)
        {
            if (currentState != ActionState.Idle)
            {
                AddActionCount(currentState);
            }

            previousActionState = currentState;
        }

        previousPosition = analyzedAgent.transform.position;
        previousDistance = GetDistanceBetweenAnalyzedAndOpponent();
    }

    private void UpdateRuntimeStatusForInspector()
    {
        completedEpisodesForInspector = completedEpisodes;

        if (episodeActive)
        {
            currentEpisodeElapsedTimeForInspector = Time.time - episodeStartTime;
        }
    }

    private ActionState InferCurrentActionState()
    {
        if (analyzedAction == null ||
            analyzedAgent == null ||
            opponentAgent == null)
        {
            return ActionState.Idle;
        }

        if (analyzedAction.IsAttacking)
        {
            return ActionState.Attack;
        }

        if (analyzedAction.IsBlocking)
        {
            return ActionState.Block;
        }

        if (analyzedAction.IsInvincible)
        {
            return ActionState.Dodge;
        }

        if (Time.time < ignoreMovementUntil)
        {
            return ActionState.Idle;
        }

        Vector3 currentPosition = analyzedAgent.transform.position;
        Vector3 movement = currentPosition - previousPosition;
        movement.y = 0f;

        if (movement.magnitude < movementDistanceThreshold)
        {
            return ActionState.Idle;
        }

        float currentDistance = GetDistanceBetweenAnalyzedAndOpponent();
        float distanceDelta = currentDistance - previousDistance;

        if (distanceDelta < -distanceChangeThreshold)
        {
            return ActionState.MoveToward;
        }

        if (distanceDelta > distanceChangeThreshold)
        {
            return ActionState.MoveBack;
        }

        return ActionState.MoveSide;
    }

    private void AddActionCount(ActionState state)
    {
        switch (state)
        {
            case ActionState.Attack:
                episodeAttackCount++;
                break;

            case ActionState.Block:
                episodeBlockCount++;
                break;

            case ActionState.Dodge:
                episodeDodgeCount++;
                break;

            case ActionState.MoveToward:
                episodeMoveTowardCount++;
                break;

            case ActionState.MoveBack:
                episodeMoveBackCount++;
                break;

            case ActionState.MoveSide:
                episodeMoveSideCount++;
                break;
        }
    }

    private void CheckEpisodeEnd()
    {
        bool agentADead = agentA != null && agentA.IsDead;
        bool agentBDead = agentB != null && agentB.IsDead;

        bool timeout =
            countTimeoutAsEpisodeEnd &&
            maxEpisodeTime > 0f &&
            Time.time - episodeStartTime >= maxEpisodeTime;

        if (!agentADead && !agentBDead && !timeout)
        {
            return;
        }

        string winner;

        if (agentADead && agentBDead)
        {
            winner = "Draw";
        }
        else if (agentBDead)
        {
            winner = "A";
        }
        else if (agentADead)
        {
            winner = "B";
        }
        else
        {
            winner = "TimeoutDraw";
        }

        EndEpisodeAndRecord(winner);
    }

    private void EndEpisodeAndRecord(string winner)
    {
        episodeActive = false;

        currentMatchStateForInspector = "Episode Ended";
        lastWinnerForInspector = winner;

        float duration = Time.time - episodeStartTime;
        string resultForAnalyzed = GetResultForAnalyzed(winner);

        int totalActions = GetEpisodeTotalActions();

        WriteCsvLine(
            Csv("EPISODE"),
            Csv(sessionId),
            Csv(runId),
            Csv(agentType),
            Csv(analyzeTarget.ToString()),
            IntText(episodeIndex),
            Csv(winner),
            Csv(resultForAnalyzed),
            FloatText(duration),
            FloatText(agentA.CurrentHealth),
            FloatText(agentB.CurrentHealth),
            IntText(episodeAttackCount),
            IntText(episodeBlockCount),
            IntText(episodeDodgeCount),
            IntText(episodeMoveTowardCount),
            IntText(episodeMoveBackCount),
            IntText(episodeMoveSideCount),
            IntText(totalActions),
            PercentText(episodeAttackCount, totalActions),
            PercentText(episodeBlockCount, totalActions),
            PercentText(episodeDodgeCount, totalActions),
            PercentText(episodeMoveTowardCount, totalActions),
            PercentText(episodeMoveBackCount, totalActions),
            PercentText(episodeMoveSideCount, totalActions)
        );

        AccumulateTotals(winner, duration);

        Debug.Log(
            $"[CombatCsvLogger] Episode {episodeIndex} 완료 / winner={winner}, result={resultForAnalyzed}, duration={duration:F2}s"
        );

        if (completedEpisodes >= targetEpisodeCount)
        {
            FinishMatch();
            return;
        }

        StartCoroutine(ResetAndBeginNextEpisode());
    }

    private IEnumerator ResetAndBeginNextEpisode()
    {
        waitingForNextEpisode = true;
        currentMatchStateForInspector = "Waiting For Reset";

        yield return new WaitForSeconds(interEpisodeDelay);

        if (resetEpisodeByLogger)
        {
            if (episodeManager != null)
            {
                episodeManager.ResetEpisode();
            }
            else
            {
                Debug.LogWarning("[CombatCsvLogger] EpisodeManager가 없어 자동 Reset을 호출하지 못했습니다.");
            }
        }

        yield return null;

        waitingForNextEpisode = false;
        BeginEpisode();
    }

    private void AccumulateTotals(string winner, float duration)
    {
        completedEpisodes++;
        completedEpisodesForInspector = completedEpisodes;

        totalDuration += duration;

        if (agentA != null)
        {
            totalAgentAFinalHp += agentA.CurrentHealth;
        }

        if (agentB != null)
        {
            totalAgentBFinalHp += agentB.CurrentHealth;
        }

        if ((analyzeTarget == AnalyzeTarget.AgentA && winner == "A") ||
            (analyzeTarget == AnalyzeTarget.AgentB && winner == "B"))
        {
            analyzedWinCount++;
        }

        if (winner == "TimeoutDraw")
        {
            timeoutCount++;
        }

        totalAttackCount += episodeAttackCount;
        totalBlockCount += episodeBlockCount;
        totalDodgeCount += episodeDodgeCount;
        totalMoveTowardCount += episodeMoveTowardCount;
        totalMoveBackCount += episodeMoveBackCount;
        totalMoveSideCount += episodeMoveSideCount;
    }

    private void FinishMatch()
    {
        matchFinished = true;
        currentMatchStateForInspector = "Finished";
        completedEpisodesForInspector = completedEpisodes;

        WriteSummaryRow();
        CloseWriter();

        RestoreTimeScale();

        Debug.Log($"[CombatCsvLogger] {completedEpisodes}판 완료. CSV 저장 완료: {csvPath}");

        if (stopPlayModeWhenFinished)
        {
            StopGame();
        }
    }

    private void WriteSummaryRow()
    {
        int episodeCount = Mathf.Max(1, completedEpisodes);

        int totalActions =
            totalAttackCount +
            totalBlockCount +
            totalDodgeCount +
            totalMoveTowardCount +
            totalMoveBackCount +
            totalMoveSideCount;

        float avgDuration = totalDuration / episodeCount;
        float avgAgentAFinalHp = totalAgentAFinalHp / episodeCount;
        float avgAgentBFinalHp = totalAgentBFinalHp / episodeCount;

        WriteCsvLine(
            Csv("SUMMARY"),
            Csv(sessionId),
            Csv(runId),
            Csv(agentType),
            Csv(analyzeTarget.ToString()),
            Csv("ALL"),
            "",
            IntText(analyzedWinCount),
            FloatText(avgDuration),
            FloatText(avgAgentAFinalHp),
            FloatText(avgAgentBFinalHp),
            IntText(totalAttackCount),
            IntText(totalBlockCount),
            IntText(totalDodgeCount),
            IntText(totalMoveTowardCount),
            IntText(totalMoveBackCount),
            IntText(totalMoveSideCount),
            IntText(totalActions),
            PercentText(totalAttackCount, totalActions),
            PercentText(totalBlockCount, totalActions),
            PercentText(totalDodgeCount, totalActions),
            PercentText(totalMoveTowardCount, totalActions),
            PercentText(totalMoveBackCount, totalActions),
            PercentText(totalMoveSideCount, totalActions)
        );
    }

    private string GetResultForAnalyzed(string winner)
    {
        if (winner == "Draw")
        {
            return "draw";
        }

        if (winner == "TimeoutDraw")
        {
            return "timeout";
        }

        if (analyzeTarget == AnalyzeTarget.AgentA)
        {
            return winner == "A" ? "win" : "loss";
        }

        return winner == "B" ? "win" : "loss";
    }

    private int GetEpisodeTotalActions()
    {
        return episodeAttackCount
            + episodeBlockCount
            + episodeDodgeCount
            + episodeMoveTowardCount
            + episodeMoveBackCount
            + episodeMoveSideCount;
    }

    private float GetDistanceBetweenAnalyzedAndOpponent()
    {
        if (analyzedAgent == null || opponentAgent == null)
        {
            return 0f;
        }

        Vector3 offset = opponentAgent.transform.position - analyzedAgent.transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void WriteCsvLine(params string[] values)
    {
        writer?.WriteLine(string.Join(",", values));
    }

    private string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private string IntText(int value)
    {
        return value.ToString(Culture);
    }

    private string FloatText(float value)
    {
        return value.ToString("F3", Culture);
    }

    private string PercentText(int count, int total)
    {
        if (total <= 0)
        {
            return "0.000";
        }

        return ((float)count / total * 100f).ToString("F3", Culture);
    }

    private string GetProjectRelativeLogFolderPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;

        if (string.IsNullOrEmpty(projectRoot))
        {
            projectRoot = Application.dataPath;
        }

        string relativePath = string.IsNullOrWhiteSpace(relativeLogFolder)
            ? "CombatCsvLogs"
            : relativeLogFolder.Trim();

        if (Path.IsPathRooted(relativePath))
        {
            Debug.LogWarning(
                $"[CombatCsvLogger] relativeLogFolder에는 상대 경로만 입력하는 것을 권장합니다. 입력값: {relativePath}"
            );

            relativePath = "CombatCsvLogs";
        }

        relativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(projectRoot, relativePath));
    }

    private string MakeSafeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "combat_result";
        }

        string safe = raw;

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        return safe;
    }

    private void CloseWriter()
    {
        if (writer == null)
        {
            return;
        }

        writer.Flush();
        writer.Close();
        writer = null;
    }

    private void StopGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}