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

    // ====== SOA �У��̶� 48��======
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

    // ====== 9x4 ����λ������======
    public const int MapWidth = 9;
    public const int MapHeight = 4;
    public const int MapCells = MapWidth * MapHeight; // 36

    // ���򣺸��� -> ����������-1 ��ʾ�գ�
    private readonly int[] _cellToDense = new int[MapCells];
    // ���򣺳������� -> ���ӣ�-1 ��ʾ��δ�����δ����λ�ã�
    private readonly int[] _denseToCell = new int[FixedCapacity];

    public PlayerUnitCollection()
    {
        ResetGrid();
    }

    // ========= ������أ��̶����� 48��=========
    public bool SetRows(IReadOnlyList<UnitRow> rows)
    {
        if (rows == null) { Clear(); return true; }
        if (rows.Count > Capacity) return false;

        var seen = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
            if (!seen.Add(rows[i].UnitId)) return false;

        Clear();           // ������/Ͱ
        ResetGrid();       // ��λ��

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

            // λ�ã��� Deployed ����λ��ϵͳ����ʼ��Ϊδ���ã�_denseToCell[i] = -1��
        }

        Count = rows.Count;
        return true;
    }

    public void Clear()
    {
        Count = 0;
        _unitIdToDenseIndex.Clear();
        for (int z = 0; z < _zoneBuckets.Length; z++) _zoneBuckets[z].Clear();
        // �����ݿɲ��壻Count=0 �󲻻ᱻ��ȡ
        ResetGrid();
    }

    // ========= ������ʾ˳���ϸ�棬�����������=========
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

    // ========= �Ѳ���λ�ã����� & ��ѯ���ϸ�=========

    /// <summary>
    /// �ϸ����á��Ѳ���Deployed������λ�� 9x4 �����е�λ�á�
    /// Ҫ��
    /// 1) placements ����Ŀ�� == ��ǰ Deployed ������
    /// 2) ÿ�� unitId ����������� Deployed��
    /// 3) ÿ�� (x,y) �����ڱ߽��ڡ��Ҳ��ظ���
    /// 4) unitId �����ظ���
    /// ͨ����Ḳ��֮ǰ�Ĳ���λ�ã�ʧ�ܷ��� false �Ҳ��Ķ���
    /// </summary>
    public bool SetDeployedPlacementsStrict(IReadOnlyList<(int unitId, int x, int y)> placements)
    {
        int deployedCount = _zoneBuckets[(int)UnitPlacementZone.Deployed].Count;
        if (deployedCount == 0) return placements == null || placements.Count == 0;
        if (placements == null || placements.Count != deployedCount) return false;
        if (deployedCount > MapCells) return false; // ���� 9x4 ����

        // У�鲢������ʱӳ��
        var tmpCellToDense = new int[MapCells];
        Fill(tmpCellToDense, -1);

        var tmpDenseToCell = new int[Count]; // ֻ��Ҫ�� Count
        Fill(tmpDenseToCell, -1);

        var seenDense = new HashSet<int>();
        var seenCell = new HashSet<int>();

        for (int i = 0; i < placements.Count; i++)
        {
            var (unitId, x, y) = placements[i];

            if (!TryCellToIndex(x, y, out int cell)) return false;
            if (!_unitIdToDenseIndex.TryGetValue(unitId, out int dense)) return false;
            if ((UnitPlacementZone)_unitZones[dense] != UnitPlacementZone.Deployed) return false;

            if (!seenDense.Add(dense)) return false;         // ��λ�ظ�
            if (!seenCell.Add(cell)) return false;           // �����ظ�

            tmpCellToDense[cell] = dense;
            tmpDenseToCell[dense] = cell;
        }

        // У�顰�Ƿ񸲸���ȫ�� Deployed��
        if (seenDense.Count != deployedCount) return false;

        // ����Ӧ��
        Array.Copy(tmpCellToDense, _cellToDense, MapCells);
        // ��������� dense->cell���ٸ�ֵ���������������꣩
        for (int i = 0; i < Count; i++) _denseToCell[i] = -1;
        for (int i = 0; i < Count; i++)
            if (tmpDenseToCell[i] >= 0) _denseToCell[i] = tmpDenseToCell[i];

        return true;
    }

    /// <summary>��ѯĳ�����ϵ� unitId����Ϊ�շ��� false��</summary>
    public bool TryGetUnitAt(int x, int y, out int unitId)
    {
        unitId = default;
        if (!TryCellToIndex(x, y, out int cell)) return false;
        int dense = _cellToDense[cell];
        if (dense < 0) return false;
        unitId = _unitIds[dense];
        return true;
    }

    /// <summary>��ѯĳ unitId �����ꣻ��δ�����δ����λ�÷��� false��</summary>
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

    /// <summary>ö�����С��Ѳ������Ѳ���λ�á��� (unitId, x, y)��</summary>
    public IEnumerable<(int unitId, int x, int y)> EnumerateDeployedWithPositions()
    {
        var bucket = _zoneBuckets[(int)UnitPlacementZone.Deployed];
        for (int k = 0; k < bucket.Count; k++)
        {
            int dense = bucket[k];
            int cell = _denseToCell[dense];
            if (cell < 0) continue; // δ����λ��
            yield return (_unitIds[dense], cell % MapWidth, cell / MapWidth);
        }
    }

    // ========= ��ѯ / ö�٣�ԭ�У�=========
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

    // ========= �ڲ������� & С���� =========
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
