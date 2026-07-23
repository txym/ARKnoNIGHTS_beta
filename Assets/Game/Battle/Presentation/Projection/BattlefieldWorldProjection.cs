using System;
using ArknoNights.Battle.Core;
using UnityEngine;

namespace ArknoNights.Battle.Presentation
{
    public enum BattleObserverView { Home, Away }

    /// <summary>Pure coordinate projection. It never mutates Core positions or events.</summary>
    public sealed class BattlefieldWorldProjection
    {
        public BattlefieldWorldProjection(Vector3 origin, Vector3 xAxis, Vector3 yAxis)
        {
            if (xAxis.sqrMagnitude <= 0f) throw new ArgumentException("An X axis is required.", nameof(xAxis));
            if (yAxis.sqrMagnitude <= 0f) throw new ArgumentException("A Y axis is required.", nameof(yAxis));
            Origin = origin;
            XAxis = xAxis.normalized;
            YAxis = yAxis.normalized;
        }

        public Vector3 Origin { get; }
        public Vector3 XAxis { get; }
        public Vector3 YAxis { get; }

        public static BattlefieldWorldProjection Default { get; } = new BattlefieldWorldProjection(Vector3.zero, Vector3.right, Vector3.forward);

        public FixedPosition Project(FixedPosition source, BattleObserverView observer)
        {
            return observer == BattleObserverView.Home
                ? source
                : new FixedPosition((BattlefieldCoordinate.Width + 1) * FixedPosition.UnitsPerMetre - source.XUnits, (BattlefieldCoordinate.Height + 1) * FixedPosition.UnitsPerMetre - source.YUnits);
        }

        public Vector3 ToWorld(FixedPosition source, BattleObserverView observer)
        {
            var projected = Project(source, observer);
            // Both Core fixed units and Unity world units use the required 100 units per metre scale.
            return Origin + XAxis * projected.XUnits + YAxis * projected.YUnits;
        }

        public Vector3 ToWorldDirection(FixedPosition from, FixedPosition to, BattleObserverView observer)
        {
            var projectedFrom = Project(from, observer);
            var projectedTo = Project(to, observer);
            var direction = XAxis * (projectedTo.XUnits - projectedFrom.XUnits) + YAxis * (projectedTo.YUnits - projectedFrom.YUnits);
            return direction.sqrMagnitude > 0f ? direction.normalized : Vector3.zero;
        }
    }
}
