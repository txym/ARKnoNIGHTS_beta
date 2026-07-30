using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Infrastructure;
using UnityEngine;

namespace ArknoNights.Player
{
    public enum PlayerUnitZone { Staging, Deployed, Overflow, Shop }

    public enum PlayerOperationCode
    {
        Success,
        UnitNotFound,
        UnitNotStaging,
        UnitNotDeployed,
        TypeUnknown,
        InsufficientDeploymentCost,
        CoordinateOutOfBounds,
        CoordinateIsGate,
        CoordinateOccupied,
        StagingCapacityExceeded,
        UnitIdDuplicate,
        UnitCapacityExceeded
    }

    public readonly struct LocalFormationCoordinate : IEquatable<LocalFormationCoordinate>
    {
        public const int Width = 9;
        public const int Height = 4;

        public LocalFormationCoordinate(int x, int y)
        {
            if (!IsWithinBounds(x, y)) throw new ArgumentOutOfRangeException(nameof(x), "Local formation coordinates must be within 1..9 by 1..4.");
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public static bool IsWithinBounds(int x, int y) => x >= 1 && x <= Width && y >= 1 && y <= Height;
        public static bool IsDeployable(int x, int y) => IsWithinBounds(x, y) && !(x == 5 && y == 1);
        public static bool TryCreate(int x, int y, out LocalFormationCoordinate coordinate)
        {
            if (!IsWithinBounds(x, y)) { coordinate = default(LocalFormationCoordinate); return false; }
            coordinate = new LocalFormationCoordinate(x, y);
            return true;
        }

        public bool Equals(LocalFormationCoordinate other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is LocalFormationCoordinate other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => X + "," + Y;
    }

    public readonly struct PlayerBuffSnapshot : IEquatable<PlayerBuffSnapshot>
    {
        public PlayerBuffSnapshot(string id, string rawPayload)
        {
            Id = id ?? string.Empty;
            RawPayload = rawPayload ?? string.Empty;
        }

        public string Id { get; }
        public string RawPayload { get; }
        public bool Equals(PlayerBuffSnapshot other) => string.Equals(Id, other.Id, StringComparison.Ordinal) && string.Equals(RawPayload, other.RawPayload, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is PlayerBuffSnapshot other && Equals(other);
        public override int GetHashCode() => ((Id ?? string.Empty).GetHashCode() * 397) ^ (RawPayload ?? string.Empty).GetHashCode();
    }

    public sealed class PlayerUnitSnapshot
    {
        internal PlayerUnitSnapshot(string unitId, string typeId, PlayerUnitZone zone, int eliteLevel, IEnumerable<PlayerBuffSnapshot> buffs, LocalFormationCoordinate? formation)
        {
            UnitId = unitId;
            TypeId = typeId;
            Zone = zone;
            EliteLevel = eliteLevel;
            Buffs = new ReadOnlyCollection<PlayerBuffSnapshot>((buffs ?? Enumerable.Empty<PlayerBuffSnapshot>()).ToArray());
            Formation = formation;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public PlayerUnitZone Zone { get; }
        public int EliteLevel { get; }
        public IReadOnlyList<PlayerBuffSnapshot> Buffs { get; }
        public LocalFormationCoordinate? Formation { get; }

        public static PlayerUnitSnapshot CreateProjection(
            string unitId,
            string typeId,
            PlayerUnitZone zone,
            int eliteLevel,
            IEnumerable<PlayerBuffSnapshot> buffs,
            LocalFormationCoordinate? formation)
        {
            return new PlayerUnitSnapshot(
                unitId,
                typeId,
                zone,
                eliteLevel,
                buffs,
                formation);
        }
    }

    public sealed class StagingStackSnapshot
    {
        internal StagingStackSnapshot(string typeId, int deploymentCost, string portraitResourcePath, int rarity, int eliteLevel, IEnumerable<PlayerBuffSnapshot> buffs, IEnumerable<string> unitIds)
        {
            TypeId = typeId;
            DeploymentCost = deploymentCost;
            PortraitResourcePath = portraitResourcePath ?? string.Empty;
            Rarity = rarity;
            EliteLevel = eliteLevel;
            Buffs = new ReadOnlyCollection<PlayerBuffSnapshot>((buffs ?? Enumerable.Empty<PlayerBuffSnapshot>()).ToArray());
            UnitIds = new ReadOnlyCollection<string>((unitIds ?? Enumerable.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }

        public string TypeId { get; }
        public int DeploymentCost { get; }
        /// <summary>Player-safe portrait path resolved by the authoritative type catalog for read-only UI projection.</summary>
        public string PortraitResourcePath { get; }
        /// <summary>Validated Player-safe catalog rarity; it is never inferred from elite level.</summary>
        public int Rarity { get; }
        public int EliteLevel { get; }
        public IReadOnlyList<PlayerBuffSnapshot> Buffs { get; }
        public IReadOnlyList<string> UnitIds { get; }
        public int Count => UnitIds.Count;

        public static StagingStackSnapshot CreateProjection(
            string typeId,
            int deploymentCost,
            string portraitResourcePath,
            int rarity,
            int eliteLevel,
            IEnumerable<PlayerBuffSnapshot> buffs,
            IEnumerable<string> unitIds)
        {
            return new StagingStackSnapshot(
                typeId,
                deploymentCost,
                portraitResourcePath,
                rarity,
                eliteLevel,
                buffs,
                unitIds);
        }
    }

    public sealed class PlayerStateSnapshot
    {
        internal PlayerStateSnapshot(string playerId, int deploymentCost, long version, IEnumerable<PlayerUnitSnapshot> units, IEnumerable<StagingStackSnapshot> stagingSlots)
        {
            PlayerId = playerId;
            DeploymentCost = deploymentCost;
            Version = version;
            Units = new ReadOnlyCollection<PlayerUnitSnapshot>((units ?? Enumerable.Empty<PlayerUnitSnapshot>()).OrderBy(unit => unit.UnitId, StringComparer.Ordinal).ToArray());
            StagingSlots = new ReadOnlyCollection<StagingStackSnapshot>((stagingSlots ?? Enumerable.Empty<StagingStackSnapshot>()).ToArray());
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string PlayerId { get; }
        public int DeploymentCost { get; }
        public long Version { get; }
        public IReadOnlyList<PlayerUnitSnapshot> Units { get; }
        public IReadOnlyList<StagingStackSnapshot> StagingSlots { get; }
        public string CanonicalSummary { get; }

        public static PlayerStateSnapshot CreateProjection(
            string playerId,
            int deploymentCost,
            long version,
            IEnumerable<PlayerUnitSnapshot> units,
            IEnumerable<StagingStackSnapshot> stagingSlots)
        {
            return new PlayerStateSnapshot(
                playerId,
                deploymentCost,
                version,
                units,
                stagingSlots);
        }

        private string BuildCanonicalSummary()
        {
            var builder = new StringBuilder();
            builder.Append(PlayerId).Append('|').Append(DeploymentCost).Append('|').Append(Version);
            foreach (var unit in Units)
            {
                builder.Append("|U:").Append(unit.UnitId).Append(',').Append(unit.TypeId).Append(',').Append((int)unit.Zone).Append(',').Append(unit.EliteLevel);
                if (unit.Formation.HasValue) builder.Append(',').Append(unit.Formation.Value.X).Append(',').Append(unit.Formation.Value.Y);
                foreach (var buff in unit.Buffs) builder.Append("|B:").Append(buff.Id).Append(',').Append(buff.RawPayload);
            }
            foreach (var slot in StagingSlots)
                builder.Append("|S:").Append(slot.TypeId).Append(',').Append(slot.DeploymentCost).Append(',').Append(slot.EliteLevel).Append(',').Append(string.Join(",", slot.UnitIds));
            return builder.ToString();
        }
    }

    public sealed class PlayerOperationResult
    {
        internal PlayerOperationResult(PlayerOperationCode code, PlayerStateSnapshot snapshot, IEnumerable<string> removedUnitIds)
        {
            Code = code;
            Snapshot = snapshot;
            RemovedUnitIds = new ReadOnlyCollection<string>((removedUnitIds ?? Enumerable.Empty<string>()).ToArray());
        }

        public bool Success => Code == PlayerOperationCode.Success;
        public PlayerOperationCode Code { get; }
        public PlayerStateSnapshot Snapshot { get; }
        public IReadOnlyList<string> RemovedUnitIds { get; }
    }

    public sealed class PlayerStateValidationError
    {
        internal PlayerStateValidationError(string code, string message) { Code = code; Message = message; }
        public string Code { get; }
        public string Message { get; }
        public override string ToString() => Code + ": " + Message;
    }

    public sealed class PlayerStateLoadResult
    {
        internal PlayerStateLoadResult(PlayerState state, IReadOnlyList<PlayerStateValidationError> errors) { State = state; Errors = errors; }
        public bool Success => State != null && Errors.Count == 0;
        public PlayerState State { get; }
        public IReadOnlyList<PlayerStateValidationError> Errors { get; }
    }

    public sealed class PlayerState
    {
        public const int InitialDeploymentCost = 99;
        public const int FixedCapacity = PlayerUnitCollection.FixedCapacity;
        public const int StagingSlotCapacity = 13;

        private readonly UnitCatalog catalog;
        private readonly Dictionary<string, PlayerUnitData> unitsById;
        // Reuses the legacy fixed-capacity zone buckets as an internal derived projection.
        // The string-keyed instance records below remain the sole authority for UI state.
        private readonly PlayerUnitCollection unitCollection = new PlayerUnitCollection();
        private readonly string playerId;
        private int deploymentCost;
        private long version;

        internal PlayerState(string playerId, int deploymentCost, UnitCatalog catalog, IEnumerable<PlayerUnitData> units)
        {
            this.playerId = playerId;
            this.deploymentCost = deploymentCost;
            this.catalog = catalog;
            unitsById = units.ToDictionary(unit => unit.UnitId, StringComparer.Ordinal);
            SynchronizeCollectionProjection();
        }

        public event Action<PlayerStateSnapshot> Changed;
        public string PlayerId => playerId;
        public int DeploymentCost => deploymentCost;
        public long Version => version;
        public PlayerStateSnapshot Snapshot => CreateSnapshot();

        public IReadOnlyList<PlayerUnitSnapshot> GetUnits(PlayerUnitZone zone) => new ReadOnlyCollection<PlayerUnitSnapshot>(unitsById.Values.Where(unit => unit.Zone == zone).OrderBy(unit => unit.UnitId, StringComparer.Ordinal).Select(ToSnapshot).ToArray());

        public PlayerOperationResult TryDeploy(string unitId, int x, int y)
        {
            if (!unitsById.TryGetValue(unitId ?? string.Empty, out var unit)) return Result(PlayerOperationCode.UnitNotFound);
            if (unit.Zone != PlayerUnitZone.Staging) return Result(PlayerOperationCode.UnitNotStaging);
            if (!LocalFormationCoordinate.TryCreate(x, y, out var formation)) return Result(PlayerOperationCode.CoordinateOutOfBounds);
            if (!LocalFormationCoordinate.IsDeployable(x, y)) return Result(PlayerOperationCode.CoordinateIsGate);
            if (unitsById.Values.Any(candidate => candidate.Zone == PlayerUnitZone.Deployed && candidate.Formation.HasValue && candidate.Formation.Value.Equals(formation))) return Result(PlayerOperationCode.CoordinateOccupied);
            if (!catalog.TryGet(
                    unit.TypeId,
                    unit.EliteLevel,
                    out var type))
                return Result(PlayerOperationCode.TypeUnknown);
            if (deploymentCost < type.DeploymentCost) return Result(PlayerOperationCode.InsufficientDeploymentCost);

            deploymentCost -= type.DeploymentCost;
            unit.Zone = PlayerUnitZone.Deployed;
            unit.Formation = formation;
            NotifyChanged();
            return Result(PlayerOperationCode.Success);
        }

        public PlayerOperationResult TryReplaceDeployed(
            string stagingUnitId,
            string expectedDeployedUnitId,
            int x,
            int y)
        {
            if (!unitsById.TryGetValue(
                    stagingUnitId ?? string.Empty,
                    out var incoming))
                return Result(PlayerOperationCode.UnitNotFound);
            if (incoming.Zone != PlayerUnitZone.Staging)
                return Result(PlayerOperationCode.UnitNotStaging);
            if (!LocalFormationCoordinate.TryCreate(
                    x,
                    y,
                    out var target))
                return Result(PlayerOperationCode.CoordinateOutOfBounds);
            if (!LocalFormationCoordinate.IsDeployable(x, y))
                return Result(PlayerOperationCode.CoordinateIsGate);
            var outgoing = unitsById.Values.FirstOrDefault(candidate =>
                candidate.Zone == PlayerUnitZone.Deployed
                && candidate.Formation.HasValue
                && candidate.Formation.Value.Equals(target));
            if (outgoing == null
                || !string.Equals(
                    outgoing.UnitId,
                    expectedDeployedUnitId,
                    StringComparison.Ordinal))
                return Result(PlayerOperationCode.CoordinateOccupied);
            if (!catalog.TryGet(
                    incoming.TypeId,
                    incoming.EliteLevel,
                    out var incomingType)
                || !catalog.TryGet(
                    outgoing.TypeId,
                    outgoing.EliteLevel,
                    out var outgoingType))
                return Result(PlayerOperationCode.TypeUnknown);
            var nextDeploymentCost =
                (long)deploymentCost
                + outgoingType.DeploymentCost
                - incomingType.DeploymentCost;
            if (nextDeploymentCost < 0)
                return Result(
                    PlayerOperationCode
                        .InsufficientDeploymentCost);

            var prospective = unitsById.Values.Select(candidate =>
            {
                if (candidate == incoming)
                    return candidate.WithZone(
                        PlayerUnitZone.Deployed,
                        target);
                if (candidate == outgoing)
                    return candidate.WithZone(
                        PlayerUnitZone.Staging,
                        null);
                return candidate;
            }).ToArray();
            if (CountStagingSlots(prospective, catalog)
                > StagingSlotCapacity)
                return Result(
                    PlayerOperationCode
                        .StagingCapacityExceeded);

            incoming.Zone = PlayerUnitZone.Deployed;
            incoming.Formation = target;
            outgoing.Zone = PlayerUnitZone.Staging;
            outgoing.Formation = null;
            deploymentCost = checked((int)nextDeploymentCost);
            NotifyChanged();
            return Result(PlayerOperationCode.Success);
        }

        /// <summary>
        /// Repositions one deployed unit within the local formation. An occupied target atomically swaps
        /// the two friendly deployed units; the source cell is a successful no-op. Deployment cost is not involved.
        /// </summary>
        public PlayerOperationResult TryRelocateDeployed(string unitId, int x, int y)
        {
            if (!unitsById.TryGetValue(unitId ?? string.Empty, out var unit)) return Result(PlayerOperationCode.UnitNotFound);
            if (unit.Zone != PlayerUnitZone.Deployed || !unit.Formation.HasValue) return Result(PlayerOperationCode.UnitNotDeployed);
            if (!LocalFormationCoordinate.TryCreate(x, y, out var target)) return Result(PlayerOperationCode.CoordinateOutOfBounds);
            if (!LocalFormationCoordinate.IsDeployable(x, y)) return Result(PlayerOperationCode.CoordinateIsGate);

            var origin = unit.Formation.Value;
            if (origin.Equals(target)) return Result(PlayerOperationCode.Success);

            var occupant = unitsById.Values.FirstOrDefault(candidate =>
                candidate.Zone == PlayerUnitZone.Deployed &&
                candidate.Formation.HasValue &&
                candidate.Formation.Value.Equals(target));

            unit.Formation = target;
            if (occupant != null) occupant.Formation = origin;
            NotifyChanged();
            return Result(PlayerOperationCode.Success);
        }

        public PlayerOperationResult TryRetreat(string unitId)
        {
            if (!unitsById.TryGetValue(unitId ?? string.Empty, out var unit)) return Result(PlayerOperationCode.UnitNotFound);
            if (unit.Zone != PlayerUnitZone.Deployed) return Result(PlayerOperationCode.UnitNotDeployed);
            if (!catalog.TryGet(
                    unit.TypeId,
                    unit.EliteLevel,
                    out var type))
                return Result(PlayerOperationCode.TypeUnknown);

            var prospective = unitsById.Values.Select(candidate => candidate == unit ? candidate.WithZone(PlayerUnitZone.Staging, null) : candidate).ToArray();
            if (CountStagingSlots(prospective, catalog) > StagingSlotCapacity) return Result(PlayerOperationCode.StagingCapacityExceeded);

            unit.Zone = PlayerUnitZone.Staging;
            unit.Formation = null;
            deploymentCost += type.DeploymentCost;
            NotifyChanged();
            return Result(PlayerOperationCode.Success);
        }

        public PlayerOperationResult RemoveOverflowUnits()
        {
            var ids = unitsById.Values.Where(unit => unit.Zone == PlayerUnitZone.Overflow).Select(unit => unit.UnitId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (ids.Length == 0) return Result(PlayerOperationCode.Success, ids);
            foreach (var id in ids) unitsById.Remove(id);
            NotifyChanged();
            return Result(PlayerOperationCode.Success, ids);
        }

        /// <summary>
        /// Adds one shop purchase without exposing the mutable unit collection to callers. The new unit follows the
        /// same strict staging-stack rule as loading: it enters staging when that projection has room, otherwise
        /// Overflow. It never spends deployment cost or deploys the unit.
        /// </summary>
        public PlayerOperationResult TryAddPurchasedUnit(string unitId, string typeId)
        {
            if (string.IsNullOrWhiteSpace(unitId) || unitsById.ContainsKey(unitId)) return Result(PlayerOperationCode.UnitIdDuplicate);
            if (!catalog.TryGet(typeId ?? string.Empty, out var type)) return Result(PlayerOperationCode.TypeUnknown);
            if (unitsById.Count >= FixedCapacity) return Result(PlayerOperationCode.UnitCapacityExceeded);

            var candidate = new PlayerUnitData(unitId, typeId, PlayerUnitZone.Staging, type.InitialEliteLevel, Array.Empty<PlayerBuffSnapshot>(), null);
            var prospective = unitsById.Values.Concat(new[] { candidate }).ToArray();
            if (CountStagingSlots(prospective, catalog) > StagingSlotCapacity)
                candidate.Zone = PlayerUnitZone.Overflow;

            unitsById.Add(candidate.UnitId, candidate);
            NotifyChanged();
            return Result(PlayerOperationCode.Success);
        }

        private PlayerOperationResult Result(PlayerOperationCode code, IEnumerable<string> removedUnitIds = null) => new PlayerOperationResult(code, CreateSnapshot(), removedUnitIds);

        private void NotifyChanged()
        {
            SynchronizeCollectionProjection();
            version++;
            Changed?.Invoke(CreateSnapshot());
        }

        private void SynchronizeCollectionProjection()
        {
            var rows = unitsById.Values
                .OrderBy(unit => unit.UnitId, StringComparer.Ordinal)
                .Select((unit, index) => new UnitRow(index + 1, ParseLegacyTypeId(unit.TypeId), (UnitPlacementZone)unit.Zone))
                .ToArray();
            if (!unitCollection.SetRows(rows)) throw new InvalidOperationException("PlayerUnitCollection projection is invalid.");
        }

        private static int ParseLegacyTypeId(string typeId) => int.TryParse(typeId, out var value) ? value : 0;

        private PlayerStateSnapshot CreateSnapshot()
        {
            var units = unitsById.Values.Select(ToSnapshot).ToArray();
            return new PlayerStateSnapshot(playerId, deploymentCost, version, units, CreateStagingSlots(unitsById.Values, catalog));
        }

        private static PlayerUnitSnapshot ToSnapshot(PlayerUnitData unit) => new PlayerUnitSnapshot(unit.UnitId, unit.TypeId, unit.Zone, unit.EliteLevel, unit.Buffs, unit.Formation);

        internal static int CountStagingSlots(IEnumerable<PlayerUnitData> units, UnitCatalog catalog) => CreateStagingSlots(units, catalog).Count;

        private static IReadOnlyList<StagingStackSnapshot> CreateStagingSlots(IEnumerable<PlayerUnitData> units, UnitCatalog catalog)
        {
            var groups = new List<List<PlayerUnitData>>();
            foreach (var unit in (units ?? Enumerable.Empty<PlayerUnitData>()).Where(item => item.Zone == PlayerUnitZone.Staging).OrderBy(item => item.UnitId, StringComparer.Ordinal))
            {
                var group = groups.FirstOrDefault(candidate => CanStack(candidate[0], unit));
                if (group == null) { group = new List<PlayerUnitData>(); groups.Add(group); }
                group.Add(unit);
            }

            var slots = groups.Select(group =>
            {
                var unit = group[0];
                catalog.TryGet(
                    unit.TypeId,
                    unit.EliteLevel,
                    out var type);
                return new StagingStackSnapshot(
                    unit.TypeId,
                    type == null ? int.MaxValue : type.DeploymentCost,
                    type == null ? string.Empty : type.PortraitResourcePath,
                    type == null ? 0 : type.Rarity,
                    unit.EliteLevel,
                    unit.Buffs,
                    group.Select(item => item.UnitId));
            }).ToList();
            slots.Sort(StagingSlotComparer.Instance);
            return new ReadOnlyCollection<StagingStackSnapshot>(slots);
        }

        private static bool CanStack(PlayerUnitData left, PlayerUnitData right) => string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal) && left.EliteLevel == right.EliteLevel && left.Buffs.SequenceEqual(right.Buffs);

        private sealed class StagingSlotComparer : IComparer<StagingStackSnapshot>
        {
            public static readonly StagingSlotComparer Instance = new StagingSlotComparer();
            public int Compare(StagingStackSnapshot left, StagingStackSnapshot right)
            {
                var cost = left.DeploymentCost.CompareTo(right.DeploymentCost);
                if (cost != 0) return cost;
                return string.Compare(left.TypeId, right.TypeId, StringComparison.Ordinal);
            }
        }
    }

    public static class LocalPlayerStateLoader
    {
        public const string SchemaVersion = "local-player-state-v1";

        public static PlayerStateLoadResult LoadFromResources(string catalogResourcePath, string playerStateResourcePath)
        {
            var catalogResult = UnitCatalogLoader.LoadFromResources(catalogResourcePath);
            if (!catalogResult.Success) return Failure("playerState.catalog.invalid", string.Join(";", catalogResult.Errors.Select(error => error.ToString())));
            return LoadFromResources(catalogResult.Catalog, playerStateResourcePath);
        }

        /// <summary>Loads a fixed player fixture using an already validated catalog so a local match has one catalog authority.</summary>
        internal static PlayerStateLoadResult LoadFromResources(UnitCatalog catalog, string playerStateResourcePath)
        {
            if (catalog == null) return Failure("playerState.catalog.missing", "catalog is required");
            var asset = Resources.Load<TextAsset>(playerStateResourcePath);
            return asset == null ? Failure("playerState.resource.missing", "resource=" + playerStateResourcePath) : LoadFromJson(catalog, asset.text);
        }

        public static PlayerStateLoadResult LoadFromJson(UnitCatalog catalog, string json)
        {
            if (catalog == null) return Failure("playerState.catalog.missing", "catalog is required");
            if (string.IsNullOrWhiteSpace(json)) return Failure("playerState.json.empty", "JSON is empty");
            LocalPlayerStateDto dto;
            try { dto = JsonUtility.FromJson<LocalPlayerStateDto>(json); }
            catch (Exception exception) { return Failure("playerState.json.invalid", exception.Message); }
            if (dto == null) return Failure("playerState.json.invalid", "JSON could not be parsed");

            var errors = new List<PlayerStateValidationError>();
            if (!string.Equals(dto.schemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add(Error("playerState.schema.unsupported", dto.schemaVersion));
            if (string.IsNullOrWhiteSpace(dto.playerId)) errors.Add(Error("playerState.playerId.invalid", "player ID is required"));
            if (dto.deploymentCost < 0) errors.Add(Error("playerState.cost.invalid", "cost=" + dto.deploymentCost));
            var sourceUnits = dto.units ?? Array.Empty<PlayerUnitDto>();
            if (sourceUnits.Length > PlayerState.FixedCapacity) errors.Add(Error("playerState.capacity.exceeded", "units=" + sourceUnits.Length));

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenCells = new HashSet<LocalFormationCoordinate>();
            var units = new List<PlayerUnitData>();
            foreach (var source in sourceUnits)
            {
                if (source == null) { errors.Add(Error("playerState.unit.missing", "unit is null")); continue; }
                var valid = true;
                if (string.IsNullOrWhiteSpace(source.unitId) || !seenIds.Add(source.unitId)) { errors.Add(Error("playerState.unitId.duplicate", source.unitId)); valid = false; }
                if (!catalog.TryGet(source.typeId, out _)) { errors.Add(Error("playerState.unit.type.unknown", source.typeId)); valid = false; }
                if (!Enum.TryParse(source.zone, true, out PlayerUnitZone zone) || !Enum.IsDefined(typeof(PlayerUnitZone), zone)) { errors.Add(Error("playerState.unit.zone.invalid", source.zone)); valid = false; }
                if (source.eliteLevel < 0 || source.eliteLevel > 3) { errors.Add(Error("playerState.unit.elite.invalid", source.unitId)); valid = false; }

                LocalFormationCoordinate? formation = null;
                var hasCoordinate = source.formationX != 0 || source.formationY != 0;
                if (zone == PlayerUnitZone.Deployed)
                {
                    if (!hasCoordinate || !LocalFormationCoordinate.TryCreate(source.formationX, source.formationY, out var parsed)) { errors.Add(Error("playerState.unit.coordinate.invalid", source.unitId)); valid = false; }
                    else if (!LocalFormationCoordinate.IsDeployable(parsed.X, parsed.Y)) { errors.Add(Error("playerState.unit.coordinate.gate", source.unitId)); valid = false; }
                    else if (!seenCells.Add(parsed)) { errors.Add(Error("playerState.unit.coordinate.duplicate", source.unitId)); valid = false; }
                    else formation = parsed;
                }
                else if (hasCoordinate) { errors.Add(Error("playerState.unit.coordinate.unexpected", source.unitId)); valid = false; }

                if (valid) units.Add(new PlayerUnitData(source.unitId, source.typeId, zone, source.eliteLevel, (source.buffs ?? Array.Empty<PlayerBuffDto>()).Where(buff => buff != null).Select(buff => new PlayerBuffSnapshot(buff.id, buff.rawPayload)), formation));
            }

            if (PlayerState.CountStagingSlots(units, catalog) > PlayerState.StagingSlotCapacity) errors.Add(Error("playerState.staging.capacity.exceeded", "slots=" + PlayerState.CountStagingSlots(units, catalog)));
            if (errors.Count > 0) return new PlayerStateLoadResult(null, new ReadOnlyCollection<PlayerStateValidationError>(errors));
            return new PlayerStateLoadResult(new PlayerState(dto.playerId, dto.deploymentCost, catalog, units), Array.Empty<PlayerStateValidationError>());
        }

        private static PlayerStateLoadResult Failure(string code, string message) => new PlayerStateLoadResult(null, new[] { Error(code, message) });
        private static PlayerStateValidationError Error(string code, string detail) => new PlayerStateValidationError(code, detail ?? string.Empty);

        [Serializable] private sealed class LocalPlayerStateDto { public string schemaVersion; public string playerId; public int deploymentCost; public PlayerUnitDto[] units; }
        [Serializable] private sealed class PlayerUnitDto { public string unitId; public string typeId; public string zone; public int eliteLevel; public PlayerBuffDto[] buffs; public int formationX; public int formationY; }
        [Serializable] private sealed class PlayerBuffDto { public string id; public string rawPayload; }
    }

    internal sealed class PlayerUnitData
    {
        public PlayerUnitData(string unitId, string typeId, PlayerUnitZone zone, int eliteLevel, IEnumerable<PlayerBuffSnapshot> buffs, LocalFormationCoordinate? formation)
        {
            UnitId = unitId;
            TypeId = typeId;
            Zone = zone;
            EliteLevel = eliteLevel;
            Buffs = (buffs ?? Enumerable.Empty<PlayerBuffSnapshot>()).ToArray();
            Formation = formation;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public PlayerUnitZone Zone { get; set; }
        public int EliteLevel { get; }
        public IReadOnlyList<PlayerBuffSnapshot> Buffs { get; }
        public LocalFormationCoordinate? Formation { get; set; }
        public PlayerUnitData WithZone(PlayerUnitZone zone, LocalFormationCoordinate? formation) => new PlayerUnitData(UnitId, TypeId, zone, EliteLevel, Buffs, formation);
    }
}
