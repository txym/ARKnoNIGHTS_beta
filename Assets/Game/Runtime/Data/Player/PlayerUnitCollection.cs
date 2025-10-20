using System;
using System.Collections.Generic;

public enum UnitPlacementZone : byte { Staging, Deployed, Overflow, Shop }

public readonly struct UnitRow
{
    public readonly int UnitId;
    public readonly int UnitTypeId;
    public readonly UnitPlacementZone Zone;
    public UnitRow(int unitId, int unitTypeId, UnitPlacementZone zone)
    { UnitId = unitId; UnitTypeId = unitTypeId; Zone = zone; }
}

public sealed class PlayerUnitCollection
{
    public const int FixedCapacity = 48;

    // ====== SOA 列（固定 48）======
    private readonly int[] _unitIds = new int[FixedCapacity];
    private readonly int[] _unitTypeIds = new int[FixedCapacity];
    private readonly byte[] _unitZones = new byte[FixedCapacity];
    private readonly int[] _indexInZoneBucket = new int[FixedCapacity];

    public int Count { get; private set; }
    public int Capacity => FixedCapacity;

    private readonly Dictionary<int, int> _unitIdToDenseIndex = new(FixedCapacity);

    private readonly List<int>[] _zoneBuckets =
    {
        new List<int>(FixedCapacity), // Staging
        new List<int>(FixedCapacity), // Deployed
        new List<int>(FixedCapacity), // Overflow
        new List<int>(FixedCapacity), // Shop
    };

    // ====== 9x4 网格位置索引======
    public const int MapWidth = 9;
    public const int MapHeight = 4;
    public const int MapCells = MapWidth * MapHeight; // 36

    // 反向：格子 -> 稠密索引（-1 表示空）
    private readonly int[] _cellToDense = new int[MapCells];
    // 正向：稠密索引 -> 格子（-1 表示：未部署或未布置位置）
    private readonly int[] _denseToCell = new int[FixedCapacity];

    public PlayerUnitCollection()
    {
        ResetGrid();
    }

    // ========= 整表加载（固定容量 48）=========
    public bool SetRows(IReadOnlyList<UnitRow> rows)
    {
        if (rows == null) { Clear(); return true; }
        if (rows.Count > Capacity) return false;

        var seen = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
            if (!seen.Add(rows[i].UnitId)) return false;

        Clear();           // 清索引/桶
        ResetGrid();       // 清位置

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            _unitIds[i] = r.UnitId;
            _unitTypeIds[i] = r.UnitTypeId;
            _unitZones[i] = (byte)r.Zone;

            _unitIdToDenseIndex[r.UnitId] = i;

            var bucket = _zoneBuckets[(int)r.Zone];
            bucket.Add(i);
            _indexInZoneBucket[i] = bucket.Count - 1;

            // 位置：仅 Deployed 参与位置系统；初始均为未布置（_denseToCell[i] = -1）
        }

