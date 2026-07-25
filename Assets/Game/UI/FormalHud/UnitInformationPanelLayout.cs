using System;

namespace ArknoNights.UI
{
    /// <summary>Centralized, uniformly scaled geometry for the UnitInformationPanel upper content area.</summary>
    public sealed class UnitInformationPanelLayout
    {
        public const float ReferencePanelWidth = 720f;
        public const float Figure6ReferenceWidth = 830f;
        public const float Figure6ReferenceHeight = 420f;

        public readonly struct Placement
        {
            public Placement(float left, float top, float width, float height)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            public float Left { get; }
            public float Top { get; }
            public float Width { get; }
            public float Height { get; }
        }

        private UnitInformationPanelLayout(float scale)
        {
            Scale = scale;
            ContentLeft = 12f * scale;
            UnitName = ScalePlacement(215f, 90f, 320f, 43f);
            CombatSummary = ScalePlacement(215f, 148f, 320f, 31f);
            Portrait = ScalePlacement(12f, 200f, 180f, 180f);
            TargetValue = ScalePlacement(12f, 393f, 180f, 52f);
            TargetValueIcon = ScalePlacement(15f, 11f, 30f, 30f);
            TargetValueValue = ScalePlacement(76f, 7f, 84f, 38f);
            StatEntryWidth = 241.5f * scale;
            StatEntryHeight = 52f * scale;
            StatColumnGap = 10f * scale;
            StatRowGap = 10f * scale;
            StatLeft = 215f * scale;
            StatRight = StatLeft + StatEntryWidth + StatColumnGap;
            StatTop = 200f * scale;
            StatIcon = ScalePlacement(15f, 11f, 30f, 30f);
            StatLabel = ScalePlacement(50f, 7f, 90f, 38f);
            StatValue = ScalePlacement(145f, 5f, 76.5f, 42f);
            HealthBarTop = 490f * scale;
        }

        public float Scale { get; }
        public float ContentLeft { get; }
        public Placement UnitName { get; }
        public Placement CombatSummary { get; }
        public Placement Portrait { get; }
        public Placement TargetValue { get; }
        public Placement TargetValueIcon { get; }
        public Placement TargetValueValue { get; }
        public float StatEntryWidth { get; }
        public float StatEntryHeight { get; }
        public float StatColumnGap { get; }
        public float StatRowGap { get; }
        public float StatLeft { get; }
        public float StatRight { get; }
        public float StatTop { get; }
        public Placement StatIcon { get; }
        public Placement StatLabel { get; }
        public Placement StatValue { get; }
        public float HealthBarTop { get; }

        public Placement StatEntry(int column, int row)
        {
            if (column < 0 || column > 1) throw new ArgumentOutOfRangeException(nameof(column));
            if (row < 0 || row > 3) throw new ArgumentOutOfRangeException(nameof(row));
            return new Placement(column == 0 ? StatLeft : StatRight, StatTop + row * (StatEntryHeight + StatRowGap), StatEntryWidth, StatEntryHeight);
        }

        public static UnitInformationPanelLayout ForPanelWidth(float panelWidth)
        {
            if (panelWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(panelWidth));
            return new UnitInformationPanelLayout(panelWidth / ReferencePanelWidth);
        }

        private Placement ScalePlacement(float left, float top, float width, float height) => new Placement(left * Scale, top * Scale, width * Scale, height * Scale);
    }
}
