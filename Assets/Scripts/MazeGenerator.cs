using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;
using UnityRandom = UnityEngine.Random;

public enum TileType : byte
{
    Floor = 0,
    Wall = 1,
    Exit = 2
}

[BurstCompile]
struct RenderJob : IJobParallelFor
{
    public int Width;
    [ReadOnly] public NativeArray<int> MazeGrid;
    [ReadOnly] public NativeArray<byte> ExitMask;
    public NativeArray<byte> Output;

    public void Execute(int index)
    {
        if (ExitMask[index] == 1)
            Output[index] = (byte)TileType.Exit;
        else
            Output[index] = (MazeGrid[index] == 1)
                ? (byte)TileType.Wall
                : (byte)TileType.Floor;
    }
}

[BurstCompile]
struct BFSDistanceJob : IJob
{
    public int Width;
    public int Height;

    [ReadOnly] public NativeArray<int> MazeGrid;
    public NativeArray<int> Distances;
    public NativeArray<Vector2Int> Predecessors;

    [ReadOnly] public NativeArray<int> InitialFrontier;
    public int FrontierLength;

    public void Execute()
    {
        var queueBuf = new NativeList<int>(Allocator.Temp);
        int head = 0, tail = FrontierLength;

        for (int i = 0; i < FrontierLength; i++)
            queueBuf.Add(InitialFrontier[i]);

        while (head < tail)
        {
            int idx = queueBuf[head++];
            int cx = idx % Width;
            int cy = idx / Width;
            int cd = Distances[idx];

            for (int d = 0; d < 4; d++)
            {
                int dx = 0, dy = 0;
                switch (d)
                {
                    case 0: dx = 1; break;
                    case 1: dx = -1; break;
                    case 2: dy = 1; break;
                    case 3: dy = -1; break;
                }

                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || nx >= Width || ny < 0 || ny >= Height) continue;

                int nIdx = nx + ny * Width;
                if (MazeGrid[nIdx] != 0) continue;
                if (Distances[nIdx] <= cd + 1) continue;

                Distances[nIdx] = cd + 1;
                Predecessors[nIdx] = new Vector2Int(cx, cy);
                queueBuf.Add(nIdx);
                tail++;
            }
        }

        queueBuf.Dispose();
    }
}

public class MazeGenerator : MonoBehaviour
{
    public static MazeGenerator Instance { get; private set; }

    public int width = 61;
    public int height = 61;
    public bool addOuterWall = true;

    public GameObject wallPrefab;
    public GameObject floorPrefab;
    public GameObject exitPrefab;

    public int numberOfExits = 3;

    [HideInInspector] public int[,] maze;
    private Vector2Int?[,] _predecessorMap;
    private int[,] _distanceMap;
    private readonly List<Vector2> _exitPositions = new List<Vector2>();

    private ObjectPool<GameObject> _wallPool, _floorPool, _exitPool;
    private readonly List<GameObject> _activeTiles = new List<GameObject>();

    private static readonly System.Random _sysRng = new System.Random();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _wallPool = new ObjectPool<GameObject>(
            () => Instantiate(wallPrefab, transform),
            go => go.SetActive(true),
            go => go.SetActive(false),
            go => Destroy(go),
            false, width * height / 2, width * height);

        _floorPool = new ObjectPool<GameObject>(
            () => Instantiate(floorPrefab, transform),
            go => go.SetActive(true),
            go => go.SetActive(false),
            go => Destroy(go),
            false, width * height / 2, width * height);

