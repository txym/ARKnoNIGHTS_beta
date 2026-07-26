using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using ArknoNights.Battle.Infrastructure;
using UnityEngine;

namespace ArknoNights.Player
{
    public enum LocalMatchOperationCode
    {
        Success,
        PlayerNotFound,
        ShopSlotNotFound,
        ShopSlotEmpty,
        InsufficientGold,
        MaximumLevelReached,
        PlayerUnitRejected
    }

    public sealed class LocalMatchValidationError
    {
        internal LocalMatchValidationError(string code, string message) { Code = code; Message = message; }
        public string Code { get; }
        public string Message { get; }
        public override string ToString() => Code + ": " + Message;
    }

    public sealed class LocalMatchLoadResult
    {
        internal LocalMatchLoadResult(LocalMatchState state, IReadOnlyList<LocalMatchValidationError> errors) { State = state; Errors = errors; }
        public bool Success => State != null && Errors.Count == 0;
        public LocalMatchState State { get; }
        public IReadOnlyList<LocalMatchValidationError> Errors { get; }
    }

    public sealed class LocalMatchShopSlotSnapshot
    {
        internal LocalMatchShopSlotSnapshot(int shopSlotId, string unitTypeId, bool isFrozen, UnitCatalog catalog)
        {
            ShopSlotId = shopSlotId;
            UnitTypeId = unitTypeId ?? string.Empty;
            IsFrozen = !string.IsNullOrEmpty(UnitTypeId) && isFrozen;
            IsEmpty = string.IsNullOrEmpty(UnitTypeId);
            if (!IsEmpty && catalog.TryGet(UnitTypeId, out var type))
            {
                Price = type.Rarity;
                Rarity = type.Rarity;
                DisplayName = type.DisplayNameZhHans;
                PortraitResourcePath = type.PortraitResourcePath;
            }
        }

        public int ShopSlotId { get; }
        public string UnitTypeId { get; }
        public bool IsFrozen { get; }
        public bool IsEmpty { get; }
        /// <summary>Derived every snapshot from UnitCatalogEntry.Rarity; it is not shop configuration.</summary>
        public int Price { get; }
        public int Rarity { get; }
        public string DisplayName { get; } = string.Empty;
        public string PortraitResourcePath { get; } = string.Empty;
    }

    public sealed class LocalMatchPlayerSnapshot
    {
        internal LocalMatchPlayerSnapshot(string playerId, string displayName, string avatarResourcePath, int life, bool isConnected, bool hasExited, PlayerStateSnapshot playerState, int level, int gold, bool isReady, IEnumerable<LocalMatchShopSlotSnapshot> shopSlots)
        {
            PlayerId = playerId;
            DisplayName = displayName ?? string.Empty;
            AvatarResourcePath = avatarResourcePath ?? string.Empty;
            Life = life;
            IsConnected = isConnected;
            HasExited = hasExited;
            PlayerState = playerState;
            Level = level;
            Gold = gold;
            IsReady = isReady;
            ShopSlots = new ReadOnlyCollection<LocalMatchShopSlotSnapshot>((shopSlots ?? Enumerable.Empty<LocalMatchShopSlotSnapshot>()).ToArray());
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarResourcePath { get; }
        public int Life { get; }
        public bool IsConnected { get; }
        public bool HasExited { get; }
        public PlayerStateSnapshot PlayerState { get; }
        public int Level { get; }
        public int Gold { get; }
        public bool IsReady { get; }
        public IReadOnlyList<LocalMatchShopSlotSnapshot> ShopSlots { get; }
    }

    public sealed class LocalMatchSnapshot
    {
        internal LocalMatchSnapshot(string localPlayerId, string observedPlayerId, long version, IEnumerable<LocalMatchPlayerSnapshot> players)
        {
            LocalPlayerId = localPlayerId;
            ObservedPlayerId = observedPlayerId;
            Version = version;
            Players = new ReadOnlyCollection<LocalMatchPlayerSnapshot>((players ?? Enumerable.Empty<LocalMatchPlayerSnapshot>()).OrderBy(player => player.PlayerId, StringComparer.Ordinal).ToArray());
            LocalPlayer = Players.Single(player => player.PlayerId == LocalPlayerId);
            CanonicalSummary = BuildCanonicalSummary();
        }

