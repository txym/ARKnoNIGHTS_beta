using System;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Presentation
{
    public interface IBattlePresentationView : IDisposable
    {
        void SetWorldPosition(Vector3 position);
        void SetFacing(Vector3 direction);
        void SetPlaybackSpeed(float playbackSpeed);
        void PlayMove();
        void PlayAttack(float animationSpeedMultiplier);
        void PlayHit();
        void PlayDeath();
        void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield);
    }

    public interface IBattlePresentationViewFactory
    {
        bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic);
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
        public override string ToString() => Code + ": " + Message + " @ " + Tick + "/" + Sequence;
    }

    public sealed class BattlePresentationViewState
    {
        internal BattlePresentationViewState(string unitId, string typeId, BattleSide side, FixedPosition position, int hitPoints, bool isAlive, int eliteLevel)
        {
            UnitId = unitId;
            TypeId = typeId;
            Side = side;
            Position = position;
            HitPoints = hitPoints;
            IsAlive = isAlive;
            EliteLevel = eliteLevel;
        }

        public string UnitId { get; }
        public string TypeId { get; }
        public BattleSide Side { get; }
        public FixedPosition Position { get; }
        public int HitPoints { get; }
        public bool IsAlive { get; }
        public int EliteLevel { get; }
    }
}
