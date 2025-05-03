using System;
using System.Collections.Generic;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Demonstrations;
using UnityEngine;
using UnityEngine.Pool;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MazeEnvironmentManager : MonoBehaviour
{
    public static MazeEnvironmentManager Instance { get; private set; }

    [Header("References")]
    public MazeGenerator mazeGenerator;
    public MazeAgent agentPrefab;

    [Header("Environment Parameters")]
    public int numberOfAgents = 5;
    public int performanceWindow = 20;
    [Range(0f, 1f)] public float highThreshold = 0.8f;
    [Range(0f, 1f)] public float lowThreshold = 0.3f;
    [Range(0f, 1f)] public float tinyMazeChance = 0.10f;
    [SerializeField]
    private List<int> orderedSizes = new List<int> { 31, 36, 41, 46, 51, 56, 61, 66, 71, 76, 81, 91, 101, 111, 121, 130, 140 };

    [Header("Heuristic Demo Settings")]
    public bool recordHeuristicDemo = true;
    public int demoRepeats = 3;
    public string demoDirectory = "Assets/ML-Agents/Demonstrations";
    public string demoBaseName = "maze_demo";

    private MazeAgent[] agents;
    private ObjectPool<MazeAgent> agentPool;
    private int difficultyIndex = 2;
    private readonly Queue<float> recentSuccessRates = new Queue<float>();
    private bool goalReachedFlag = false;
    private float countdownTimer;
    private const float CountdownDuration = 10f;

    private int currentDemoRun = 0;
    private List<DemonstrationRecorder> recorders = new List<DemonstrationRecorder>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        InitializeAgentPool();

        if (recordHeuristicDemo)
        {
            string fullPath = Path.Combine(Application.dataPath, "ML-Agents", "Demonstrations");
            Directory.CreateDirectory(fullPath);
        }
    }

    private void Start()
    {
        BuildEnvironment();
    }

    private void Update()
    {
        if (agents == null) return;

        if (!goalReachedFlag)
        {
            foreach (var a in agents)
            {
                if (a.ReachedGoal)
                {
                    goalReachedFlag = true;
                    countdownTimer = recordHeuristicDemo ? float.PositiveInfinity : CountdownDuration;
                    break;
                }
            }
        }
        else if (!recordHeuristicDemo)
        {
            countdownTimer -= Time.deltaTime;
            if (countdownTimer <= 0f)
            {
                foreach (var a in agents)
                {
                    if (!a.ReachedGoal && !a.IsInactive)
                    {
                        a.AddReward(-10f);
                        a.MarkAsInactive();
                    }
                }
                goalReachedFlag = false;
            }
        }

        bool allDone = true;
        foreach (var a in agents)
        {
            if (!a.IsInactive)
            {
                allDone = false;
                break;
            }
        }

        if (allDone)
        {
            if (recordHeuristicDemo)
            {
                StopDemoRecording();
            }
            else
            {
                ResetEnvironment();
            }
        }
    }

    private void BuildEnvironment()
    {
        RebuildMaze();
        SpawnAgents();
        ResetAgents();

        if (recordHeuristicDemo)
            StartDemoRecording();
    }

    public void ResetEnvironment()
    {
        AdjustDifficulty();
        RebuildMaze();
        ResetAgents();
    }

    private void RebuildMaze()
    {
        float ext = Academy.Instance.EnvironmentParameters.GetWithDefault("maze_size", -1f);
        int size;

        if (ext > 0f) size = Mathf.RoundToInt(ext);
        else if (UnityEngine.Random.value < tinyMazeChance && orderedSizes.Count >= 4)
            size = orderedSizes[UnityEngine.Random.Range(0, 4)];
        else
            size = orderedSizes[Mathf.Clamp(difficultyIndex, 0, orderedSizes.Count - 1)];

        mazeGenerator.width = size;
        mazeGenerator.height = size;
        mazeGenerator.GenerateMazeEnvironment();
    }

    private void SpawnAgents()
    {
        if (agents != null)
            foreach (var a in agents)
                agentPool.Release(a);

        agents = new MazeAgent[numberOfAgents];
        var starts = GetDistinctStartPositions(numberOfAgents);

        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector2 p2 = (i < starts.Count) ? starts[i] : starts[0];
            Vector3 worldP = new Vector3(p2.x, p2.y, 0f);

            agents[i] = agentPool.Get();
            agents[i].transform.SetParent(null, true);
            agents[i].transform.position = worldP;
        }
    }

    private void ResetAgents()
    {
        if (agents == null) return;

        var starts = GetDistinctStartPositions(agents.Length);
        for (int i = 0; i < agents.Length; i++)
        {
            Vector2 p2 = (i < starts.Count) ? starts[i] : starts[0];
            agents[i].transform.position = new Vector3(p2.x, p2.y, 0f);
            agents[i].EndEpisode();
        }

        goalReachedFlag = false;
    }

    private void AdjustDifficulty()
    {
        if (orderedSizes.Count <= 1) return;

        int succ = 0;
        foreach (var a in agents)
            if (a.ReachedGoal) succ++;

        float rate = succ / (float)agents.Length;
        recentSuccessRates.Enqueue(rate);
        if (recentSuccessRates.Count > performanceWindow)
            recentSuccessRates.Dequeue();

        float avg = 0f;
        foreach (var r in recentSuccessRates) avg += r;
        avg /= recentSuccessRates.Count;

        if (avg >= highThreshold && difficultyIndex < orderedSizes.Count - 1) difficultyIndex++;
        else if (avg <= lowThreshold && difficultyIndex > 0) difficultyIndex--;
    }

    private void InitializeAgentPool()
    {
        agentPool = new ObjectPool<MazeAgent>(
            () => Instantiate(agentPrefab),
            a => a.gameObject.SetActive(true),
            a => a.gameObject.SetActive(false),
            a => Destroy(a.gameObject),
            false, numberOfAgents, numberOfAgents * 2
        );
    }

    private List<Vector2> GetDistinctStartPositions(int count)
    {
        var list = new List<Vector2>(count);
        int cx = mazeGenerator.width / 2, cy = mazeGenerator.height / 2;

        if (mazeGenerator.maze[cx, cy] == 0)
            list.Add(mazeGenerator.GetWorldPosition(cx, cy));

        int radius = 1;
        while (list.Count < count)
        {
            for (int dx = -radius; dx <= radius && list.Count < count; dx++)
                for (int dy = -radius; dy <= radius && list.Count < count; dy++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (mazeGenerator.IsInBounds(x, y) && mazeGenerator.maze[x, y] == 0)
                    {
                        Vector2 p = mazeGenerator.GetWorldPosition(x, y);
                        if (!list.Contains(p)) list.Add(p);
                    }
                }
            radius++;
        }

        return list;
    }

    private void StartDemoRecording()
    {
        currentDemoRun = 0;
        ResetEnvironment();
        Invoke(nameof(RecordForAllAgents), 0.1f);
    }

    private void RecordForAllAgents()
    {
        recorders.Clear();
        string path = Path.Combine("Assets", "ML-Agents", "Demonstrations");

        bool shouldRecord = currentDemoRun < demoRepeats - 1;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsInactive || agents[i].ReachedGoal)
                continue;

            var existing = agents[i].GetComponent<DemonstrationRecorder>();
            if (existing != null) Destroy(existing);

            if (shouldRecord)
            {
                var recorder = agents[i].gameObject.AddComponent<DemonstrationRecorder>();
                recorder.DemonstrationDirectory = path;
                recorder.DemonstrationName = $"{demoBaseName}_run{currentDemoRun + 1:00}_agent{i:00}";
                recorder.Record = true;
                recorders.Add(recorder);
            }
        }
    }


    private void StopDemoRecording()
    {
        foreach (var rec in recorders)
        {
            if (rec != null)
                rec.Close();
        }
        recorders.Clear();

        currentDemoRun++;
        if (currentDemoRun < demoRepeats)
        {
            ResetEnvironment();
            Invoke(nameof(RecordForAllAgents), 0.1f);
        }
        else
        {
            Debug.Log("\u2705 All heuristic demonstrations recorded!");
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}