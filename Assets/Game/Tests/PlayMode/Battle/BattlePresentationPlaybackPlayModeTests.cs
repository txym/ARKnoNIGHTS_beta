using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ArknoNights.Battle.Tests
{
    public sealed class BattlePresentationPlaybackPlayModeTests
    {
        [UnityTest]
        public IEnumerator CompletedResult_ReplaysAcrossFramesWithoutChangingCoreOutcome()
        {
            var loaded = BattleFixtureLoader.LoadFromResources("BattleFixtures/task003-minimal-v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var originalSummary = result.StableSummary;
            var factory = new FakeFactory();

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(0.025f);
                yield return null;
                playback.Advance(10f);

                Assert.IsTrue(playback.IsCompleted);
                Assert.AreEqual(result.Events.Count, playback.ConsumedEventCount);
                Assert.AreEqual(originalSummary, result.StableSummary);
                Assert.IsEmpty(playback.Diagnostics);
                Assert.That(playback.ViewStates.All(item => result.FinalUnits.Any(finalUnit => finalUnit.UnitId == item.UnitId && finalUnit.Position.Equals(item.Position) && finalUnit.HitPoints == item.HitPoints && finalUnit.IsAlive == item.IsAlive)), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator RealCatalog_DefaultUnitViewsInitializeMoveAttackAndDispose()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK004A_RealFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(1000f);
                Assert.IsTrue(playback.IsCompleted);
                Assert.AreEqual(result.Events.Count, playback.ConsumedEventCount);
                Assert.IsEmpty(playback.Diagnostics);
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_DynamicArcslmiReplayDisposesAndRecreatesExactIds()
        {
            var authoritative = RunSingleArcslmaSummonBattle();
            var result = WithoutDynamicEventSnapshots(authoritative);
            var dynamicSpawns = result.Events
                .Where(item => item.Type == BattleEventType.Spawn && item.UnitTypeId == "5504")
                .ToArray();
            var dynamicIds = dynamicSpawns.Select(item => item.UnitId).ToArray();
            Assert.That(dynamicIds, Is.EqualTo(new[] { "-1", "-2", "-3" }));
            var dynamicSpawnTick = dynamicSpawns
                .Select(item => item.Tick)
                .Distinct()
                .Single();

            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            var unitSkelType2 = Type.GetType("UnitSkelType2, Assembly-CSharp");
            var presentationViewType = Type.GetType("UnitSkelPresentationView, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            Assert.IsNotNull(unitSkelType2);
            Assert.IsNotNull(presentationViewType);
            var catalog = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1");
            Assert.That(catalog.Success, Is.True, string.Join(";", catalog.Errors.Select(item => item.ToString())));
            Assert.That(catalog.Catalog.TryGet("5504", out var arcslmi), Is.True);
            Assert.That(arcslmi.SkeletonDataResourcePath, Is.EqualTo("Characters/5504_arcslmi/enemy_5504_arcslmi_SkeletonData"));
            var expectedSkeletonData = Resources.Load(arcslmi.SkeletonDataResourcePath);
            Assert.IsNotNull(expectedSkeletonData);
            var factoryObject = new GameObject("TASK4_DynamicArcslmiFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);
            var firstInstanceIds = new Dictionary<string, int>(StringComparer.Ordinal);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.That(playback.Load(result, factory, out var diagnostics), Is.True, string.Join(";", diagnostics));
                playback.Play();
                playback.Advance((dynamicSpawnTick + 0.01f) / BattleInput.TicksPerSecond);
                yield return null;

                foreach (var unitId in dynamicIds)
                {
                    var viewObject = FindChild(factoryObject.transform, "BattleView_" + unitId);
                    Assert.IsNotNull(viewObject, unitId + " must be created at its authoritative Spawn Tick.");
                    Assert.IsNotNull(viewObject.GetComponent(unitSkelType2), unitId + " must use UnitSkelType2.");
                    var skeleton = viewObject.GetComponent("SkeletonAnimation");
                    Assert.IsNotNull(skeleton);
                    var skeletonDataField = skeleton.GetType().GetField("skeletonDataAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    Assert.IsNotNull(skeletonDataField);
                    Assert.AreSame(expectedSkeletonData, skeletonDataField.GetValue(skeleton), unitId);
                    var presentationView = viewObject.GetComponent(presentationViewType);
                    Assert.AreEqual("Move", GetPrivateString(presentationView, "moveAnimation"));
                    Assert.AreEqual("Attack", GetPrivateString(presentationView, "attackAnimation"));
                    Assert.AreEqual("Die", GetPrivateString(presentationView, "deathAnimation"));
                    firstInstanceIds.Add(unitId, viewObject.GetInstanceID());
                }

                Assert.That(playback.Replay(out var replayDiagnostics), Is.True, string.Join(";", replayDiagnostics));
                yield return null;
                Assert.That(dynamicIds.All(unitId => FindChild(factoryObject.transform, "BattleView_" + unitId) == null), Is.True,
                    "Replay must dispose dynamic views before their Spawn Tick.");

                playback.Play();
                playback.Advance((dynamicSpawnTick + 0.01f) / BattleInput.TicksPerSecond);
                yield return null;
                foreach (var unitId in dynamicIds)
                {
                    var recreated = FindChild(factoryObject.transform, "BattleView_" + unitId);
                    Assert.IsNotNull(recreated, unitId + " must be recreated with the same dynamic identity.");
                    Assert.AreNotEqual(firstInstanceIds[unitId], recreated.GetInstanceID(), unitId + " leaked its pre-Replay GameObject.");
                }
            }

            yield return null;
            Assert.That(dynamicIds.All(unitId => FindChild(factoryObject.transform, "BattleView_" + unitId) == null), Is.True,
                "Playback disposal must remove all dynamic views.");
            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_FactoryUsesTheConfiguredCatalogMapping()
        {
            const string arcslmiPath = "Characters/5504_arcslmi/enemy_5504_arcslmi_SkeletonData";
            const string alternatePath = "Characters/5503_arcslma/enemy_5503_arcslma_SkeletonData";
            var sourceCatalog = Resources.Load<TextAsset>("BattleData/unit-catalog-v1");
            Assert.IsNotNull(sourceCatalog);
            Assert.That(sourceCatalog.text, Does.Contain(arcslmiPath));
            var alternateCatalog = new TextAsset(sourceCatalog.text.Replace(arcslmiPath, alternatePath));
            var expectedSkeletonData = Resources.Load(alternatePath);
            Assert.IsNotNull(expectedSkeletonData);

            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType);
            var catalogProperty = factoryType.GetProperty("UnitCatalogAsset", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(catalogProperty, "The factory needs a catalog injection seam so catalog provenance is behaviorally testable.");
            var factoryObject = new GameObject("TASK4_CatalogDrivenFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            catalogProperty.SetValue(factory, alternateCatalog);

            Assert.That(factory.TryCreate("catalog-probe", "5504", out var view, out var diagnostic), Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
            var viewObject = FindChild(factoryObject.transform, "BattleView_catalog-probe");
            Assert.IsNotNull(viewObject);
            var skeleton = viewObject.GetComponent("SkeletonAnimation");
            Assert.IsNotNull(skeleton);
            var skeletonDataField = skeleton.GetType().GetField("skeletonDataAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(skeletonDataField);
            Assert.AreSame(expectedSkeletonData, skeletonDataField.GetValue(skeleton),
                "The created 5504 view must follow the configured UnitCatalog mapping rather than a type-specific hardcoded path.");

            view.Dispose();
            UnityEngine.Object.Destroy(alternateCatalog);
            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_ChangingPlaybackSpeedDoesNotApplyTheSpeedTwiceToArcslmaAttack()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var arcslmaUnitIds = loaded.Input.Players.SelectMany(player => player.Units)
                .Where(unit => unit.TypeId == "5503")
                .Select(unit => unit.UnitId)
                .ToArray();
            var attack = result.Events.First(item => item.Type == BattleEventType.Attack && arcslmaUnitIds.Contains(item.UnitId));
            Assert.AreEqual(54, attack.OriginalAnimationTicks);
            Assert.AreEqual(40, attack.EffectiveAnimationTicks);
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK003_AttackTimingFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.SetPlaybackSpeed(0.5f);
                playback.Play();
                playback.Advance((attack.Tick + 0.1f) / 10f);

                var attackView = GameObject.Find("BattleView_" + attack.UnitId);
                Assert.IsNotNull(attackView);
                var unitSkelType = Type.GetType("UnitSkelType2, Assembly-CSharp");
                var unitSkelBaseType = Type.GetType("UnitSkelBase, Assembly-CSharp");
                var unitSkel = attackView.GetComponent(unitSkelType);
                Assert.IsNotNull(unitSkel);
                var state = unitSkelBaseType.GetField("state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(unitSkel);
                var entry = state.GetType().GetMethod("GetCurrent").Invoke(state, new object[] { 0 });
                Assert.IsNotNull(entry);
                var entryTimeScale = (float)entry.GetType().GetProperty("TimeScale").GetValue(entry, null);
                Assert.AreEqual(1.35f, entryTimeScale, 0.0001f, "TrackEntry must contain only the 54/40 attack compression; global playback speed is applied by SkeletonAnimation.timeScale and must not be multiplied into the attack entry again.");
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealCatalog_PauseFreezesSpineAnimationUntilResume()
        {
            var loaded = LocalBattleLoader.LoadFromResources("BattleData/unit-catalog-v1", "BattleData/task004a-real-1v1");
            Assert.IsTrue(loaded.Success, string.Join(";", loaded.Errors.Select(item => item.ToString())));
            var result = new BattleRunner(loaded.Input).RunToCompletion();
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType, "Assembly-CSharp real presentation bridge is unavailable.");
            var factoryObject = new GameObject("TASK003_PauseFreezeFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            using (var playback = new BattleEventPlaybackController())
            {
                Assert.IsTrue(playback.Load(result, factory, out var diagnostics), string.Join(";", diagnostics));
                playback.Play();
                playback.Advance(0.05f);

                var homeUnitId = loaded.Input.Players.Single(player => player.Side == BattleSide.Home).Units.First().UnitId;
                var awayUnitId = loaded.Input.Players.Single(player => player.Side == BattleSide.Away).Units.First().UnitId;
                var homeView = factoryObject.transform.Find("BattleView_" + homeUnitId);
                var awayView = factoryObject.transform.Find("BattleView_" + awayUnitId);
                Assert.IsNotNull(homeView);
                Assert.IsNotNull(awayView);
                var homeSkeleton = homeView.GetComponent("SkeletonAnimation");
                var awaySkeleton = awayView.GetComponent("SkeletonAnimation");
                Assert.IsNotNull(homeSkeleton);
                Assert.IsNotNull(awaySkeleton);

                playback.Pause();
                Assert.AreEqual(0f, GetSpineTimeScale(homeSkeleton));
                Assert.AreEqual(0f, GetSpineTimeScale(awaySkeleton));
                playback.SetPlaybackSpeed(2f);
                Assert.AreEqual(0f, GetSpineTimeScale(homeSkeleton));
                playback.Resume();
                Assert.AreEqual(2f, GetSpineTimeScale(homeSkeleton));
                Assert.AreEqual(2f, GetSpineTimeScale(awaySkeleton));
            }

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealUnitView_DeathCompletesThenBlackensForHalfASecondAndHides()
        {
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType);
            var factoryObject = new GameObject("DeathPresentationFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            Assert.That(factory.TryCreate("death-probe", "5504", out var view, out var diagnostic),
                Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
            var viewObject = FindChild(factoryObject.transform, "BattleView_death-probe");
            Assert.IsNotNull(viewObject);
            var presentationView = view as Component;
            Assert.IsNotNull(presentationView);
            var skeleton = viewObject.GetComponent("SkeletonAnimation");
            Assert.IsNotNull(skeleton);
            var statusBarRoot = viewObject.transform.Find("WorldStatusBar");
            Assert.IsNotNull(statusBarRoot);
            view.SetStatusBarState("death-probe", false, 0, 0);
            Assert.That(statusBarRoot.gameObject.activeInHierarchy, Is.True);
            var initialColor = GetSkeletonColor(skeleton);

            view.SetPlaybackSpeed(10f);
            view.PlayDeath();
            view.PlayDeath();
            Assert.That(view.HasPendingTerminalPresentation, Is.True);

            var animationDeadline = Time.realtimeSinceStartup + 3f;
            while (GetPrivateFieldValue(presentationView, "deathState").ToString() == "Animation" &&
                   Time.realtimeSinceStartup < animationDeadline)
            {
                var animationColor = GetSkeletonColor(skeleton);
                Assert.That(animationColor.r, Is.EqualTo(initialColor.r).Within(0.0001f));
                Assert.That(animationColor.g, Is.EqualTo(initialColor.g).Within(0.0001f));
                Assert.That(animationColor.b, Is.EqualTo(initialColor.b).Within(0.0001f),
                    "RGB must remain unchanged until the real Spine death TrackEntry completes.");
                yield return null;
            }

            Assert.That(GetPrivateFieldValue(presentationView, "deathState").ToString(), Is.EqualTo("Blackening"));
            Assert.That(viewObject.activeSelf, Is.True, "The view must remain visible when blackening begins.");
            Assert.That(statusBarRoot.gameObject.activeInHierarchy, Is.True,
                "The status bar remains visible while the owning unit remains visible.");

            var observedFadeSeconds = 0f;
            while (viewObject.activeSelf && observedFadeSeconds < 0.2f)
            {
                yield return null;
                observedFadeSeconds += Time.unscaledDeltaTime;
            }

            Assert.That(viewObject.activeSelf, Is.True);
            var middleFadeColor = GetSkeletonColor(skeleton);
            Assert.That(middleFadeColor.r, Is.LessThan(initialColor.r));
            Assert.That(middleFadeColor.g, Is.LessThan(initialColor.g));
            Assert.That(middleFadeColor.b, Is.LessThan(initialColor.b));
            Assert.That(middleFadeColor.r, Is.GreaterThan(0f));
            Assert.That(middleFadeColor.g, Is.GreaterThan(0f));
            Assert.That(middleFadeColor.b, Is.GreaterThan(0f));
            Assert.That(middleFadeColor.a, Is.EqualTo(initialColor.a).Within(0.0001f));

            while (viewObject.activeSelf && observedFadeSeconds < 1f)
            {
                yield return null;
                observedFadeSeconds += Time.unscaledDeltaTime;
            }

            Assert.That(viewObject.activeSelf, Is.False);
            Assert.That(statusBarRoot.gameObject.activeInHierarchy, Is.False);
            Assert.That(view.HasPendingTerminalPresentation, Is.False);
            Assert.That(observedFadeSeconds, Is.InRange(0.45f, 0.65f),
                "The view must hide about 0.5 unscaled seconds after Spine reports death animation completion.");

            view.Dispose();
            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealUnitView_ExternalDisableDuringDeathReleasesTerminalPresentation()
        {
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType);
            var factoryObject = new GameObject("DeathDisableFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            Assert.That(factory.TryCreate("disable-probe", "5504", out var view, out var diagnostic),
                Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
            var viewObject = FindChild(factoryObject.transform, "BattleView_disable-probe");
            Assert.IsNotNull(viewObject);

            view.PlayDeath();
            Assert.That(view.HasPendingTerminalPresentation, Is.True);
            viewObject.SetActive(false);

            Assert.That(view.HasPendingTerminalPresentation, Is.False,
                "An externally disabled view cannot continue updating and must not keep formal completion pending.");

            view.Dispose();
            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealUnitView_DisposeDuringDeathImmediatelyHidesBeforeFrameEnd()
        {
            var factoryType = Type.GetType("MappedBattlePresentationViewFactory, Assembly-CSharp");
            Assert.IsNotNull(factoryType);
            var factoryObject = new GameObject("DeathDisposeFactory");
            var factory = factoryObject.AddComponent(factoryType) as IBattlePresentationViewFactory;
            Assert.IsNotNull(factory);

            Assert.That(factory.TryCreate("dispose-probe", "5504", out var view, out var diagnostic),
                Is.True, diagnostic == null ? string.Empty : diagnostic.ToString());
            var viewObject = FindChild(factoryObject.transform, "BattleView_dispose-probe");
            Assert.IsNotNull(viewObject);

            view.PlayDeath();
            view.Dispose();

            Assert.That(viewObject.activeSelf, Is.False,
                "Dispose must hide the old death view before Unity destroys it at frame end.");
            yield return null;
            Assert.That(FindChild(factoryObject.transform, "BattleView_dispose-probe"), Is.Null);

            UnityEngine.Object.Destroy(factoryObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealUnitView_FacesRightByDefaultAndOnlyFlipsForHorizontalMovement()
        {
            var viewType = Type.GetType("UnitSkelPresentationView, Assembly-CSharp");
            Assert.IsNotNull(viewType, "Assembly-CSharp facing bridge is unavailable.");
            var gameObject = new GameObject("TASK003_FacingView");
            var view = gameObject.AddComponent(viewType) as IBattlePresentationView;
            Assert.IsNotNull(view);

            var right = Quaternion.Euler(60f, 0f, 0f);
            var left = right * Quaternion.Euler(0f, 180f, 0f);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f));
            view.SetFacing(Vector3.forward);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f), "Pure Z movement must keep the default right-facing direction.");
            view.SetFacing(Vector3.left);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, left), Is.LessThan(0.5f));
            view.SetFacing(Vector3.forward);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, left), Is.LessThan(0.5f), "Pure Z movement must preserve the current left-facing direction.");
            view.SetFacing(Vector3.right);
            Assert.That(Quaternion.Angle(gameObject.transform.rotation, right), Is.LessThan(0.5f));

            UnityEngine.Object.Destroy(gameObject);
            yield return null;
        }

        private static float GetSpineTimeScale(Component skeleton)
        {
            var field = skeleton.GetType().GetField("timeScale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "SkeletonAnimation.timeScale is unavailable.");
            return (float)field.GetValue(skeleton);
        }

        private static Color GetSkeletonColor(Component skeletonAnimation)
        {
            var skeleton = skeletonAnimation.GetType().GetProperty("Skeleton").GetValue(skeletonAnimation, null);
            Assert.IsNotNull(skeleton);
            var type = skeleton.GetType();
            return new Color(
                (float)type.GetProperty("R").GetValue(skeleton, null),
                (float)type.GetProperty("G").GetValue(skeleton, null),
                (float)type.GetProperty("B").GetValue(skeleton, null),
                (float)type.GetProperty("A").GetValue(skeleton, null));
        }

        private static BattleRunResult RunSingleArcslmaSummonBattle()
        {
            var catalog = UnitCatalogLoader.LoadFromResources("BattleData/unit-catalog-v1");
            Assert.That(catalog.Success, Is.True, string.Join(";", catalog.Errors.Select(item => item.ToString())));
            var abilities = AbilityCatalogLoader.LoadFromResources("BattleData/ability-catalog-v1", catalog.Catalog);
            Assert.That(abilities.Success, Is.True, string.Join(";", abilities.Errors.Select(item => item.ToString())));
            var definitions = catalog.Catalog.Entries
                .Select(item =>
                    item.Definition.TypeId == "5503"
                        ? WithoutNormalAttack(item.Definition)
                        : item.Definition.TypeId == "1000"
                            ? WithMaxHitPoints(item.Definition, 100000)
                            : item.Definition)
                .ToArray();
            var specification = new BattleInputSpecification(
                BattleInput.SupportedSchemaVersion,
                "presentation-playmode-single-caster",
                200,
                definitions,
                abilities.Catalog.Abilities,
                new[]
                {
                    new PlayerSnapshot("home", BattleSide.Home, new[]
                    {
                        new UnitSnapshot("caster", "5503", UnitZone.Deployed, new FormationCoordinate(5, 2), Array.Empty<BuffPlaceholder>())
                    }),
                    new PlayerSnapshot("away", BattleSide.Away, new[]
                    {
                        new UnitSnapshot("enemy", "1000", UnitZone.Deployed, new FormationCoordinate(5, 2), Array.Empty<BuffPlaceholder>())
                    })
                });
            Assert.That(BattleInputFactory.TryCreate(specification, out var input, out var errors), Is.True, string.Join(";", errors.Select(item => item.ToString())));
            return new BattleRunner(input).RunToCompletion();
        }

        private static UnitDefinition WithoutNormalAttack(UnitDefinition source)
        {
            return new UnitDefinition(
                source.TypeId,
                source.MaxHitPoints,
                0,
                source.Defense,
                source.MagicResistance,
                source.MoveSpeedCentimetresPerSecond,
                0,
                0,
                DamageType.None,
                AttackMethod.None,
                source.BlockCapacity,
                source.TauntLevel,
                source.IsSyntheticFixtureData,
                source.InnateAbilityIds,
                source.ActionMethod,
                source.LifeDeduct);
        }

        private static UnitDefinition WithMaxHitPoints(UnitDefinition source, int maxHitPoints)
        {
            return new UnitDefinition(
                source.TypeId,
                maxHitPoints,
                source.Attack,
                source.Defense,
                source.MagicResistance,
                source.MoveSpeedCentimetresPerSecond,
                source.AttackIntervalTicks,
                source.AttackAnimationDurationTicks,
                source.DamageType,
                source.AttackMethod,
                source.BlockCapacity,
                source.TauntLevel,
                source.IsSyntheticFixtureData,
                source.InnateAbilityIds);
        }

        private static BattleRunResult WithoutDynamicEventSnapshots(BattleRunResult source)
        {
            var events = source.Events.Select(item =>
                item.Type == BattleEventType.Spawn && item.UnitId.StartsWith("-", StringComparison.Ordinal)
                    ? CloneEvent(item, null)
                    : item);
            return new BattleRunResult(
                source.BattleId,
                source.HomePlayerId,
                source.AwayPlayerId,
                source.InputCanonicalSummary,
                source.KnownUnitTypeIds,
                source.CompletedTicks,
                source.StopReason,
                source.Winner,
                source.Trace,
                new ReadOnlyCollection<BattleEvent>(events.ToArray()),
                source.FinalUnits,
                new ReadOnlyDictionary<string, BattleUnitInstanceSnapshot>(
                    source.UnitSnapshots.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)),
                source.StableSummary,
                source.HomeLifeDamage,
                source.AwayLifeDamage,
                source.FinalCheckpoint);
        }

        private static BattleEvent CloneEvent(BattleEvent source, BattleUnitInstanceSnapshot spawnSnapshot)
        {
            return new BattleEvent(
                source.Type,
                source.Tick,
                source.Sequence,
                source.UnitId,
                source.UnitTypeId,
                source.UnitSide,
                source.RelatedUnitId,
                source.FromPosition,
                source.ToPosition,
                source.DamageType,
                source.DamageAmount,
                source.HitPointsBefore,
                source.HitPointsAfter,
                source.PlannedDamageTick,
                source.OriginalAnimationTicks,
                source.EffectiveAnimationTicks,
                source.Winner,
                source.Reason,
                spawnSnapshot,
                source.AnimationKey);
        }

        private static GameObject FindChild(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Where(item => item != root && item.name == name)
                .Select(item => item.gameObject)
                .SingleOrDefault();
        }

        private static string GetPrivateString(Component component, string fieldName)
        {
            var field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            return (string)field.GetValue(component);
        }

        private static object GetPrivateFieldValue(Component component, string fieldName)
        {
            var field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(component);
        }

        private sealed class FakeFactory : IBattlePresentationViewFactory
        {
            public bool TryCreate(string unitId, string typeId, out IBattlePresentationView view, out BattlePresentationDiagnostic diagnostic)
            {
                view = new FakeView();
                diagnostic = null;
                return true;
            }
        }

        private sealed class FakeView : IBattlePresentationView
        {
            public void SetWorldPosition(Vector3 position) { }
            public void SetFacing(Vector3 direction) { }
            public void SetPlaybackSpeed(float playbackSpeed) { }
            public void PlayMove() { }
            public void PlayAttack(float animationSpeedMultiplier) { }
            public void PlayHit() { }
            public void PlayDeath() { }
            public void SetStatusBarState(string unitId, bool isEnemy, int currentHitPoints, int currentShield) { }
            public void Dispose() { }
        }
    }
}
