using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using UnityEngine;

[RequireComponent(typeof(RayPerceptionSensorComponent2D))]
public class MazeAgent : Agent
{
    [HideInInspector] public MazeGenerator mazeGenerator;
    public float moveDelay = 0f;

    [Header("Step-wise Rewards")]
    public float stepPenalty = -0.01f;
    public float wallPenalty = -0.05f;
    public float potentialCoeff = 2f;

    [Header("Goal Reward")]
    public float goalReward = 10f;

    [Header("Periodic Penalties")]
    public int periodicStepInterval = 1000;
    public float periodicStepPenalty = -10f;

    [Header("No-Progress Penalties")]
    public float tinyNoProgressPenalty = -0.005f;
    public int noProgressPenaltyInterval = 50;
    public float noProgressPenalty = -10f;

    [Header("Episode Settings")]
    public int maxStepCount = 2000;
    public int noProgressThreshold = 200;

    [Header("Complexity Reward")]
    public float complexityRewardScale = 0.1f;

    public bool IsInactive => _isInactive;
    public bool ReachedGoal { get; private set; }

    private bool _isMoving;
    private bool _isInactive;
    private float _moveTimer;
    private int _currentStepCount;
    private int _noProgressSteps;
    private float _bestDistance = float.MaxValue;
    private float _prevDist;
    private int _mazeBFSLength;
    private float _maxDiag;
    private static float _originalFixedDeltaTime = 0f;
    private static bool _speedInitialized = false;

    public Queue<Vector2Int> plannedPath = new Queue<Vector2Int>();
    private List<Vector2> _exitWorldPositions = new List<Vector2>();

    private static readonly Vector2[] _dirs =
    {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right
    };

    private LayerMask _mazeLayerMask;
    private BehaviorParameters _behaviorParams;
    private int _mazeWidth, _mazeHeight;
    private float _invMazeWidth, _invMazeHeight;

    public override void Initialize()
    {
        if (!_speedInitialized)
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _speedInitialized = true;
        }
        mazeGenerator ??= MazeGenerator.Instance;
        _mazeLayerMask = LayerMask.GetMask("MazeEnvironment");
        _behaviorParams = GetComponent<BehaviorParameters>();