        public string LocalPlayerId { get; }
        public string ObservedPlayerId { get; }
        public long Version { get; }
        public IReadOnlyList<LocalMatchPlayerSnapshot> Players { get; }
        public LocalMatchPlayerSnapshot LocalPlayer { get; }
        public string CanonicalSummary { get; }

        private string BuildCanonicalSummary()
        {
            var builder = new StringBuilder();
            builder.Append(LocalPlayerId).Append('|').Append(ObservedPlayerId).Append('|').Append(Version);
            foreach (var player in Players)
            {
                builder.Append("|P:").Append(player.PlayerId).Append(',').Append(player.Life).Append(',').Append(player.IsConnected ? 1 : 0)
                    .Append(',').Append(player.HasExited ? 1 : 0)
                    .Append(',').Append(player.Level).Append(',').Append(player.Gold).Append(',').Append(player.IsReady ? 1 : 0)
                    .Append(',').Append(player.PlayerState.CanonicalSummary);
                foreach (var slot in player.ShopSlots)
                    builder.Append("|S:").Append(slot.ShopSlotId).Append(',').Append(slot.UnitTypeId).Append(',').Append(slot.IsFrozen ? 1 : 0);
            }
            return builder.ToString();
        }
    }

    public sealed class LocalMatchOperationResult
    {
        internal LocalMatchOperationResult(LocalMatchOperationCode code, LocalMatchSnapshot snapshot, string purchasedUnitId = "")
        {
            Code = code;
            Snapshot = snapshot;
            PurchasedUnitId = purchasedUnitId ?? string.Empty;
        }

        public bool Success => Code == LocalMatchOperationCode.Success;
        public LocalMatchOperationCode Code { get; }
        public LocalMatchSnapshot Snapshot { get; }
        public string PurchasedUnitId { get; }
    }

    /// <summary>Plain-C# local demo match state. It owns only local-session economy, shop, ready, and observation data.</summary>
    public sealed class LocalMatchState
    {
        public const int PlayerCount = 4;
        public const int InitialLevel = 1;
        public const int MaximumLevel = 9;
        public const int InitialGold = 7;
        public const int RefreshCost = 1;
        public const int ShopSlotCount = 6;

        private static readonly int[] UpgradeCosts = { 4, 6, 8, 10, 12, 14, 16, 18 };

        private readonly UnitCatalog catalog;
        private readonly Dictionary<string, LocalMatchPlayerData> playersById;
        private readonly ReadOnlyCollection<string> orderedPlayerIds;
        private readonly HashSet<string> knownUnitIds;
        private readonly string[][] shopPages;
        private readonly ShopSlotData[] shopSlots;
        private readonly string localPlayerId;
        private string observedPlayerId;
        private int level;
        private int gold;
        private bool isReady;
        private int currentShopPage;
        private int purchasedUnitSequence;
        private long version;

        internal LocalMatchState(UnitCatalog catalog, string localPlayerId, IEnumerable<LocalMatchPlayerData> players, IEnumerable<string[]> shopPages, int initialLevel, int initialGold)
        {
            this.catalog = catalog;
            this.localPlayerId = localPlayerId;
            var orderedPlayers = (players ?? Enumerable.Empty<LocalMatchPlayerData>()).ToArray();
            playersById = orderedPlayers.ToDictionary(player => player.PlayerId, StringComparer.Ordinal);
            orderedPlayerIds = new ReadOnlyCollection<string>(orderedPlayers.Select(player => player.PlayerId).ToArray());
            knownUnitIds = new HashSet<string>(playersById.Values.SelectMany(player => player.PlayerState.Snapshot.Units).Select(unit => unit.UnitId), StringComparer.Ordinal);
            this.shopPages = shopPages.Select(page => page.ToArray()).ToArray();
            shopSlots = this.shopPages[0].Select((typeId, index) => new ShopSlotData(index, typeId, false)).ToArray();
            observedPlayerId = localPlayerId;
            level = initialLevel;
            gold = initialGold;
        }

        public event Action<LocalMatchSnapshot> Changed;
        public string LocalPlayerId => localPlayerId;
        public string ObservedPlayerId => observedPlayerId;
        /// <summary>Stable fixture order used for the confirmed Player1-vs-2 / Player3-vs-4 battle pairing.</summary>
        public IReadOnlyList<string> OrderedPlayerIds => orderedPlayerIds;
        public LocalMatchSnapshot Snapshot => CreateSnapshot();

