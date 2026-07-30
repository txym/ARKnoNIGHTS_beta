using System;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Presentation
{
    public interface IBattlePresentationView : IDisposable
    {
        bool HasPendingTerminalPresentation => false;
        void SetWorldPosition(Vector3 position);
        void SetFacing(Vector3 direction);
        void SetPlaybackSpeed(float playbackSpeed);
        void SetPresentationState(string stateTag) { }
        void PlayIdle() { }
        void PlayMove();
        void PlayAttack(float animationSpeedMultiplier);
        void PlayHit();
        void PlayDeath();
        void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield);
        void SetStatusBarState(string unitId, bool isEnemy, int maxHitPoints, int currentHitPoints, int currentShield) => SetStatusBarState(unitId, isEnemy, currentHitPoints, currentShield);
    }

    public interface IBattlePresentationViewFactory
    {
        bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic);
    }

    public interface IEliteBattlePresentationViewFactory
        : IBattlePresentationViewFactory
    {
        bool TryCreate(
            string unitId,
            string typeId,
            int eliteLevel,
            out IBattlePresentationView view,
            out BattlePresentationDiagnostic diagnostic);
    }

    /// <summary>Optional capability for views backed by catalog-named Skill clips.</summary>
    public interface IBattleSkillPresentationView
    {
        void PlaySkill(string animationKey, float animationSpeedMultiplier);
    }

    public sealed class BattlePresentationDiagnostic
    {
        public BattlePresentationDiagnostic(string code, string message, int tick = -1, int sequence = -1)
            : this(code, message, string.Empty, string.Empty, tick, sequence)
        {
        }

        public BattlePresentationDiagnostic(string code, string message, string battleId, string unitId, int tick = -1, int sequence = -1)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            BattleId = battleId ?? string.Empty;
            UnitId = unitId ?? string.Empty;
            Tick = tick;
            Sequence = sequence;
        }

        public string Code { get; }
        public string Message { get; }
        public string BattleId { get; }
        public string UnitId { get; }
        public int Tick { get; }
        public int Sequence { get; }
        public override string ToString() => Code + ": " + Message + " battleId=" + BattleId + " unitId=" + UnitId + " tick=" + Tick + " sequence=" + Sequence;
    }

    public sealed class BattlePresentationViewState
    {
        internal BattlePresentationViewState(string unitId, string typeId, BattleSide side, FixedPosition position, int hitPoints, bool isAlive, int eliteLevel)
            : this(unitId, typeId, side, position, new PresentationPosition(position.XUnits / 100d, position.YUnits / 100d), hitPoints, hitPoints, 0, true, isAlive, UnitPresentationAction.Idle, eliteLevel)
        {
        }

        internal BattlePresentationViewState(string unitId, string typeId, BattleSide side, FixedPosition position, PresentationPosition continuousPosition, int maxHitPoints, int hitPoints, int currentShield, bool hasSpawned, bool isAlive, UnitPresentationAction action, int eliteLevel, BattleUnitAttributesSnapshot attributes = null)
        {
            UnitId = unitId;
            TypeId = typeId;
            Side = side;
            Position = position;
            ContinuousPosition = continuousPosition;
            MaxHitPoints = maxHitPoints;
            HitPoints = hitPoints;
            CurrentShield = currentShield;
            HasSpawned = hasSpawned;
            IsAlive = isAlive;
            Action = action;
            EliteLevel = eliteLevel;
            AttributesAvailable = attributes != null;
            Attack = attributes?.Attack ?? 0;
            Defense = attributes?.Defense ?? 0;
            MagicResistance =
                attributes?.MagicResistance ?? 0;
            MoveSpeedCentimetresPerSecond =
                attributes?.MoveSpeedCentimetresPerSecond ?? 0;
            AttackIntervalTicks =
                attributes?.AttackIntervalTicks ?? 0;
            BlockCapacity =
                attributes?.BlockCapacity ?? 0;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public BattleSide Side { get; }
        public FixedPosition Position { get; }
        public PresentationPosition ContinuousPosition { get; }
        public int MaxHitPoints { get; }
        public int HitPoints { get; }
        public int CurrentShield { get; }
        public bool HasSpawned { get; }
        public bool IsAlive { get; }
        public UnitPresentationAction Action { get; }
        public int EliteLevel { get; }
        public bool AttributesAvailable { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MagicResistance { get; }
        public int MoveSpeedCentimetresPerSecond { get; }
        public int AttackIntervalTicks { get; }
        public int BlockCapacity { get; }
    }
}
