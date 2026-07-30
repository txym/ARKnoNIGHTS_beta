using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ArknoNights.Match;

namespace ArknoNights.Lobby
{
    public enum MatchWireKind
    {
        Handshake,
        HandshakeAccepted,
        Reject,
        MatchInitialized,
        Command,
        CommandAck,
        ScopedSnapshot,
        SnapshotRequest,
        ClockSync,
        BattleSeal,
        FirstChunkReady,
        PlaybackStart,
        PlaybackClock,
        FinalSecondHash,
        ClientBattleFailure,
        ReconnectRequest,
        ReconnectAccepted,
        ReconnectRejected,
        ExplicitQuit,
        MatchEnded,
        Ping,
        Pong
    }

    [Flags]
    public enum MatchWireDirection
    {
        ClientToHost = 1,
        HostToClient = 2,
        Bidirectional = ClientToHost | HostToClient
    }

    public enum MatchProtocolError
    {
        None,
        EmptyFrame,
        FrameTooLarge,
        InvalidFrameLength,
        InvalidUtf8,
        InvalidJson,
        UnsupportedProtocolVersion,
        UnsupportedSchemaVersion,
        UnknownMessageKind,
        DirectionNotAllowed,
        MissingRequiredField,
        InvalidPayload,
        PayloadTooLarge
    }

    public enum MatchCommandKind
    {
        SetPreparationReady,
        RefreshShop,
        ToggleShopFreeze,
        PurchaseShopOffer,
        PurchaseLevelUpgrade,
        DeployUnit,
        ReplaceDeployedUnit,
        RelocateOrSwapUnit,
        RetreatUnit
    }

    public enum MatchLocalConnectionState
    {
        Connected,
        Reconnecting,
        Spectating,
        Ended
    }

    public enum MatchReconnectRejectCode
    {
        SessionEnded,
        UnknownSession,
        InvalidToken,
        UnknownPlayer,
        ExplicitlyQuit,
        CompatibilityMismatch,
        MalformedRequest
    }

    [DataContract]
    public sealed class MatchWireEnvelope
    {
        [DataMember(Name = "protocolVersion", Order = 1)]
        public int ProtocolVersion;
        [DataMember(Name = "schemaVersion", Order = 2)]
        public int SchemaVersion;
        [DataMember(Name = "kind", Order = 3)]
        public string Kind;
        [DataMember(Name = "sessionId", Order = 4)]
        public string SessionId;
        [DataMember(Name = "messageId", Order = 5)]
        public string MessageId;
        [DataMember(Name = "payload", Order = 6)]
        public string Payload;
    }

    [DataContract]
    public sealed class MatchCompatibilityWire
    {
        [DataMember(Name = "protocolVersion", Order = 1)]
        public string ProtocolVersion;
        [DataMember(Name = "matchRulesVersion", Order = 2)]
        public string MatchRulesVersion;
        [DataMember(Name = "battleCoreVersion", Order = 3)]
        public string BattleCoreVersion;
        [DataMember(Name = "unitCatalogSha256", Order = 4)]
        public string UnitCatalogSha256;
        [DataMember(Name = "abilityCatalogSha256", Order = 5)]
        public string AbilityCatalogSha256;

        public static MatchCompatibilityWire FromDomain(MatchCompatibilityManifest manifest)
        {
            if (manifest == null) return null;
            return new MatchCompatibilityWire
            {
                ProtocolVersion = manifest.ProtocolVersion,
                MatchRulesVersion = manifest.MatchRulesVersion,
                BattleCoreVersion = manifest.BattleCoreVersion,
                UnitCatalogSha256 = manifest.UnitCatalogSha256,
                AbilityCatalogSha256 = manifest.AbilityCatalogSha256
            };
        }

        public MatchCompatibilityManifest ToDomain()
        {
            return new MatchCompatibilityManifest(
                ProtocolVersion,
                MatchRulesVersion,
                BattleCoreVersion,
                UnitCatalogSha256,
                AbilityCatalogSha256);
        }

        public bool IsValid
        {
            get
            {
                try { return ToDomain().IsValid; }
                catch (Exception) { return false; }
            }
        }
    }

