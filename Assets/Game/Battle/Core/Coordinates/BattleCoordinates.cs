using System;

namespace ArknoNights.Battle.Core
{
    /// <summary>One-based coordinate in a player's 9 by 4 local formation.</summary>
    public readonly struct FormationCoordinate : IEquatable<FormationCoordinate>
    {
        public const int Width = 9;
        public const int Height = 4;

        public FormationCoordinate(int x, int y)
        {
            if (!IsWithinBounds(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Formation coordinates must be within 1..9 by 1..4.");
            }

            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public bool IsValid => IsWithinBounds(X, Y);

        public static bool IsWithinBounds(int x, int y) => x >= 1 && x <= Width && y >= 1 && y <= Height;
        public static bool TryCreate(int x, int y, out FormationCoordinate coordinate)
        {
            if (!IsWithinBounds(x, y))
            {
                coordinate = default(FormationCoordinate);
                return false;
            }

            coordinate = new FormationCoordinate(x, y);
            return true;
        }

        public bool Equals(FormationCoordinate other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FormationCoordinate other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => X + "," + Y;
    }

    /// <summary>One-based coordinate in the authoritative 9 by 8 battlefield.</summary>
    public readonly struct BattlefieldCoordinate : IEquatable<BattlefieldCoordinate>
    {
        public const int Width = 9;
        public const int Height = 8;

        public BattlefieldCoordinate(int x, int y)
        {
            if (!IsWithinBounds(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Battlefield coordinates must be within 1..9 by 1..8.");
            }

            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
        public bool IsValid => IsWithinBounds(X, Y);

        public static bool IsWithinBounds(int x, int y) => x >= 1 && x <= Width && y >= 1 && y <= Height;
        public static bool TryCreate(int x, int y, out BattlefieldCoordinate coordinate)
        {
            if (!IsWithinBounds(x, y))
            {
                coordinate = default(BattlefieldCoordinate);
                return false;
            }

            coordinate = new BattlefieldCoordinate(x, y);
            return true;
        }

        public bool Equals(BattlefieldCoordinate other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is BattlefieldCoordinate other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => X + "," + Y;
    }

    public static class BattlefieldRules
    {
        public static readonly BattlefieldCoordinate BlueGate = new BattlefieldCoordinate(5, 1);
        public static readonly BattlefieldCoordinate RedGate = new BattlefieldCoordinate(5, 8);

        public static bool IsGate(BattlefieldCoordinate coordinate) => coordinate.Equals(BlueGate) || coordinate.Equals(RedGate);
        public static bool IsDeployable(BattlefieldCoordinate coordinate) => coordinate.IsValid && !IsGate(coordinate);
        public static BattlefieldCoordinate Rotate180(BattlefieldCoordinate coordinate) => new BattlefieldCoordinate(10 - coordinate.X, 9 - coordinate.Y);
        public static BattlefieldCoordinate MapHome(FormationCoordinate coordinate) => new BattlefieldCoordinate(coordinate.X, coordinate.Y);
        public static BattlefieldCoordinate MapAway(FormationCoordinate coordinate) => Rotate180(MapHome(coordinate));
    }

    /// <summary>Deterministic logic position. One metre is 100 fixed units (one centimetre).</summary>
    public readonly struct FixedPosition : IEquatable<FixedPosition>
    {
        public const int UnitsPerMetre = 100;
        public const int QuarterMetre = 25;

        public FixedPosition(int xUnits, int yUnits)
        {
            XUnits = xUnits;
            YUnits = yUnits;
        }

        public int XUnits { get; }
        public int YUnits { get; }
        public static FixedPosition FromCell(BattlefieldCoordinate coordinate) => new FixedPosition(coordinate.X * UnitsPerMetre, coordinate.Y * UnitsPerMetre);
        public bool Equals(FixedPosition other) => XUnits == other.XUnits && YUnits == other.YUnits;
        public override bool Equals(object obj) => obj is FixedPosition other && Equals(other);
        public override int GetHashCode() => (XUnits * 397) ^ YUnits;
        public override string ToString() => XUnits + "," + YUnits;
    }
}