        Count = rows.Count;
        return true;
    }

    public void Clear()
    {
        Count = 0;
        _unitIdToDenseIndex.Clear();
        for (int z = 0; z < _zoneBuckets.Length; z++) _zoneBuckets[z].Clear();
        // 列数据可不清；Count=0 后不会被读取
        ResetGrid();
    }

    // ========= 区内显示顺序（严格版，仅保留这个）=========
    public bool ApplyZoneOrder(UnitPlacementZone zone, IReadOnlyList<int> unitIdsInOrder)
    {
        var bucket = _zoneBuckets[(int)zone];
        int n = bucket.Count;
        if (n == 0) return true;
        if (unitIdsInOrder == null || unitIdsInOrder.Count != n) return false;

        var newOrder = new int[n];
        var seenDense = new HashSet<int>();

        for (int i = 0; i < n; i++)
        {
            int uid = unitIdsInOrder[i];
            if (!_unitIdToDenseIndex.TryGetValue(uid, out int dense)) return false;
            if ((UnitPlacementZone)_unitZones[dense] != zone) return false;
            if (!seenDense.Add(dense)) return false;
            newOrder[i] = dense;
        }

        for (int i = 0; i < n; i++)
        {
            int dense = newOrder[i];
            bucket[i] = dense;
            _indexInZoneBucket[dense] = i;
        }
        return true;
    }

    // ========= 已部署位置：设置 & 查询（严格）=========

    /// <summary>
    /// 严格设置“已部署（Deployed）”单位在 9x4 网格中的位置。
    /// 要求：
    /// 1) placements 的条目数 == 当前 Deployed 数量；
    /// 2) 每个 unitId 必须存在且在 Deployed；
    /// 3) 每个 (x,y) 必须在边界内、且不重复；
    /// 4) unitId 不得重复；
    /// 通过后会覆盖之前的部署位置；失败返回 false 且不改动。
    /// </summary>
    public bool SetDeployedPlacementsStrict(IReadOnlyList<(int unitId, int x, int y)> placements)
    {
        int deployedCount = _zoneBuckets[(int)UnitPlacementZone.Deployed].Count;
        if (deployedCount == 0) return placements == null || placements.Count == 0;
        if (placements == null || placements.Count != deployedCount) return false;
        if (deployedCount > MapCells) return false; // 超过 9x4 容量

        // 校验并构建临时映射
        var tmpCellToDense = new int[MapCells];
        Fill(tmpCellToDense, -1);

        var tmpDenseToCell = new int[Count]; // 只需要到 Count
        Fill(tmpDenseToCell, -1);

        var seenDense = new HashSet<int>();
        var seenCell = new HashSet<int>();

        for (int i = 0; i < placements.Count; i++)
        {
            var (unitId, x, y) = placements[i];

            if (!TryCellToIndex(x, y, out int cell)) return false;
            if (!_unitIdToDenseIndex.TryGetValue(unitId, out int dense)) return false;
            if ((UnitPlacementZone)_unitZones[dense] != UnitPlacementZone.Deployed) return false;

            if (!seenDense.Add(dense)) return false;         // 单位重复
            if (!seenCell.Add(cell)) return false;           // 格子重复

            tmpCellToDense[cell] = dense;
            tmpDenseToCell[dense] = cell;
        }

        // 校验“是否覆盖了全部 Deployed”
        if (seenDense.Count != deployedCount) return false;

        // 覆盖应用
        Array.Copy(tmpCellToDense, _cellToDense, MapCells);
        // 先清空现有 dense->cell，再赋值（避免遗留旧坐标）
        for (int i = 0; i < Count; i++) _denseToCell[i] = -1;
        for (int i = 0; i < Count; i++)
            if (tmpDenseToCell[i] >= 0) _denseToCell[i] = tmpDenseToCell[i];

        return true;
    }

    /// <summary>查询某坐标上的 unitId；若为空返回 false。</summary>
    public bool TryGetUnitAt(int x, int y, out int unitId)
    {
        unitId = default;
        if (!TryCellToIndex(x, y, out int cell)) return false;
        int dense = _cellToDense[cell];
        if (dense < 0) return false;
        unitId = _unitIds[dense];
        return true;
    }

    /// <summary>查询某 unitId 的坐标；若未部署或未布置位置返回 false。</summary>
    public bool TryGetPosition(int unitId, out int x, out int y)
    {
        x = y = default;
        if (!_unitIdToDenseIndex.TryGetValue(unitId, out int dense)) return false;
        if ((UnitPlacementZone)_unitZones[dense] != UnitPlacementZone.Deployed) return false;

        int cell = _denseToCell[dense];
        if (cell < 0) return false;

        x = cell % MapWidth;
        y = cell / MapWidth;
        return true;
    }

    /// <summary>枚举所有“已部署且已布置位置”的 (unitId, x, y)。</summary>
    public IEnumerable<(int unitId, int x, int y)> EnumerateDeployedWithPositions()
    {
        var bucket = _zoneBuckets[(int)UnitPlacementZone.Deployed];
        for (int k = 0; k < bucket.Count; k++)
        {
            int dense = bucket[k];
            int cell = _denseToCell[dense];
            if (cell < 0) continue; // 未布置位置
            yield return (_unitIds[dense], cell % MapWidth, cell / MapWidth);
        }
    }

    // ========= 查询 / 枚举（原有）=========
    public bool ContainsUnit(int unitId) => _unitIdToDenseIndex.ContainsKey(unitId);

    public bool TryGetUnit(int unitId, out int unitTypeId, out UnitPlacementZone zone)
    {
        if (_unitIdToDenseIndex.TryGetValue(unitId, out int i))
        {
            unitTypeId = _unitTypeIds[i];
            zone = (UnitPlacementZone)_unitZones[i];
            return true;
        }
        unitTypeId = default; zone = default; return false;
    }

    public int GetUnitCountInZone(UnitPlacementZone zone) => _zoneBuckets[(int)zone].Count;

    public IEnumerable<int> EnumerateUnitIdsInZone(UnitPlacementZone zone)
    {
        var bucket = _zoneBuckets[(int)zone];
        for (int k = 0; k < bucket.Count; k++)
            yield return _unitIds[bucket[k]];
    }

    public IEnumerable<UnitRow> EnumerateAllRows()
    {
        for (int i = 0; i < Count; i++)
            yield return new UnitRow(_unitIds[i], _unitTypeIds[i], (UnitPlacementZone)_unitZones[i]);
    }

    // ========= 内部：网格 & 小工具 =========
    private void ResetGrid()
    {
        Fill(_cellToDense, -1);
        Fill(_denseToCell, -1);
    }

    private static bool TryCellToIndex(int x, int y, out int cell)
    {
        if ((uint)x >= MapWidth || (uint)y >= MapHeight) { cell = -1; return false; }
        cell = y * MapWidth + x;
        return true;
    }

    private static void Fill(int[] arr, int value)
    {
        for (int i = 0; i < arr.Length; i++) arr[i] = value;
    }
}