        if (_behaviorParams.BehaviorType == BehaviorType.HeuristicOnly)
        {
            Time.timeScale = 1f;  
            Time.fixedDeltaTime = _originalFixedDeltaTime / Time.timeScale;
        }
        else
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = _originalFixedDeltaTime;
        }
    }

    public override void OnEpisodeBegin()
    {
        _mazeWidth = mazeGenerator.width;
        _mazeHeight = mazeGenerator.height;
        float scale = (_mazeWidth * _mazeHeight) / (31f * 31f);

        maxStepCount = Mathf.RoundToInt(maxStepCount * scale);
        noProgressThreshold = Mathf.RoundToInt(noProgressThreshold * scale);

        _invMazeWidth = 1f / _mazeWidth;
        _invMazeHeight = 1f / _mazeHeight;

        if (_maxDiag == 0f)
        {
            _maxDiag = Mathf.Sqrt(_mazeWidth * _mazeWidth + _mazeHeight * _mazeHeight);
        }

        _currentStepCount = 0;
        _noProgressSteps = 0;
        _bestDistance = float.MaxValue;
        _isInactive = false;

        _mazeBFSLength = Mathf.RoundToInt(DistanceToNearestExit());


        Vector2 start = transform.position;
        if (!mazeGenerator.IsInBounds(
                Mathf.RoundToInt(start.x + _mazeWidth / 2f - 0.5f),
                Mathf.RoundToInt(start.y + _mazeHeight / 2f - 0.5f)))
        {
            start = mazeGenerator.GetWorldPosition(_mazeWidth / 2, _mazeHeight / 2);
        }
        ResetAgent(start);


        float baseReward = Mathf.Clamp(_mazeBFSLength * 0.5f, 1f, 100f);
        goalReward = baseReward + _mazeBFSLength * complexityRewardScale;

        if (_behaviorParams.BehaviorType == BehaviorType.HeuristicOnly)
            ComputePlannedPath();
        else
            plannedPath.Clear();

        RequestDecision();
    }


    private void ResetAgent(Vector2 startPos)
    {
        List<Vector2Int> freeCells = mazeGenerator.GetFreeCells();
        bool positionSet = false;

        List<Vector2> eligibleStarts = new List<Vector2>();

        Vector2 center = mazeGenerator.GetWorldPosition(_mazeWidth / 2, _mazeHeight / 2);
        float maxDist = _maxDiag * 0.2f; 

        foreach (var cell in freeCells)
        {
            Vector2 pos = mazeGenerator.GetWorldPosition(cell.x, cell.y);
            if (!CanMoveTo(pos)) continue;

            float distToCenter = Vector2.Distance(pos, center);
            if (distToCenter <= maxDist)
            {
                eligibleStarts.Add(pos);
            }
        }


        if (eligibleStarts.Count > 0)
        {
            transform.position = eligibleStarts[UnityEngine.Random.Range(0, eligibleStarts.Count)];
            positionSet = true;
        }


        if (!positionSet)
        {
            transform.position = startPos; 
        }

        Physics2D.SyncTransforms(); 

        _exitWorldPositions.Clear();
        var exits = mazeGenerator.GetExitGridPositions();
        foreach (var e in exits)
        {
            _exitWorldPositions.Add(mazeGenerator.GetWorldPosition(e.x, e.y));
        }

        _prevDist = DistanceToNearestExit();
        _isMoving = false;
        _moveTimer = 0f;
        ReachedGoal = false;
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        Vector2 me = (Vector2)transform.position;
        Vector2 dirNorm = Vector2.zero;
        Vector2 normPos = Vector2.zero;
        Vector2 normExit = Vector2.zero;

        if (_exitWorldPositions.Count > 0)
        {
            Vector2 exit = NearestExitWorld();
            dirNorm = (exit - me) / _maxDiag;

            float normX = (me.x + _mazeWidth * 0.5f) * _invMazeWidth;
            float normY = (me.y + _mazeHeight * 0.5f) * _invMazeHeight;
            normPos = new Vector2(normX, normY);

            float exitX = (exit.x + _mazeWidth * 0.5f) * _invMazeWidth;
            float exitY = (exit.y + _mazeHeight * 0.5f) * _invMazeHeight;
            normExit = new Vector2(exitX, exitY);
        }

        sensor.AddObservation(dirNorm);
        sensor.AddObservation(normPos);
        sensor.AddObservation(normExit);
    }

    private void FixedUpdate()
    {
        if (_isInactive) return;

        if (mazeGenerator.IsAtExit(transform.position))
        {
            AddReward(goalReward);
            ReachedGoal = true;
            _isInactive = true;
            return;
        }

        if (_isMoving)
        {
            _moveTimer -= Time.fixedDeltaTime;
            if (_moveTimer <= 0f)
            {
                _isMoving = false;
                RequestDecision();
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_isInactive || _isMoving) return;

        _currentStepCount++;
        if (_currentStepCount >= maxStepCount)
        {
            _isInactive = true;
            return;
        }

        // Apply periodic penalty at regular intervals
        if (_currentStepCount % periodicStepInterval == 0)
        {
            AddReward(periodicStepPenalty);
        }

        AddReward(stepPenalty);

        int action = actions.DiscreteActions[0];
        if (action < 0 || action >= _dirs.Length) return;

        Vector2 target = (Vector2)transform.position + _dirs[action];

        if (CanMoveTo(target))
        {
            transform.position = target;
            _isMoving = true;
            _moveTimer = moveDelay;

            float newDist = DistanceToNearestExit();
            float delta = _prevDist - newDist;

            if (newDist < _bestDistance)
            {
                _bestDistance = newDist;
                _noProgressSteps = Mathf.Max(0, _noProgressSteps - 1);
            }

            if (delta > 0f)
            {
                AddReward(potentialCoeff * (delta / _maxDiag));
            }
            else
            {
                _noProgressSteps++;
                AddReward(tinyNoProgressPenalty);

                if (_noProgressSteps % noProgressPenaltyInterval == 0)
                {
                    AddReward(noProgressPenalty);
                }

                if (_noProgressSteps >= noProgressThreshold)
                {
                    _isInactive = true;
                    return;
                }
            }


            _prevDist = newDist;
        }
        else
        {
            AddReward(wallPenalty);
            _isMoving = true;
            _moveTimer = moveDelay;
        }
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
    {
        if (_behaviorParams.BehaviorType == BehaviorType.HeuristicOnly)
            return;

        bool allMasked = true;
        for (int i = 0; i < _dirs.Length; i++)
        {
            Vector2 t = (Vector2)transform.position + _dirs[i];
            if (!CanMoveTo(t))
            {
                mask.SetActionEnabled(0, i, false);
            }
            else
            {
                allMasked = false;
            }
        }

        if (allMasked)
        {
            mask.SetActionEnabled(0, 0, true); 
        }
    }



    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;
        da.Clear();

        if (plannedPath.Count >= 2)
        {
            Vector2Int cur = plannedPath.Dequeue();
            Vector2Int next = plannedPath.Peek();
            Vector2Int d = next - cur;

            int action = d == Vector2Int.up ? 0 :
                         d == Vector2Int.down ? 1 :
                         d == Vector2Int.left ? 2 :
                         d == Vector2Int.right ? 3 : 0;
            da[0] = action;
        }
        else
        {
            da[0] = 0;
        }
    }


    public void ComputePlannedPath()
    {
        Vector2 worldPos = transform.position;
        Vector2Int mazePos = mazeGenerator.WorldToMazeCoordinates(worldPos);

        List<Vector2Int> path = mazeGenerator.GetShortestPath(mazePos);
        plannedPath = new Queue<Vector2Int>(path);
    }



    public bool CanMoveTo(Vector2 pos)
    {
        Vector2 boxSize = new Vector2(0.8f, 0.8f);
        return Physics2D.OverlapBox(pos, boxSize, 0f, _mazeLayerMask) == null;
    }


    private Vector2 NearestExitWorld()
    {
        Vector2 me = (Vector2)transform.position;
        Vector2 best = me;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < _exitWorldPositions.Count; i++)
        {
            Vector2 e = _exitWorldPositions[i];
            float d = (e - me).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = e;
            }
        }
        return best;
    }

    private float DistanceToNearestExit()
    {
        return mazeGenerator.GetPathDistance(transform.position);
    }

    public void MarkAsInactive()
    {
        _isInactive = true;
    }
}