        _exitPool = new ObjectPool<GameObject>(
            () => Instantiate(exitPrefab, transform),
            go => go.SetActive(true),
            go => go.SetActive(false),
            go => Destroy(go),
            false, numberOfExits, numberOfExits * 2);
    }

    public List<Vector2> GetExitGridPositions() => _exitPositions;
    public int GetDistanceToNearestExit(Vector2 pos) => GetPathDistance(pos);
    public Vector2 GetWorldPosition(float mx, float my) => new Vector2(mx - width / 2f + 0.5f, my - height / 2f + 0.5f);
    public Vector2Int WorldToMazeCoordinates(Vector2 w) =>
        new Vector2Int(Mathf.RoundToInt(w.x + width / 2f - 0.5f), Mathf.RoundToInt(w.y + height / 2f - 0.5f));


    public void GenerateMazeEnvironment()
    {
        InitializeMaze();
        GenerateMaze(width / 2, height / 2);
        GenerateExits();
        ComputeDistanceMap();
        DrawMaze();
    }

    public List<Vector2Int> GetShortestPath(Vector2Int start)
    {
        var path = new List<Vector2Int>();
        if (!IsInBounds(start.x, start.y) || maze[start.x, start.y] != 0) return path;
        Vector2Int? cur = start;
        while (cur.HasValue)
        {
            path.Add(cur.Value);
            cur = _predecessorMap[cur.Value.x, cur.Value.y];
        }
        return path;
    }

    private void InitializeMaze()
    {
        maze = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                maze[x, y] = (x == 0 || x == width - 1 || y == 0 || y == height - 1) ? 0 : 1;
    }

    private void GenerateMaze(int x, int y)
    {
        maze[x, y] = 0;
        foreach (var (dx, dy) in ShuffleDirections())
        {
            int nx = x + dx * 2, ny = y + dy * 2;
            if (IsInBounds(nx, ny) && maze[nx, ny] == 1)
            {
                maze[x + dx, y + dy] = 0;
                GenerateMaze(nx, ny);
            }
        }
    }

    private void ComputeDistanceMap()
    {
        int count = width * height;
        var mazeArr = new NativeArray<int>(count, Allocator.TempJob);
        var distArr = new NativeArray<int>(count, Allocator.TempJob);
        var predArr = new NativeArray<Vector2Int>(count, Allocator.TempJob);

        var exits = _exitPositions;
        int fCount = exits.Count;
        var frontArr = new NativeArray<int>(fCount, Allocator.TempJob);
        for (int i = 0; i < fCount; i++)
        {
            int ex = (int)exits[i].x, ey = (int)exits[i].y;
            frontArr[i] = ex + ey * width;
        }

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int idx = x + y * width;
                mazeArr[idx] = maze[x, y];
                distArr[idx] = int.MaxValue;
                predArr[idx] = new Vector2Int(-1, -1);
            }

        foreach (int idx in frontArr)
            distArr[idx] = 0;

        var job = new BFSDistanceJob
        {
            Width = width,
            Height = height,
            MazeGrid = mazeArr,
            Distances = distArr,
            Predecessors = predArr,
            InitialFrontier = frontArr,
            FrontierLength = fCount
        };
        job.Schedule().Complete();

        _distanceMap = new int[width, height];
        _predecessorMap = new Vector2Int?[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                int idx = x + y * width;
                _distanceMap[x, y] = distArr[idx];
                var p = predArr[idx];
                _predecessorMap[x, y] = p.x >= 0 ? p : (Vector2Int?)null;
            }

        mazeArr.Dispose();
        distArr.Dispose();
        predArr.Dispose();
        frontArr.Dispose();
    }

    private void GenerateExits()
    {
        _exitPositions.Clear();
        int marginX = Mathf.FloorToInt(width * 0.125f);
        int marginY = Mathf.FloorToInt(height * 0.125f);
        int minX = marginX;
        int maxX = width - 1 - marginX;
        int minY = marginY;
        int maxY = height - 1 - marginY;

        for (int i = 0; i < numberOfExits; i++)
        {
            int ex, ey;
            if (UnityRandom.value > 0.5f)
            {
                ex = UnityRandom.Range(minX, maxX + 1);
                ey = UnityRandom.value > 0.5f ? height - 1 : 0;
            }
            else
            {
                ey = UnityRandom.Range(minY, maxY + 1);
                ex = UnityRandom.value > 0.5f ? width - 1 : 0;
            }

            maze[ex, ey] = 0;
            CarvePathFromExit(ex, ey);
            _exitPositions.Add(new Vector2(ex, ey));
        }
    }


    private void CarvePathFromExit(int ex, int ey)
    {
        int dx = 0, dy = 0;
        if (ey == 0) dy = 1;
        else if (ey == height - 1) dy = -1;
        else if (ex == 0) dx = 1;
        else dx = -1;

        int cx = ex + dx;
        int cy = ey + dy;

        while (IsInBounds(cx, cy) && maze[cx, cy] == 1)
        {
            maze[cx, cy] = 0;
            cx += dx;
            cy += dy;
        }
    }


    public void DrawMaze()
    {
        foreach (var go in _activeTiles)
        {
            if (go.CompareTag("Wall")) _wallPool.Release(go);
            else if (go.CompareTag("Exit")) _exitPool.Release(go);
            else _floorPool.Release(go);
        }
        _activeTiles.Clear();

        int count = width * height;
        var gridArr = new NativeArray<int>(count, Allocator.TempJob);
        var exitArr = new NativeArray<byte>(count, Allocator.TempJob);
        var outArr = new NativeArray<byte>(count, Allocator.TempJob);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = x + y * width;
                gridArr[idx] = maze[x, y];
                exitArr[idx] = (byte)(_exitPositions.Contains(new Vector2(x, y)) ? 1 : 0);
            }

        var job = new RenderJob
        {
            Width = width,
            MazeGrid = gridArr,
            ExitMask = exitArr,
            Output = outArr
        };
        job.Schedule(count, 64).Complete();

        for (int i = 0; i < count; i++)
        {
            int x = i % width, y = i / width;
            var pos = new Vector3(x - width / 2f + 0.5f, y - height / 2f + 0.5f, 0);
            GameObject tile = outArr[i] switch
            {
                (byte)TileType.Wall => _wallPool.Get(),
                (byte)TileType.Exit => _exitPool.Get(),
                _ => _floorPool.Get()
            };
            tile.transform.localPosition = pos;
            _activeTiles.Add(tile);
        }

        gridArr.Dispose();
        exitArr.Dispose();
        outArr.Dispose();

        if (addOuterWall) DrawOuterWall();
    }

    private void DrawOuterWall()
    {
        float left = -width / 2f - 0.5f;
        float right = width / 2f + 0.5f;
        float bottom = -height / 2f - 0.5f;
        float top = height / 2f + 0.5f;

        for (int x = -1; x <= width; x++)
        {
            float wx = x - width / 2f + 0.5f;
            var w1 = _wallPool.Get(); w1.transform.localPosition = new Vector3(wx, bottom, 0);
            var w2 = _wallPool.Get(); w2.transform.localPosition = new Vector3(wx, top, 0);
            _activeTiles.Add(w1); _activeTiles.Add(w2);
        }
        for (int y = 0; y < height; y++)
        {
            float wy = y - height / 2f + 0.5f;
            var w1 = _wallPool.Get(); w1.transform.localPosition = new Vector3(left, wy, 0);
            var w2 = _wallPool.Get(); w2.transform.localPosition = new Vector3(right, wy, 0);
            _activeTiles.Add(w1); _activeTiles.Add(w2);
        }
    }

    public bool IsInBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    private (int dx, int dy)[] ShuffleDirections()
    {
        var arr = new (int, int)[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = _sysRng.Next(i + 1);
            var tmp = arr[i]; arr[i] = arr[j]; arr[j] = tmp;
        }
        return arr;
    }

    public int GetPathDistance(Vector2 wp)
    {
        if (_distanceMap == null)
            ComputeDistanceMap();

        int mx = Mathf.RoundToInt(wp.x + width / 2f - 0.5f);
        int my = Mathf.RoundToInt(wp.y + height / 2f - 0.5f);
        return IsInBounds(mx, my) ? _distanceMap[mx, my] : int.MaxValue;
    }

    public bool IsAtExit(Vector2 wp, int radius = 1)
    {
        int mx = Mathf.RoundToInt(wp.x + width / 2f - 0.5f);
        int my = Mathf.RoundToInt(wp.y + height / 2f - 0.5f);
        int r2 = radius * radius;
        foreach (var e in _exitPositions)
        {
            int dx = mx - (int)e.x, dy = my - (int)e.y;
            if (dx * dx + dy * dy <= r2) return true;
        }
        return false;
    }

    public List<Vector2Int> GetFreeCells()
    {
        var freeCells = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (maze[x, y] == 0)
                    freeCells.Add(new Vector2Int(x, y));
        return freeCells;
    }

    public bool IsExitOnBorder(int x, int y)
        => maze[x - 1, y] == 1 || maze[x + 1, y] == 1 || maze[x, y - 1] == 1 || maze[x, y + 1] == 1;
}