        /// <summary>Returns the persistent state belonging to one fixture player without changing command ownership.</summary>
        public bool TryGetPlayerState(string playerId, out PlayerState state)
        {
            if (playersById.TryGetValue(playerId ?? string.Empty, out var player))
            {
                state = player.PlayerState;
                return true;
            }
            state = null;
            return false;
        }

        public LocalMatchOperationResult TryPurchase(int shopSlotId)
        {
            if (!TryGetShopSlot(shopSlotId, out var slot)) return Result(LocalMatchOperationCode.ShopSlotNotFound);
            if (slot.IsEmpty) return Result(LocalMatchOperationCode.ShopSlotEmpty);
            if (!catalog.TryGet(slot.UnitTypeId, out var type)) return Result(LocalMatchOperationCode.PlayerUnitRejected);
            if (gold < type.Rarity) return Result(LocalMatchOperationCode.InsufficientGold);

            var nextSequence = purchasedUnitSequence;
            string purchasedUnitId;
            do
            {
                nextSequence++;
                purchasedUnitId = localPlayerId + "-shop-" + nextSequence.ToString("D4");
            }
            while (knownUnitIds.Contains(purchasedUnitId));

            var playerResult = playersById[localPlayerId].PlayerState.TryAddPurchasedUnit(purchasedUnitId, slot.UnitTypeId);
            if (!playerResult.Success) return Result(LocalMatchOperationCode.PlayerUnitRejected);

            purchasedUnitSequence = nextSequence;
            knownUnitIds.Add(purchasedUnitId);
            gold -= type.Rarity;
            slot.UnitTypeId = string.Empty;
            slot.IsFrozen = false;
            NotifyChanged();
            return new LocalMatchOperationResult(LocalMatchOperationCode.Success, CreateSnapshot(), purchasedUnitId);
        }

