using System;
using System.Linq;
using System.Reflection;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Player;
using ArknoNights.UI;
using NUnit.Framework;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class StagingHudLayoutEditModeTests
    {
        [Test]
        public void ZeroSlots_HasNoBackgroundOrSlots()
        {
            var layout = StagingHudLayout.Calculate(1920f, 1080f, 0);
            Assert.AreEqual(0, layout.Slots.Count);
            Assert.AreEqual(200f, layout.SlotHeight, 0.001f);
        }

        [TestCase(1)]
        [TestCase(10)]
        public void NaturalLayouts_AreRightAlignedAndKeepNaturalWidth(int count)
        {
            var layout = StagingHudLayout.Calculate(1920f, 1080f, count);
            Assert.IsFalse(layout.IsCompressed);
            Assert.AreEqual(180f, layout.Slots[0].Width, 0.001f);
            Assert.AreEqual(1920f - count * 180f, layout.Slots[0].X, 0.001f);
            Assert.LessOrEqual(layout.Slots.Last().X + layout.Slots.Last().Width, 1920.001f);
        }

        [Test]
        public void TwelveSlots_CompressToEqual160Widths()
        {
            var layout = StagingHudLayout.Calculate(1920f, 1080f, 12);
            Assert.IsTrue(layout.IsCompressed);
            Assert.That(layout.Slots.Select(slot => slot.Width), Is.All.EqualTo(160f).Within(0.001f));
            Assert.AreEqual(0f, layout.Slots[0].X, 0.001f);
        }

        [TestCase(0)]
        [TestCase(6)]
        [TestCase(12)]
        public void ThirteenCompressedSlots_SelectedSlotIsNaturalAndAllWidthsFit(int selected)
        {
            var layout = StagingHudLayout.Calculate(1920f, 1080f, 13, selected);
            Assert.IsTrue(layout.IsCompressed);
            Assert.AreEqual(180f, layout.Slots[selected].Width, 0.001f);
            Assert.AreEqual(20f, layout.Slots[selected].OffsetY, 0.001f);
            Assert.That(layout.Slots.Where(slot => !slot.Selected).Select(slot => slot.Width), Is.All.GreaterThanOrEqualTo(140f));
            Assert.AreEqual(1920f, layout.Slots.Sum(slot => slot.Width), 0.01f);
            Assert.LessOrEqual(layout.Slots.Last().X + layout.Slots.Last().Width, 1920.01f);
        }

        [Test]
        public void CancelSelection_RestoresStableEqualOrder()
        {
            var selected = StagingHudLayout.Calculate(1920f, 1080f, 12, 5);
            var restored = StagingHudLayout.Calculate(1920f, 1080f, 12);
            Assert.That(restored.Slots.Select(slot => slot.Index), Is.EqualTo(Enumerable.Range(0, 12)));
            Assert.That(restored.Slots.Select(slot => slot.Width), Is.All.EqualTo(160f).Within(0.001f));
            Assert.AreEqual(180f, selected.Slots[5].Width, 0.001f);
        }

        [Test]
        public void PlayerSnapshotOrder_MapsWithoutUiResortingAndHasStableSelectionIds()
        {
            var loaded = LocalPlayerStateLoader.LoadFromResources("BattleData/unit-catalog-v1", "PlayerData/local-player-state-v1");
            Assert.IsTrue(loaded.Success);
            var slots = loaded.State.Snapshot.StagingSlots;
            CollectionAssert.AreEqual(new[] { "1000", "5503" }, slots.Select(slot => slot.TypeId).ToArray());
            var ids = slots.Select(StagingHudController.BuildSlotId).ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count());
            Assert.That(ids[0], Does.Contain("local-1000-alpha"));
            Assert.That(ids[1], Does.Contain("local-5503-alpha"));
        }

        [Test]
        public void UnitPortraitLoader_LoadsTexture2DAsOneCachedSprite()
        {
            const string path = "ProfilePicture/UIImage_1000_gopro";
            var texture = Resources.Load<Texture2D>(path);
            Assert.That(texture, Is.Not.Null, "Portrait must remain a raw Texture2D.");

            var loader = Type.GetType("ArknoNights.UI.UnitPortraitLoader, ARKnoNIGHTS.UI");
            Assert.That(loader, Is.Not.Null, "Texture2D-only portrait loader must exist.");
            var load = loader.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
            Assert.That(load, Is.Not.Null);

            var first = (Sprite)load.Invoke(null, new object[] { path });
            var second = (Sprite)load.Invoke(null, new object[] { path });
            Assert.That(first, Is.Not.Null);
            Assert.That(first.texture, Is.SameAs(texture));
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ExternalSnapshotRefresh_WithUnchangedProjectionPreservesSelection()
        {
            var root = new GameObject("ExternalStagingSelectionTests");
            try
            {
                var hud = root.AddComponent<StagingHudController>();
                if (!hud.InitializationSucceeded)
                {
                    typeof(StagingHudController)
                        .GetMethod(
                            "Awake",
                            BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(hud, null);
                }
                Assert.That(hud.InitializationSucceeded, Is.True);
                var snapshot = hud.Snapshot;
                var slotId = StagingHudController.BuildSlotId(
                    snapshot.StagingSlots[0]);
                hud.SetExternalDisplayedSnapshot(snapshot, false);
                hud.ToggleSelection(slotId);
                Assert.That(hud.SelectedSlotId, Is.EqualTo(slotId));

                hud.SetExternalDisplayedSnapshot(snapshot, false);

                Assert.That(
                    hud.SelectedSlotId,
                    Is.EqualTo(slotId),
                    "Countdown-only LAN HUD refreshes must not clear staging selection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
