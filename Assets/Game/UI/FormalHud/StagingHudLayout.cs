using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArknoNights.UI
{
    /// <summary>Pure UI_SPEC sizing and placement math. It has no PlayerState or scene dependency.</summary>
    public static class StagingHudLayout
    {
        public const int MaximumSlotCount = 13;

        public readonly struct Slot
        {
            public Slot(int index, float x, float width, float offsetY, bool selected)
            {
                Index = index;
                X = x;
                Width = width;
                OffsetY = offsetY;
                Selected = selected;
            }

            public int Index { get; }
            public float X { get; }
            public float Width { get; }
            public float OffsetY { get; }
            public bool Selected { get; }
        }

        public sealed class Result
        {
            internal Result(float portraitSize, float slotHeight, float startX, bool compressed, IReadOnlyList<Slot> slots)
            {
                PortraitSize = portraitSize;
                SlotHeight = slotHeight;
                StartX = startX;
                IsCompressed = compressed;
                Slots = slots;
            }

            public float PortraitSize { get; }
            public float SlotHeight { get; }
            public float StartX { get; }
            public bool IsCompressed { get; }
            public IReadOnlyList<Slot> Slots { get; }
        }

        public static Result Calculate(float screenWidth, float screenHeight, int slotCount, int selectedIndex = -1)
        {
            if (screenWidth < 0f) throw new ArgumentOutOfRangeException(nameof(screenWidth));
            if (screenHeight < 0f) throw new ArgumentOutOfRangeException(nameof(screenHeight));
            if (slotCount < 0 || slotCount > MaximumSlotCount) throw new ArgumentOutOfRangeException(nameof(slotCount));

            var portrait = screenHeight / 6f;
            var slotHeight = portrait * 10f / 9f;
            if (slotCount == 0) return new Result(portrait, slotHeight, screenWidth, false, Array.Empty<Slot>());

            var naturalTotal = slotCount * portrait;
            var compressed = naturalTotal > screenWidth;
            var widths = new float[slotCount];
            if (!compressed)
            {
                for (var index = 0; index < slotCount; index++) widths[index] = portrait;
            }
            else
            {
                var baseWidth = screenWidth / slotCount;
                for (var index = 0; index < slotCount; index++) widths[index] = baseWidth;
                if (selectedIndex >= 0 && selectedIndex < slotCount)
                    ApplyCompressedSelection(widths, selectedIndex, screenWidth, portrait);
            }

            var startX = compressed ? 0f : screenWidth - naturalTotal;
            var selectedOffset = portrait / 9f;
            var slots = new List<Slot>(slotCount);
            var x = startX;
            for (var index = 0; index < slotCount; index++)
            {
                var selected = index == selectedIndex;
                slots.Add(new Slot(index, x, widths[index], selected ? selectedOffset : 0f, selected));
                x += widths[index];
            }
            return new Result(portrait, slotHeight, startX, compressed, slots);
        }

        private static void ApplyCompressedSelection(float[] widths, int selectedIndex, float screenWidth, float naturalWidth)
        {
            var minimum = naturalWidth * 7f / 9f;
            var nonSelected = widths.Length - 1;
            if (nonSelected == 0)
            {
                widths[selectedIndex] = naturalWidth;
                return;
            }

            // UI_SPEC permits an arithmetic falloff until it reaches the minimum width.
            // Solve the slope by bisection so the selected natural width plus clamped falloff
            // exactly occupies the available width, including first/last slot selection.
            var requiredForOthers = screenWidth - naturalWidth;
            var minimumTotal = nonSelected * minimum;
            if (requiredForOthers < minimumTotal - 0.01f)
                throw new InvalidOperationException("UI_SPEC minimum compressed widths do not fit the available width.");

            var low = 0f;
            var high = naturalWidth - minimum;
            for (var iteration = 0; iteration < 48; iteration++)
            {
                var slope = (low + high) * 0.5f;
                var total = 0f;
                for (var index = 0; index < widths.Length; index++)
                {
                    if (index == selectedIndex) continue;
                    total += Mathf.Max(minimum, naturalWidth - slope * Mathf.Abs(index - selectedIndex));
                }
                if (total > requiredForOthers) low = slope; else high = slope;
            }

            widths[selectedIndex] = naturalWidth;
            var solvedSlope = high;
            for (var index = 0; index < widths.Length; index++)
            {
                if (index == selectedIndex) continue;
                widths[index] = Mathf.Max(minimum, naturalWidth - solvedSlope * Mathf.Abs(index - selectedIndex));
            }

            // Floating-point bisection can leave a negligible remainder. Keep it inside the
            // final flexible slot without violating the specified minimum width.
            var currentTotal = 0f;
            for (var index = 0; index < widths.Length; index++) currentTotal += widths[index];
            var difference = screenWidth - currentTotal;
            for (var index = widths.Length - 1; index >= 0 && Mathf.Abs(difference) > 0.001f; index--)
            {
                if (index == selectedIndex) continue;
                var candidate = widths[index] + difference;
                if (candidate < minimum) continue;
                widths[index] = candidate;
                break;
            }
        }
    }
}