        public LocalMatchOperationResult TryRefresh()
        {
            if (gold < RefreshCost) return Result(LocalMatchOperationCode.InsufficientGold);
            var nextPage = (currentShopPage + 1) % shopPages.Length;
            for (var index = 0; index < shopSlots.Length; index++)
            {
                if (shopSlots[index].IsFrozen && !shopSlots[index].IsEmpty) continue;
                shopSlots[index].UnitTypeId = shopPages[nextPage][index];
                shopSlots[index].IsFrozen = false;
            }

            currentShopPage = nextPage;
            gold -= RefreshCost;
            NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TryToggleFrozen(int shopSlotId)
        {
            if (!TryGetShopSlot(shopSlotId, out var slot)) return Result(LocalMatchOperationCode.ShopSlotNotFound);
            if (slot.IsEmpty) return Result(LocalMatchOperationCode.ShopSlotEmpty);
            slot.IsFrozen = !slot.IsFrozen;
            NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TrySetOccupiedShopSlotsFrozen(bool frozen)
        {
            var changed = false;
            foreach (var slot in shopSlots)
            {
                if (slot.IsEmpty || slot.IsFrozen == frozen) continue;
                slot.IsFrozen = frozen;
                changed = true;
            }

            if (changed) NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TryUpgrade()
        {
            if (level >= MaximumLevel) return Result(LocalMatchOperationCode.MaximumLevelReached);
            var cost = UpgradeCosts[level - InitialLevel];
            if (gold < cost) return Result(LocalMatchOperationCode.InsufficientGold);
            gold -= cost;
            level++;
            NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TryToggleReady()
        {
            isReady = !isReady;
            NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TryObserve(string playerId)
        {
            if (!playersById.ContainsKey(playerId ?? string.Empty)) return Result(LocalMatchOperationCode.PlayerNotFound);
            if (string.Equals(observedPlayerId, playerId, StringComparison.Ordinal)) return Result(LocalMatchOperationCode.Success);
            observedPlayerId = playerId;
            NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        public LocalMatchOperationResult TryObserveLocalPlayer() => TryObserve(localPlayerId);

        /// <summary>
        /// Resets only transient preparation UI state when a completed local battle returns to preparation.
        /// Player units, formation, economy, shop contents, lives, and every computed battle result remain untouched.
        /// </summary>
        public LocalMatchOperationResult ResetPreparationUiState()
        {
            var changed = isReady || !string.Equals(observedPlayerId, localPlayerId, StringComparison.Ordinal);
            isReady = false;
            observedPlayerId = localPlayerId;
            if (changed) NotifyChanged();
            return Result(LocalMatchOperationCode.Success);
        }

        private bool TryGetShopSlot(int shopSlotId, out ShopSlotData slot)
        {
            if (shopSlotId >= 0 && shopSlotId < shopSlots.Length) { slot = shopSlots[shopSlotId]; return true; }
            slot = null;
            return false;
        }

        private LocalMatchOperationResult Result(LocalMatchOperationCode code) => new LocalMatchOperationResult(code, CreateSnapshot());

        private void NotifyChanged()
        {
            version++;
            Changed?.Invoke(CreateSnapshot());
        }

        private LocalMatchSnapshot CreateSnapshot()
        {
            var players = orderedPlayerIds.Select(playerId => playersById[playerId]).Select(player =>
            {
                var local = string.Equals(player.PlayerId, localPlayerId, StringComparison.Ordinal);
                var slots = local ? shopSlots.Select(slot => new LocalMatchShopSlotSnapshot(slot.ShopSlotId, slot.UnitTypeId, slot.IsFrozen, catalog)) : Enumerable.Empty<LocalMatchShopSlotSnapshot>();
                return new LocalMatchPlayerSnapshot(player.PlayerId, player.DisplayName, player.AvatarResourcePath, player.Life, player.IsConnected, player.HasExited, player.PlayerState.Snapshot, local ? level : 0, local ? gold : 0, local && isReady, slots);
            });
            return new LocalMatchSnapshot(localPlayerId, observedPlayerId, version, players);
        }
    }

    public static class LocalMatchStateLoader
    {
        public const string SchemaVersion = "local-match-state-v1";

        public static LocalMatchLoadResult LoadFromResources(string catalogResourcePath, string matchStateResourcePath, PlayerState localPlayerOverride = null)
        {
            var catalogResult = UnitCatalogLoader.LoadFromResources(catalogResourcePath);
            if (!catalogResult.Success) return Failure("localMatch.catalog.invalid", string.Join(";", catalogResult.Errors.Select(error => error.ToString())));
            return LoadFromResources(catalogResult.Catalog, matchStateResourcePath, localPlayerOverride);
        }

        public static LocalMatchLoadResult LoadFromResources(UnitCatalog catalog, string matchStateResourcePath, PlayerState localPlayerOverride = null)
        {
            if (catalog == null) return Failure("localMatch.catalog.missing", "catalog is required");
            var asset = Resources.Load<TextAsset>(matchStateResourcePath);
            return asset == null ? Failure("localMatch.resource.missing", matchStateResourcePath) : LoadFromJson(catalog, asset.text, localPlayerOverride);
        }

        public static LocalMatchLoadResult LoadFromJson(UnitCatalog catalog, string json, PlayerState localPlayerOverride = null)
        {
            if (catalog == null) return Failure("localMatch.catalog.missing", "catalog is required");
            if (string.IsNullOrWhiteSpace(json)) return Failure("localMatch.json.empty", "JSON is empty");
            LocalMatchStateDto dto;
            try { dto = JsonUtility.FromJson<LocalMatchStateDto>(json); }
            catch (Exception exception) { return Failure("localMatch.json.invalid", exception.Message); }
            if (dto == null) return Failure("localMatch.json.invalid", "JSON could not be parsed");

            var errors = new List<LocalMatchValidationError>();
            if (!string.Equals(dto.schemaVersion, SchemaVersion, StringComparison.Ordinal)) errors.Add(Error("localMatch.schema.unsupported", dto.schemaVersion));
            var sourcePlayers = dto.players ?? Array.Empty<LocalMatchPlayerDto>();
            if (sourcePlayers.Length != LocalMatchState.PlayerCount) errors.Add(Error("localMatch.players.count.invalid", sourcePlayers.Length.ToString()));
            var playerIds = new HashSet<string>(StringComparer.Ordinal);
            var unitIds = new HashSet<string>(StringComparer.Ordinal);
            var players = new List<LocalMatchPlayerData>();
            foreach (var source in sourcePlayers)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.playerId) || !playerIds.Add(source.playerId)) { errors.Add(Error("localMatch.playerId.invalid", source == null ? string.Empty : source.playerId)); continue; }
                if (source.life != 400) errors.Add(Error("localMatch.player.life.invalid", source.playerId));
                PlayerState playerState;
                if (localPlayerOverride != null && string.Equals(source.playerId, dto.localPlayerId, StringComparison.Ordinal))
                {
                    playerState = localPlayerOverride;
                }
                else
                {
                    var playerLoad = LocalPlayerStateLoader.LoadFromResources(catalog, source.playerStateResourcePath);
                    if (!playerLoad.Success) { errors.Add(Error("localMatch.playerState.invalid", source.playerId)); continue; }
                    playerState = playerLoad.State;
                }
                if (!string.Equals(playerState.PlayerId, source.playerId, StringComparison.Ordinal)) { errors.Add(Error("localMatch.playerState.playerId.mismatch", source.playerId)); continue; }
                if (playerState.Snapshot.Units.Any(unit => !unitIds.Add(unit.UnitId))) { errors.Add(Error("localMatch.unitId.duplicate", source.playerId)); continue; }
                players.Add(new LocalMatchPlayerData(source.playerId, source.displayName, source.avatarResourcePath, source.life, source.isConnected, source.hasExited, playerState));
            }

            if (string.IsNullOrWhiteSpace(dto.localPlayerId) || !playerIds.Contains(dto.localPlayerId)) errors.Add(Error("localMatch.localPlayer.invalid", dto.localPlayerId));
            if (dto.initialLevel < LocalMatchState.InitialLevel || dto.initialLevel > LocalMatchState.MaximumLevel) errors.Add(Error("localMatch.initialLevel.invalid", dto.initialLevel.ToString()));
            if (dto.initialGold < 0) errors.Add(Error("localMatch.initialGold.invalid", dto.initialGold.ToString()));
            var pages = dto.shopPages ?? Array.Empty<LocalMatchShopPageDto>();
            if (pages.Length != 3) errors.Add(Error("localMatch.shop.pages.invalid", pages.Length.ToString()));
            var parsedPages = new List<string[]>();
            foreach (var page in pages)
            {
                var typeIds = page == null ? Array.Empty<string>() : page.typeIds ?? Array.Empty<string>();
                if (typeIds.Length != LocalMatchState.ShopSlotCount || typeIds.Any(typeId => !catalog.TryGet(typeId, out _))) errors.Add(Error("localMatch.shop.page.invalid", string.Join(",", typeIds)));
                else parsedPages.Add(typeIds.ToArray());
            }

            if (errors.Count > 0) return new LocalMatchLoadResult(null, new ReadOnlyCollection<LocalMatchValidationError>(errors));
            return new LocalMatchLoadResult(new LocalMatchState(catalog, dto.localPlayerId, players, parsedPages, dto.initialLevel, dto.initialGold), Array.Empty<LocalMatchValidationError>());
        }

        private static LocalMatchLoadResult Failure(string code, string detail) => new LocalMatchLoadResult(null, new[] { Error(code, detail) });
        private static LocalMatchValidationError Error(string code, string detail) => new LocalMatchValidationError(code, detail ?? string.Empty);

        [Serializable] private sealed class LocalMatchStateDto { public string schemaVersion; public string localPlayerId; public int initialLevel; public int initialGold; public LocalMatchPlayerDto[] players; public LocalMatchShopPageDto[] shopPages; }
        [Serializable] private sealed class LocalMatchPlayerDto { public string playerId; public string displayName; public string avatarResourcePath; public int life; public bool isConnected; public bool hasExited; public string playerStateResourcePath; }
        [Serializable] private sealed class LocalMatchShopPageDto { public string[] typeIds; }
    }

    internal sealed class LocalMatchPlayerData
    {
        public LocalMatchPlayerData(string playerId, string displayName, string avatarResourcePath, int life, bool isConnected, bool hasExited, PlayerState playerState)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            AvatarResourcePath = avatarResourcePath;
            Life = life;
            IsConnected = isConnected;
            HasExited = hasExited;
            PlayerState = playerState;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarResourcePath { get; }
        public int Life { get; }
        public bool IsConnected { get; }
        public bool HasExited { get; }
        public PlayerState PlayerState { get; }
    }

    internal sealed class ShopSlotData
    {
        public ShopSlotData(int shopSlotId, string unitTypeId, bool isFrozen) { ShopSlotId = shopSlotId; UnitTypeId = unitTypeId; IsFrozen = isFrozen; }
        public int ShopSlotId { get; }
        public string UnitTypeId { get; set; }
        public bool IsFrozen { get; set; }
        public bool IsEmpty => string.IsNullOrEmpty(UnitTypeId);
    }
}