    [DataContract]
    public sealed class MatchHandshakePayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "manifest")] public MatchCompatibilityWire Manifest;
    }

    [DataContract]
    public sealed class MatchHandshakeAcceptedPayload
    {
        [DataMember(Name = "connectionId")] public string ConnectionId;
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
    }

    [DataContract]
    public sealed class MatchRejectPayload
    {
        [DataMember(Name = "code")] public string Code;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
    }

    [DataContract]
    public sealed class MatchInitializedPayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "seatIndex")] public int SeatIndex;
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
        [DataMember(Name = "hostPlayerId")] public string HostPlayerId;
        [DataMember(Name = "matchSeed")] public string MatchSeed;
        [DataMember(Name = "reconnectToken")] public string ReconnectToken;
        [DataMember(Name = "manifest")] public MatchCompatibilityWire Manifest;
        [DataMember(Name = "snapshot")] public ScopedSnapshotPayload Snapshot;
        [DataMember(Name = "clock")] public MatchClockSyncPayload Clock;
    }

    [DataContract]
    public sealed class MatchCommandWirePayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
        [DataMember(Name = "commandId")] public string CommandId;
        [DataMember(Name = "knownStateRevision")] public long KnownStateRevision;
        [DataMember(Name = "commandKind")] public string CommandKind;
        [DataMember(Name = "desiredReady")] public bool DesiredReady;
        [DataMember(Name = "slotIndex")] public int SlotIndex;
        [DataMember(Name = "expectedUnitId")] public string ExpectedUnitId;
        [DataMember(Name = "expectedCurrentLevel")] public int ExpectedCurrentLevel;
        [DataMember(Name = "expectedCurrentPrice")] public int ExpectedCurrentPrice;
        [DataMember(Name = "unitId")] public string UnitId;
        [DataMember(Name = "stagingUnitId")] public string StagingUnitId;
        [DataMember(Name = "expectedDeployedUnitId")] public string ExpectedDeployedUnitId;
        [DataMember(Name = "targetX")] public int TargetX;
        [DataMember(Name = "targetY")] public int TargetY;
        [DataMember(Name = "expectedAvailableCost")] public int ExpectedAvailableCost;

        public bool TryToDomain(string sessionId, out MatchCommandEnvelope command)
        {
            command = null;
            if (!Enum.TryParse(CommandKind, false, out MatchCommandKind kind)
                || !Enum.IsDefined(typeof(MatchCommandKind), kind))
            {
                return false;
            }

            MatchCommandPayload payload;
            switch (kind)
            {
                case MatchCommandKind.SetPreparationReady:
                    payload = new SetPreparationReadyCommand(DesiredReady);
                    break;
                case MatchCommandKind.RefreshShop:
                    payload = new RefreshShopCommand();
                    break;
                case MatchCommandKind.ToggleShopFreeze:
                    payload = new ToggleShopFreezeCommand();
                    break;
                case MatchCommandKind.PurchaseShopOffer:
                    payload = new PurchaseShopOfferCommand(SlotIndex, ExpectedUnitId);
                    break;
                case MatchCommandKind.PurchaseLevelUpgrade:
                    payload = new PurchaseLevelUpgradeCommand(ExpectedCurrentLevel, ExpectedCurrentPrice);
                    break;
                case MatchCommandKind.DeployUnit:
                    payload = new DeployUnitCommand(
                        UnitId,
                        new MatchFormationPosition(TargetX, TargetY),
                        ExpectedAvailableCost);
                    break;
                case MatchCommandKind.ReplaceDeployedUnit:
                    payload = new ReplaceDeployedUnitCommand(
                        StagingUnitId,
                        ExpectedDeployedUnitId,
                        new MatchFormationPosition(TargetX, TargetY));
                    break;
                case MatchCommandKind.RelocateOrSwapUnit:
                    payload = new RelocateOrSwapUnitCommand(
                        UnitId,
                        new MatchFormationPosition(TargetX, TargetY));
                    break;
                case MatchCommandKind.RetreatUnit:
                    payload = new RetreatUnitCommand(UnitId);
                    break;
                default:
                    return false;
            }

            command = new MatchCommandEnvelope(
                sessionId,
                PlayerId,
                CommandId,
                KnownStateRevision,
                payload);
            return true;
        }
    }

    [DataContract]
    public sealed class MatchCommandAckPayload
    {
        [DataMember(Name = "commandId")] public string CommandId;
        [DataMember(Name = "resultCode")] public string ResultCode;
        [DataMember(Name = "currentStateRevision")] public long CurrentStateRevision;
        [DataMember(Name = "acceptedStateRevision")] public long AcceptedStateRevision;
        [DataMember(Name = "hasAcceptedStateRevision")] public bool HasAcceptedStateRevision;
        [DataMember(Name = "didChangeState")] public bool DidChangeState;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
        [DataMember(Name = "hostAcceptSequence")] public long HostAcceptSequence;
    }

    [DataContract]
    public sealed class MatchSnapshotRequestPayload
    {
        [DataMember(Name = "clientLastAppliedRevision")] public long ClientLastAppliedRevision;
    }

    [DataContract]
    public sealed class MatchClockSyncPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "phase")] public string Phase;
        [DataMember(Name = "hostMonotonicNowMs")] public long HostMonotonicNowMs;
        [DataMember(Name = "preparationDeadlineHostMonotonicMs")] public long PreparationDeadlineHostMonotonicMs;
    }

    [DataContract]
    public sealed class MatchBattleSealPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleSetId")] public string BattleSetId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "sealedPayload")] public string SealedPayload;
        [DataMember(Name = "battleInputs")] public MatchBattleInputHashWire[] BattleInputs;
    }

    [DataContract]
    public sealed class MatchBattleInputHashWire
    {
        [DataMember(Name = "battleId")] public string BattleId;
        [DataMember(Name = "inputSha256")] public string InputSha256;
        [DataMember(Name = "sealedInputHash")] public string SealedInputHash;
    }

    [DataContract]
    public sealed class MatchFirstChunkReadyPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleSetId")] public string BattleSetId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "readyRevision")] public long ReadyRevision;
    }

    [DataContract]
    public sealed class MatchPlaybackStartPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleSetId")] public string BattleSetId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "hostMonotonicStartMs")] public long HostMonotonicStartMs;
        [DataMember(Name = "startTick")] public int StartTick;
    }

    [DataContract]
    public sealed class MatchPlaybackClockPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleSetId")] public string BattleSetId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "hostMonotonicNowMs")] public long HostMonotonicNowMs;
        [DataMember(Name = "currentTick")] public int CurrentTick;
    }

    [DataContract]
    public sealed class MatchFinalSecondHashPayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleId")] public string BattleId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "battleInputSha256")] public string BattleInputSha256;
        [DataMember(Name = "finalSecondSha256")] public string FinalSecondSha256;
    }

    [DataContract]
    public sealed class MatchClientBattleFailurePayload
    {
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "battleId")] public string BattleId;
        [DataMember(Name = "canonicalInputHash")] public string CanonicalInputHash;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
    }

    [DataContract]
    public sealed class MatchReconnectRequestPayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "rawToken")] public string RawToken;
        [DataMember(Name = "manifest")] public MatchCompatibilityWire Manifest;
        [DataMember(Name = "clientLastAppliedRevision")] public long ClientLastAppliedRevision;
    }

    [DataContract]
    public sealed class MatchReconnectAcceptedPayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "seatIndex")] public int SeatIndex;
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
        [DataMember(Name = "reconnectToken")] public string ReconnectToken;
    }

    [DataContract]
    public sealed class MatchReconnectRejectedPayload
    {
        [DataMember(Name = "code")] public string Code;
        [DataMember(Name = "stableDetailCode")] public string StableDetailCode;
    }

    [DataContract]
    public sealed class MatchExplicitQuitPayload
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
    }

    [DataContract]
    public sealed class MatchEndedPayload
    {
        [DataMember(Name = "endReason")] public string EndReason;
        [DataMember(Name = "finalRevision")] public long FinalRevision;
        [DataMember(Name = "finalStandings")] public MatchStandingWire[] FinalStandings;
    }

    [DataContract]
    public sealed class MatchHeartbeatPayload
    {
        [DataMember(Name = "connectionGeneration")] public long ConnectionGeneration;
        [DataMember(Name = "sentUnixMilliseconds")] public long SentUnixMilliseconds;
    }

    [DataContract]
    public sealed class ScopedSnapshotPayload
    {
        [DataMember(Name = "sessionId")] public string SessionId;
        [DataMember(Name = "stateRevision")] public long StateRevision;
        [DataMember(Name = "publicState")] public PublicMatchStateWire PublicState;
        [DataMember(Name = "ownerPrivateState")] public OwnerMatchStateWire OwnerPrivateState;
        [DataMember(Name = "localConnectionState")] public string LocalConnectionState;
    }

    [DataContract]
    public sealed class PublicMatchStateWire
    {
        [DataMember(Name = "sessionId")] public string SessionId;
        [DataMember(Name = "stateRevision")] public long StateRevision;
        [DataMember(Name = "phase")] public string Phase;
        [DataMember(Name = "roundNumber")] public int RoundNumber;
        [DataMember(Name = "preparationRemainingMs")] public long PreparationRemainingMs;
        [DataMember(Name = "pairings")] public PublicMatchPairingWire[] Pairings;
        [DataMember(Name = "endReason")] public string EndReason;
        [DataMember(Name = "finalStandings")] public MatchStandingWire[] FinalStandings;
        [DataMember(Name = "seats")] public PublicMatchSeatWire[] Seats;
    }

    [DataContract]
    public sealed class PublicMatchPairingWire
    {
        [DataMember(Name = "battleId")] public string BattleId;
        [DataMember(Name = "battleIndex")] public int BattleIndex;
        [DataMember(Name = "kind")] public string Kind;
        [DataMember(Name = "homePlayerId")] public string HomePlayerId;
        [DataMember(Name = "awayPlayerId")] public string AwayPlayerId;
        [DataMember(Name = "shadowOwnerPlayerId")] public string ShadowOwnerPlayerId;
    }

    [DataContract]
    public sealed class MatchStandingWire
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "life")] public int Life;
        [DataMember(Name = "placement")] public int Placement;
        [DataMember(Name = "hasPlacement")] public bool HasPlacement;
    }

    [DataContract]
    public sealed class PublicMatchSeatWire
    {
        [DataMember(Name = "seatIndex")] public int SeatIndex;
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "displayName")] public string DisplayName;
        [DataMember(Name = "avatarId")] public string AvatarId;
        [DataMember(Name = "life")] public int Life;
        [DataMember(Name = "connectionState")] public string ConnectionState;
        [DataMember(Name = "ready")] public bool Ready;
        [DataMember(Name = "eliminated")] public bool Eliminated;
        [DataMember(Name = "placement")] public int Placement;
        [DataMember(Name = "hasPlacement")] public bool HasPlacement;
        [DataMember(Name = "units")] public MatchUnitWire[] Units;
        [DataMember(Name = "targetedUnitBuffs")] public MatchTargetedBuffWire[] TargetedUnitBuffs;
        [DataMember(Name = "globalBuffs")] public MatchGlobalBuffWire[] GlobalBuffs;
        [DataMember(Name = "sourceEffects")] public MatchSourceEffectWire[] SourceEffects;
    }

    [DataContract]
    public sealed class OwnerMatchStateWire
    {
        [DataMember(Name = "playerId")] public string PlayerId;
        [DataMember(Name = "gold")] public int Gold;
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "upgradeDiscountCountAtThisLevel")] public int UpgradeDiscountCountAtThisLevel;
        [DataMember(Name = "currentUpgradePrice")] public int CurrentUpgradePrice;
        [DataMember(Name = "successfulShopPurchaseCount")] public int SuccessfulShopPurchaseCount;
        [DataMember(Name = "hasIssuedEffectiveFreezeThisRound")] public bool HasIssuedEffectiveFreezeThisRound;
        [DataMember(Name = "totalDeploymentCost")] public int TotalDeploymentCost;
        [DataMember(Name = "availableDeploymentCost")] public int AvailableDeploymentCost;
        [DataMember(Name = "streakKind")] public string StreakKind;
        [DataMember(Name = "streakCount")] public int StreakCount;
        [DataMember(Name = "units")] public MatchUnitWire[] Units;
        [DataMember(Name = "shopOffers")] public MatchShopOfferWire[] ShopOffers;
        [DataMember(Name = "overflowUnits")] public MatchUnitWire[] OverflowUnits;
        [DataMember(Name = "stagingStacks")] public MatchStagingStackWire[] StagingStacks;
        [DataMember(Name = "targetedUnitBuffs")] public MatchTargetedBuffWire[] TargetedUnitBuffs;
        [DataMember(Name = "globalBuffs")] public MatchGlobalBuffWire[] GlobalBuffs;
        [DataMember(Name = "sourceEffects")] public MatchSourceEffectWire[] SourceEffects;
    }

    [DataContract]
    public sealed class MatchUnitWire
    {
        [DataMember(Name = "unitId")] public string UnitId;
        [DataMember(Name = "typeId")] public string TypeId;
        [DataMember(Name = "zone")] public string Zone;
        [DataMember(Name = "eliteLevel")] public int EliteLevel;
        [DataMember(Name = "hasFormation")] public bool HasFormation;
        [DataMember(Name = "formationX")] public int FormationX;
        [DataMember(Name = "formationY")] public int FormationY;
        [DataMember(Name = "acquisitionOrdinal")] public long AcquisitionOrdinal;
        [DataMember(Name = "buffs")] public MatchBuffWire[] Buffs;
    }

    [DataContract]
    public sealed class MatchBuffWire
    {
        [DataMember(Name = "buffId")] public string BuffId;
        [DataMember(Name = "canonicalPayload")] public string CanonicalPayload;
    }

    [DataContract]
    public sealed class MatchShopOfferWire
    {
        [DataMember(Name = "slotIndex")] public int SlotIndex;
        [DataMember(Name = "unitId")] public string UnitId;
        [DataMember(Name = "typeId")] public string TypeId;
        [DataMember(Name = "rarity")] public int Rarity;
        [DataMember(Name = "hasRarity")] public bool HasRarity;
        [DataMember(Name = "isFrozen")] public bool IsFrozen;
    }

    [DataContract]
    public sealed class MatchStagingStackWire
    {
        [DataMember(Name = "typeId")] public string TypeId;
        [DataMember(Name = "numericTypeId")] public long NumericTypeId;
        [DataMember(Name = "deploymentCost")] public int DeploymentCost;
        [DataMember(Name = "eliteLevel")] public int EliteLevel;
        [DataMember(Name = "buffCanonicalSummary")] public string BuffCanonicalSummary;
        [DataMember(Name = "unitIds")] public string[] UnitIds;
    }

    [DataContract]
    public sealed class MatchTargetedBuffWire
    {
        [DataMember(Name = "buffInstanceId")] public string BuffInstanceId;
        [DataMember(Name = "buffTypeId")] public string BuffTypeId;
        [DataMember(Name = "targetUnitId")] public string TargetUnitId;
        [DataMember(Name = "canonicalPayload")] public string CanonicalPayload;
        [DataMember(Name = "discardPolicy")] public string DiscardPolicy;
    }

    [DataContract]
    public sealed class MatchGlobalBuffWire
    {
        [DataMember(Name = "buffInstanceId")] public string BuffInstanceId;
        [DataMember(Name = "buffTypeId")] public string BuffTypeId;
        [DataMember(Name = "canonicalPayload")] public string CanonicalPayload;
    }

    [DataContract]
    public sealed class MatchSourceEffectWire
    {
        [DataMember(Name = "effectInstanceId")] public string EffectInstanceId;
        [DataMember(Name = "effectTypeId")] public string EffectTypeId;
        [DataMember(Name = "canonicalPayload")] public string CanonicalPayload;
    }

    public static class MatchProtocol
    {
        public const int ProtocolVersion = 1;
        public const int SchemaVersion = 1;
        public const int AbsoluteMaximumFrameBytes = 4 * 1024 * 1024;
        public const int ControlPayloadMaximum = 64 * 1024;
        public const int ScopedSnapshotPayloadMaximum = 1024 * 1024;
        public const int BattleSealPayloadMaximum = AbsoluteMaximumFrameBytes - sizeof(int);
        public static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static MatchWireDirection GetAllowedDirection(MatchWireKind kind)
        {
            switch (kind)
            {
                case MatchWireKind.Handshake:
                case MatchWireKind.Command:
                case MatchWireKind.SnapshotRequest:
                case MatchWireKind.FirstChunkReady:
                case MatchWireKind.FinalSecondHash:
                case MatchWireKind.ClientBattleFailure:
                case MatchWireKind.ReconnectRequest:
                case MatchWireKind.ExplicitQuit:
                    return MatchWireDirection.ClientToHost;
                case MatchWireKind.HandshakeAccepted:
                case MatchWireKind.Reject:
                case MatchWireKind.MatchInitialized:
                case MatchWireKind.CommandAck:
                case MatchWireKind.ScopedSnapshot:
                case MatchWireKind.ClockSync:
                case MatchWireKind.BattleSeal:
                case MatchWireKind.PlaybackStart:
                case MatchWireKind.PlaybackClock:
                case MatchWireKind.ReconnectAccepted:
                case MatchWireKind.ReconnectRejected:
                case MatchWireKind.MatchEnded:
                    return MatchWireDirection.HostToClient;
                case MatchWireKind.Ping:
                case MatchWireKind.Pong:
                    return MatchWireDirection.Bidirectional;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static byte[] Encode(
            MatchWireKind kind,
            string sessionId,
            string messageId,
            object payload,
            MatchWireDirection direction)
        {
            var envelope = new MatchWireEnvelope
            {
                ProtocolVersion = ProtocolVersion,
                SchemaVersion = SchemaVersion,
                Kind = kind.ToString(),
                SessionId = sessionId,
                MessageId = messageId,
                Payload = MatchJson.Serialize(payload)
            };
            if (!ValidateEnvelope(envelope, direction, out var error))
            {
                throw new ArgumentException("The match message is invalid: " + error + ".", nameof(payload));
            }

            var bytes = StrictUtf8.GetBytes(MatchJson.Serialize(envelope));
            if (bytes.Length > MaximumPayloadBytes(kind))
            {
                throw new ArgumentException("The match payload exceeds its kind-specific maximum.", nameof(payload));
            }
            return FrameForTests(bytes);
        }

        public static bool TryDecode(
            byte[] frame,
            MatchWireDirection direction,
            out MatchWireEnvelope envelope,
            out MatchProtocolError error)
        {
            envelope = null;
            if (frame == null || frame.Length == 0)
            {
                error = MatchProtocolError.EmptyFrame;
                return false;
            }
            if (frame.Length > AbsoluteMaximumFrameBytes)
            {
                error = MatchProtocolError.FrameTooLarge;
                return false;
            }
            if (frame.Length <= sizeof(int))
            {
                error = MatchProtocolError.InvalidFrameLength;
                return false;
            }
            var payloadLength = ReadLength(frame);
            if (payloadLength <= 0 || payloadLength != frame.Length - sizeof(int))
            {
                error = MatchProtocolError.InvalidFrameLength;
                return false;
            }

            string json;
            try { json = StrictUtf8.GetString(frame, sizeof(int), payloadLength); }
            catch (DecoderFallbackException)
            {
                error = MatchProtocolError.InvalidUtf8;
                return false;
            }
            try { envelope = MatchJson.Deserialize<MatchWireEnvelope>(json); }
            catch (Exception)
            {
                error = MatchProtocolError.InvalidJson;
                return false;
            }
            if (!ValidateEnvelope(envelope, direction, out error))
            {
                envelope = null;
                return false;
            }
            if (!Enum.TryParse(envelope.Kind, false, out MatchWireKind kind))
            {
                envelope = null;
                error = MatchProtocolError.UnknownMessageKind;
                return false;
            }
            if (payloadLength > MaximumPayloadBytes(kind))
            {
                envelope = null;
                error = MatchProtocolError.PayloadTooLarge;
                return false;
            }
            return true;
        }

        public static bool TryDeserializePayload<T>(
            MatchWireEnvelope envelope,
            out T payload)
            where T : class
        {
            payload = null;
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Payload)) return false;
            try
            {
                payload = MatchJson.Deserialize<T>(envelope.Payload);
                return payload != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static byte[] FrameForTests(byte[] payload)
        {
            if (payload == null || payload.Length == 0
                || payload.Length + sizeof(int) > AbsoluteMaximumFrameBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            var frame = new byte[payload.Length + sizeof(int)];
            frame[0] = (byte)(payload.Length >> 24);
            frame[1] = (byte)(payload.Length >> 16);
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, frame, sizeof(int), payload.Length);
            return frame;
        }

        private static bool ValidateEnvelope(
            MatchWireEnvelope envelope,
            MatchWireDirection direction,
            out MatchProtocolError error)
        {
            if (envelope == null)
            {
                error = MatchProtocolError.InvalidJson;
                return false;
            }
            if (envelope.ProtocolVersion != ProtocolVersion)
            {
                error = MatchProtocolError.UnsupportedProtocolVersion;
                return false;
            }
            if (envelope.SchemaVersion != SchemaVersion)
            {
                error = MatchProtocolError.UnsupportedSchemaVersion;
                return false;
            }
            if (!Enum.TryParse(envelope.Kind, false, out MatchWireKind kind)
                || !Enum.IsDefined(typeof(MatchWireKind), kind))
            {
                error = MatchProtocolError.UnknownMessageKind;
                return false;
            }
            if ((GetAllowedDirection(kind) & direction) == 0)
            {
                error = MatchProtocolError.DirectionNotAllowed;
                return false;
            }
            if (!ValidToken(envelope.SessionId, 128)
                || !ValidToken(envelope.MessageId, 128)
                || string.IsNullOrWhiteSpace(envelope.Payload))
            {
                error = MatchProtocolError.MissingRequiredField;
                return false;
            }
            if (!ValidatePayload(
                kind,
                envelope.SessionId,
                envelope.Payload))
            {
                error = MatchProtocolError.InvalidPayload;
                return false;
            }
            error = MatchProtocolError.None;
            return true;
        }

        private static bool ValidatePayload(
            MatchWireKind kind,
            string sessionId,
            string json)
        {
            switch (kind)
            {
                case MatchWireKind.Handshake:
                    return DeserializeAnd(json, (MatchHandshakePayload value) =>
                        ValidPlayerId(value.PlayerId) && value.Manifest != null && value.Manifest.IsValid);
                case MatchWireKind.HandshakeAccepted:
                    return DeserializeAnd(json, (MatchHandshakeAcceptedPayload value) =>
                        ValidToken(value.ConnectionId, 128) && value.ConnectionGeneration > 0);
                case MatchWireKind.Reject:
                    return DeserializeAnd(json, (MatchRejectPayload value) =>
                        ValidToken(value.Code, 128) && ValidDetail(value.StableDetailCode));
                case MatchWireKind.MatchInitialized:
                    return DeserializeAnd(json, (MatchInitializedPayload value) =>
                        ValidPlayerId(value.PlayerId)
                        && value.SeatIndex >= 1 && value.SeatIndex <= 4
                        && value.ConnectionGeneration > 0
                        && ValidPlayerId(value.HostPlayerId)
                        && ValidToken(value.MatchSeed, 256)
                        && ReconnectTokenIssuer.IsValidRawToken(
                            value.ReconnectToken)
                        && value.Manifest != null && value.Manifest.IsValid
                        && ValidateSnapshot(value.Snapshot)
                        && string.Equals(
                            sessionId,
                            value.Snapshot.SessionId,
                            StringComparison.Ordinal)
                        && ValidateClock(value.Clock));
                case MatchWireKind.Command:
                    return DeserializeAnd(json, (MatchCommandWirePayload value) =>
                        ValidPlayerId(value.PlayerId)
                        && value.ConnectionGeneration > 0
                        && ValidToken(value.CommandId, 128)
                        && value.KnownStateRevision >= 0
                        && ValidateCommand(value));
                case MatchWireKind.CommandAck:
                    return DeserializeAnd(json, (MatchCommandAckPayload value) =>
                        ValidToken(value.CommandId, 128)
                        && EnumToken<MatchCommandCode>(value.ResultCode)
                        && value.CurrentStateRevision >= 0
                        && (!value.HasAcceptedStateRevision || value.AcceptedStateRevision >= 0)
                        && ValidDetail(value.StableDetailCode)
                        && value.HostAcceptSequence > 0);
                case MatchWireKind.ScopedSnapshot:
                    return DeserializeAnd<ScopedSnapshotPayload>(
                        json,
                        value => ValidateSnapshot(value)
                            && string.Equals(
                                sessionId,
                                value.SessionId,
                                StringComparison.Ordinal));
                case MatchWireKind.SnapshotRequest:
                    return DeserializeAnd(json, (MatchSnapshotRequestPayload value) =>
                        value.ClientLastAppliedRevision >= 0);
                case MatchWireKind.ClockSync:
                    return DeserializeAnd<MatchClockSyncPayload>(json, ValidateClock);
                case MatchWireKind.BattleSeal:
                    return DeserializeAnd(json, (MatchBattleSealPayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleSetId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && value.SealedPayload != null
                        && (value.BattleInputs == null
                            || value.BattleInputs.Length <= 2
                            && value.BattleInputs.All(item =>
                                item != null
                                && ValidToken(item.BattleId, 128)
                                && IsSha256(item.InputSha256)
                                && (string.IsNullOrEmpty(item.SealedInputHash)
                                    || IsSha256(item.SealedInputHash)))
                            && value.BattleInputs
                                .GroupBy(item => item.BattleId, StringComparer.Ordinal)
                                .All(group => group.Count() == 1)));
                case MatchWireKind.FirstChunkReady:
                    return DeserializeAnd(json, (MatchFirstChunkReadyPayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleSetId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && value.ReadyRevision >= 0);
                case MatchWireKind.PlaybackStart:
                    return DeserializeAnd(json, (MatchPlaybackStartPayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleSetId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && value.HostMonotonicStartMs >= 0
                        && value.StartTick >= 0);
                case MatchWireKind.PlaybackClock:
                    return DeserializeAnd(json, (MatchPlaybackClockPayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleSetId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && value.HostMonotonicNowMs >= 0
                        && value.CurrentTick >= 0);
                case MatchWireKind.FinalSecondHash:
                    return DeserializeAnd(json, (MatchFinalSecondHashPayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && (string.IsNullOrEmpty(value.BattleInputSha256)
                            || IsSha256(value.BattleInputSha256))
                        && IsSha256(value.FinalSecondSha256));
                case MatchWireKind.ClientBattleFailure:
                    return DeserializeAnd(json, (MatchClientBattleFailurePayload value) =>
                        value.RoundNumber > 0
                        && ValidToken(value.BattleId, 128)
                        && IsSha256(value.CanonicalInputHash)
                        && ValidDetail(value.StableDetailCode));
                case MatchWireKind.ReconnectRequest:
                    return DeserializeAnd(json, (MatchReconnectRequestPayload value) =>
                        ValidPlayerId(value.PlayerId)
                        && ReconnectTokenIssuer.IsValidRawToken(
                            value.RawToken)
                        && value.Manifest != null && value.Manifest.IsValid
                        && value.ClientLastAppliedRevision >= 0);
                case MatchWireKind.ReconnectAccepted:
                    return DeserializeAnd(json, (MatchReconnectAcceptedPayload value) =>
                        ValidPlayerId(value.PlayerId)
                        && value.SeatIndex >= 1 && value.SeatIndex <= 4
                        && value.ConnectionGeneration > 0
                        && (string.IsNullOrEmpty(value.ReconnectToken)
                            || ReconnectTokenIssuer.IsValidRawToken(
                                value.ReconnectToken)));
                case MatchWireKind.ReconnectRejected:
                    return DeserializeAnd(json, (MatchReconnectRejectedPayload value) =>
                        EnumToken<MatchReconnectRejectCode>(value.Code)
                        && ValidDetail(value.StableDetailCode));
                case MatchWireKind.ExplicitQuit:
                    return DeserializeAnd(json, (MatchExplicitQuitPayload value) =>
                        ValidPlayerId(value.PlayerId) && value.ConnectionGeneration > 0);
                case MatchWireKind.MatchEnded:
                    return DeserializeAnd(json, (MatchEndedPayload value) =>
                        EnumToken<MatchEndReason>(value.EndReason)
                        && value.EndReason != MatchEndReason.None.ToString()
                        && value.FinalRevision >= 0
                        && value.FinalStandings != null
                        && value.FinalStandings.Length <= 4
                        && value.FinalStandings.All(ValidateStanding));
                case MatchWireKind.Ping:
                case MatchWireKind.Pong:
                    return DeserializeAnd(json, (MatchHeartbeatPayload value) =>
                        value.ConnectionGeneration > 0 && value.SentUnixMilliseconds >= 0);
                default:
                    return false;
            }
        }

        private static bool ValidateSnapshot(ScopedSnapshotPayload value)
        {
            if (value == null
                || !ValidToken(value.SessionId, 128)
                || value.StateRevision < 0
                || value.PublicState == null
                || !string.Equals(value.SessionId, value.PublicState.SessionId, StringComparison.Ordinal)
                || value.StateRevision != value.PublicState.StateRevision
                || !EnumToken<MatchLocalConnectionState>(value.LocalConnectionState)
                || !EnumToken<MatchPhase>(value.PublicState.Phase)
                || !EnumToken<MatchEndReason>(value.PublicState.EndReason)
                || value.PublicState.RoundNumber < 0
                || value.PublicState.PreparationRemainingMs < 0
                || value.PublicState.Pairings == null
                || value.PublicState.Pairings.Length > 2
                || value.PublicState.FinalStandings == null
                || value.PublicState.FinalStandings.Length > 4
                || value.PublicState.Seats == null
                || value.PublicState.Seats.Length > 4)
            {
                return false;
            }
            if (value.PublicState.Seats
                    .GroupBy(seat => seat == null ? -1 : seat.SeatIndex)
                    .Any(group => group.Count() != 1)
                || value.PublicState.Seats
                    .Where(seat => seat != null)
                    .GroupBy(seat => seat.PlayerId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1))
            {
                return false;
            }
            if (value.OwnerPrivateState != null
                && (!ValidPlayerId(value.OwnerPrivateState.PlayerId)
                    || value.OwnerPrivateState.Units == null
                    || value.OwnerPrivateState.ShopOffers == null
                    || value.OwnerPrivateState.OverflowUnits == null
                    || value.OwnerPrivateState.StagingStacks == null
                    || value.OwnerPrivateState.TargetedUnitBuffs == null
                    || value.OwnerPrivateState.GlobalBuffs == null
                    || value.OwnerPrivateState.SourceEffects == null
                    || value.OwnerPrivateState.Gold < 0
                    || value.OwnerPrivateState.Level
                        < MatchEconomyRules.MinimumLevel
                    || value.OwnerPrivateState.Level
                        > MatchEconomyRules.MaximumLevel
                    || value.OwnerPrivateState.UpgradeDiscountCountAtThisLevel < 0
                    || value.OwnerPrivateState.CurrentUpgradePrice < 0
                    || value.OwnerPrivateState.SuccessfulShopPurchaseCount < 0
                    || value.OwnerPrivateState.TotalDeploymentCost < 0
                    || value.OwnerPrivateState.AvailableDeploymentCost < 0
                    || value.OwnerPrivateState.StreakCount < 0
                    || !EnumToken<MatchStreakKind>(
                        value.OwnerPrivateState.StreakKind)))
            {
                return false;
            }
            return (value.OwnerPrivateState == null
                    || value.PublicState.Seats.Any(seat =>
                        seat != null
                        && string.Equals(
                            seat.PlayerId,
                            value.OwnerPrivateState.PlayerId,
                            StringComparison.Ordinal)))
                && value.PublicState.FinalStandings.All(ValidateStanding)
                && value.PublicState.Pairings.All(pairing =>
                    pairing != null
                    && ValidToken(pairing.BattleId, 128)
                    && EnumToken<MatchPairingKind>(pairing.Kind)
                    && ValidPlayerId(pairing.HomePlayerId)
                    && ValidPlayerId(pairing.AwayPlayerId)
                    && (pairing.Kind == MatchPairingKind.Shadow.ToString()
                        ? ValidPlayerId(pairing.ShadowOwnerPlayerId)
                        : string.IsNullOrEmpty(
                            pairing.ShadowOwnerPlayerId)))
                && value.PublicState.Seats.All(seat =>
                    seat != null
                    && seat.SeatIndex >= 1 && seat.SeatIndex <= 4
                    && ValidPlayerId(seat.PlayerId)
                    && ValidToken(seat.DisplayName, 128)
                    && ValidToken(seat.AvatarId, 128)
                    && seat.Life >= 0
                    && (!seat.HasPlacement || seat.Placement >= 1)
                    && EnumToken<PublicConnectionState>(seat.ConnectionState)
                    && seat.Units != null
                    && seat.Units.All(ValidateUnit)
                    && (seat.TargetedUnitBuffs == null
                        || seat.TargetedUnitBuffs.All(ValidateTargetedBuff))
                    && (seat.GlobalBuffs == null
                        || seat.GlobalBuffs.All(ValidateGlobalBuff))
                    && (seat.SourceEffects == null
                        || seat.SourceEffects.All(ValidateSourceEffect)))
                && (value.OwnerPrivateState == null
                    || ValidateOwner(value.OwnerPrivateState));
        }

        private static bool ValidateClock(MatchClockSyncPayload value)
        {
            return value != null
                && value.RoundNumber >= 0
                && EnumToken<MatchPhase>(value.Phase)
                && value.HostMonotonicNowMs >= 0
                && value.PreparationDeadlineHostMonotonicMs >= 0;
        }

        private static bool ValidateStanding(MatchStandingWire value)
        {
            return value != null
                && ValidPlayerId(value.PlayerId)
                && (!value.HasPlacement || value.Placement >= 1);
        }

        private static bool ValidateUnit(MatchUnitWire value)
        {
            return value != null
                && ValidToken(value.UnitId, 128)
                && ValidToken(value.TypeId, 128)
                && EnumToken<MatchUnitZone>(value.Zone)
                && value.EliteLevel >= 0
                && value.EliteLevel <= 3
                && (!value.HasFormation
                    || new MatchFormationPosition(
                        value.FormationX,
                        value.FormationY).IsValid)
                && value.AcquisitionOrdinal >= 0
                && value.Buffs != null
                && value.Buffs.All(buff =>
                    buff != null
                    && ValidToken(buff.BuffId, 128)
                    && buff.CanonicalPayload != null
                    && buff.CanonicalPayload.Length <= 4096);
        }

        private static bool ValidateCommand(
            MatchCommandWirePayload value)
        {
            if (!value.TryToDomain("validation-session", out _)
                || !Enum.TryParse(
                    value.CommandKind,
                    false,
                    out MatchCommandKind kind))
            {
                return false;
            }
            switch (kind)
            {
                case MatchCommandKind.SetPreparationReady:
                case MatchCommandKind.RefreshShop:
                case MatchCommandKind.ToggleShopFreeze:
                    return true;
                case MatchCommandKind.PurchaseShopOffer:
                    return value.SlotIndex >= 1
                        && value.SlotIndex <= MatchEconomyRules.ShopSlotCount
                        && ValidToken(value.ExpectedUnitId, 128);
                case MatchCommandKind.PurchaseLevelUpgrade:
                    return value.ExpectedCurrentLevel
                            >= MatchEconomyRules.MinimumLevel
                        && value.ExpectedCurrentLevel
                            <= MatchEconomyRules.MaximumLevel
                        && value.ExpectedCurrentPrice >= 0;
                case MatchCommandKind.DeployUnit:
                    return ValidToken(value.UnitId, 128)
                        && value.ExpectedAvailableCost >= 0
                        && new MatchFormationPosition(
                            value.TargetX,
                            value.TargetY).IsValid;
                case MatchCommandKind.ReplaceDeployedUnit:
                    return ValidToken(value.StagingUnitId, 128)
                        && ValidToken(
                            value.ExpectedDeployedUnitId,
                            128)
                        && new MatchFormationPosition(
                            value.TargetX,
                            value.TargetY).IsValid;
                case MatchCommandKind.RelocateOrSwapUnit:
                    return ValidToken(value.UnitId, 128)
                        && new MatchFormationPosition(
                            value.TargetX,
                            value.TargetY).IsValid;
                case MatchCommandKind.RetreatUnit:
                    return ValidToken(value.UnitId, 128);
                default:
                    return false;
            }
        }

        private static bool ValidateOwner(OwnerMatchStateWire value)
        {
            return value.Units.All(ValidateUnit)
                && value.OverflowUnits.All(ValidateUnit)
                && value.ShopOffers.Length
                    <= MatchEconomyRules.ShopSlotCount
                && value.ShopOffers.All(ValidateShopOffer)
                && value.StagingStacks.All(ValidateStagingStack)
                && value.TargetedUnitBuffs.All(buff =>
                    buff != null
                    && ValidToken(buff.BuffInstanceId, 128)
                    && ValidToken(buff.BuffTypeId, 128)
                    && ValidToken(buff.TargetUnitId, 128)
                    && ValidCanonicalPayload(buff.CanonicalPayload)
                    && EnumToken<MatchTargetedBuffDiscardPolicy>(
                        buff.DiscardPolicy))
                && value.GlobalBuffs.All(buff =>
                    buff != null
                    && ValidToken(buff.BuffInstanceId, 128)
                    && ValidToken(buff.BuffTypeId, 128)
                    && ValidCanonicalPayload(buff.CanonicalPayload))
                && value.SourceEffects.All(effect =>
                    effect != null
                    && ValidToken(effect.EffectInstanceId, 128)
                    && ValidToken(effect.EffectTypeId, 128)
                    && ValidCanonicalPayload(
                        effect.CanonicalPayload));
        }

        private static bool ValidateTargetedBuff(
            MatchTargetedBuffWire buff)
        {
            return buff != null
                && ValidToken(buff.BuffInstanceId, 128)
                && ValidToken(buff.BuffTypeId, 128)
                && ValidToken(buff.TargetUnitId, 128)
                && ValidCanonicalPayload(buff.CanonicalPayload)
                && EnumToken<MatchTargetedBuffDiscardPolicy>(
                    buff.DiscardPolicy);
        }

        private static bool ValidateGlobalBuff(
            MatchGlobalBuffWire buff)
        {
            return buff != null
                && ValidToken(buff.BuffInstanceId, 128)
                && ValidToken(buff.BuffTypeId, 128)
                && ValidCanonicalPayload(buff.CanonicalPayload);
        }

        private static bool ValidateSourceEffect(
            MatchSourceEffectWire effect)
        {
            return effect != null
                && ValidToken(effect.EffectInstanceId, 128)
                && ValidToken(effect.EffectTypeId, 128)
                && ValidCanonicalPayload(effect.CanonicalPayload);
        }

        private static bool ValidateShopOffer(
            MatchShopOfferWire value)
        {
            if (value == null
                || value.SlotIndex < 1
                || value.SlotIndex > MatchEconomyRules.ShopSlotCount)
            {
                return false;
            }
            if (string.IsNullOrEmpty(value.UnitId))
            {
                return string.IsNullOrEmpty(value.TypeId)
                    && !value.HasRarity;
            }
            return ValidToken(value.UnitId, 128)
                && ValidToken(value.TypeId, 128)
                && value.HasRarity
                && value.Rarity >= 1
                && value.Rarity <= 6;
        }

        private static bool ValidateStagingStack(
            MatchStagingStackWire value)
        {
            return value != null
                && ValidToken(value.TypeId, 128)
                && value.NumericTypeId >= 0
                && value.DeploymentCost >= 0
                && value.EliteLevel >= 0
                && value.EliteLevel <= 3
                && ValidCanonicalPayload(
                    value.BuffCanonicalSummary)
                && value.UnitIds != null
                && value.UnitIds.All(unitId =>
                    ValidToken(unitId, 128))
                && value.UnitIds.Distinct(
                    StringComparer.Ordinal).Count()
                    == value.UnitIds.Length;
        }

        private static bool ValidCanonicalPayload(string value)
        {
            return value != null && value.Length <= 65536;
        }

        private static bool DeserializeAnd<T>(string json, Func<T, bool> validator)
            where T : class
        {
            try
            {
                var value = MatchJson.Deserialize<T>(json);
                return value != null && validator(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int MaximumPayloadBytes(MatchWireKind kind)
        {
            if (kind == MatchWireKind.ScopedSnapshot
                || kind == MatchWireKind.MatchInitialized)
            {
                return ScopedSnapshotPayloadMaximum;
            }
            if (kind == MatchWireKind.BattleSeal) return BattleSealPayloadMaximum;
            return ControlPayloadMaximum;
        }

        private static int ReadLength(byte[] frame)
        {
            return (frame[0] << 24)
                | (frame[1] << 16)
                | (frame[2] << 8)
                | frame[3];
        }

        private static bool ValidPlayerId(string value)
        {
            return LocalProfileIdentity.IsValid(value);
        }

        private static bool ValidToken(string value, int maximumLength)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
        }

        private static bool ValidDetail(string value)
        {
            return ValidToken(value, 256);
        }

        private static bool EnumToken<T>(string value) where T : struct
        {
            return Enum.TryParse(value, false, out T parsed)
                && Enum.IsDefined(typeof(T), parsed);
        }

        private static bool IsSha256(string value)
        {
            return value != null
                && value.Length == 64
                && value.All(character =>
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }
    }

    internal static class MatchJson
    {
        public static string Serialize<T>(T value)
        {
            if (ReferenceEquals(value, null)) throw new ArgumentNullException(nameof(value));
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static string Serialize(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var serializer = new DataContractJsonSerializer(value.GetType());
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new SerializationException("JSON is empty.");
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
