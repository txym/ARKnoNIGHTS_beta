using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArknoNights.Battle.Core;
using ArknoNights.Battle.Demo;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Battle.Presentation;
using ArknoNights.Lobby;
using ArknoNights.Match;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ArknoNights.Lobby.Tests
{
    public sealed class LanMatchBattleAdapterPlayModeTests
    {
        [Test]
        public void ThreePlayerSeal_BuildsOfficialAndShadowWithEliteExpansionAndStableHashes()
        {
            var units = UnitCatalogLoader.LoadFromResources(
                "BattleData/unit-catalog-v1");
            Assert.That(units.Success, Is.True, string.Join(";", units.Errors));
            var abilities = AbilityCatalogLoader.LoadFromResources(
                "BattleData/ability-catalog-v1",
                units.Catalog);
            Assert.That(abilities.Success, Is.True, string.Join(";", abilities.Errors));
            var typeId = units.Catalog.Entries.First().Definition.TypeId;
            var snapshot = Snapshot(typeId);
            var sealedHashes = new Dictionary<string, string>
            {
                ["official"] = new string('a', 64),
                ["shadow"] = new string('b', 64)
            };

            var first = CreateBattleSet(
                snapshot,
                units.Catalog,
                abilities.Catalog,
                sealedHashes);
            var second = CreateBattleSet(
                snapshot,
                units.Catalog,
                abilities.Catalog,
                sealedHashes);
            var requests = Property<IReadOnlyList<BattleMatchRequest>>(
                first,
                "Requests");
            var observations = Property<IReadOnlyList<PlayerBattleObservation>>(
                first,
                "Observations");
            var hashes = Property<IReadOnlyDictionary<string, string>>(
                first,
                "InputHashes");
            var repeatedHashes = Property<IReadOnlyDictionary<string, string>>(
                second,
                "InputHashes");

            Assert.That(requests.Select(item => item.MatchId),
                Is.EqualTo(new[] { "official", "shadow" }));
            Assert.That(observations, Has.Count.EqualTo(3));
            Assert.That(
                observations.Single(item => item.PlayerId == "player-a").MatchId,
                Is.EqualTo("official"));
            Assert.That(
                observations.Single(item => item.PlayerId == "player-b").MatchId,
                Is.EqualTo("official"));
            Assert.That(
                observations.Single(item => item.PlayerId == "player-c").MatchId,
                Is.EqualTo("shadow"));
            Assert.That(
                observations.Single(item => item.PlayerId == "player-c").Observer,
                Is.EqualTo(BattleObserverView.Away));
            Assert.That(
                requests.Single(item => item.MatchId == "official")
                    .Input.Players.Single(item => item.PlayerId == "player-a")
                    .Units,
                Has.Count.EqualTo(1));
            Assert.That(
                requests.Single(item => item.MatchId == "official")
                    .Input.Players.Single(item => item.PlayerId == "player-b")
                    .Units,
                Has.Count.EqualTo(3));
            Assert.That(
                requests.Single(item => item.MatchId == "shadow")
                    .Input.Players.Single(item => item.PlayerId == "player-c")
                    .Units,
                Has.Count.EqualTo(5));
            Assert.That(hashes, Is.EqualTo(repeatedHashes));
        }

        [Test]
        public void HostClockEstimator_UsesMedianOffsetAndNeverMovesBackward()
        {
            var type = RuntimeType("HostClockEstimator");
            var estimator = Activator.CreateInstance(type);
            var add = type.GetMethod("AddSample");
            var estimate = type.GetMethod("EstimateHostNow");
            add.Invoke(estimator, new object[] { 1000L, 100L, 20L });
            add.Invoke(estimator, new object[] { 1100L, 200L, 20L });
            add.Invoke(estimator, new object[] { 50000L, 300L, 20L });

            var first = (long)estimate.Invoke(estimator, new object[] { 400L });
            var second = (long)estimate.Invoke(estimator, new object[] { 350L });

            Assert.That(first, Is.EqualTo(1310L));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void FormationPointerProjection_UsesBoardPlaneWithoutPhysicsCollider()
        {
            var method = RuntimeType("LanMatchHudController").GetMethod(
                "TryProjectFormationRay",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var arguments = new object[]
            {
                new Ray(new Vector3(200f, 100f, 200f), Vector3.down),
                null
            };

            Assert.That((bool)method.Invoke(null, arguments), Is.True);
            var target = (MatchFormationPosition)arguments[1];
            Assert.That(target.X, Is.EqualTo(2));
            Assert.That(target.Y, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator HostRuntime_ReusesFormalBattleCanvas_AndRestoresItOnDispose()
        {
            const string hostId =
                "lan-33333333333333333333333333333333";
            var hostProfile = new LobbyProfile(hostId, "Host", 0);
            CreateRuntimeConfiguration(
                out var configuration,
                out var runtimeAssets);
            var hostTask = LanRoomHost.StartAsync(
                hostProfile,
                configuration);
            yield return WaitForTask(hostTask, 5f);
            var host = hostTask.Result;
            GameObject formalHudObject = null;
            GameObject runtimeObject = null;
            try
            {
                Assert.That(
                    host.TryStart(hostId, out var startFailure),
                    Is.True,
                    startFailure.ToString());
                SceneManager.LoadScene(
                    "SampleScene",
                    LoadSceneMode.Single);
                for (var frame = 0; frame < 20; frame++)
                    yield return null;
                var stagingHud = UnityEngine.Object.FindObjectOfType(
                    RuntimeType("ArknoNights.UI.StagingHudController"))
                    as Component;
                Assert.That(stagingHud, Is.Not.Null);
                formalHudObject = stagingHud.gameObject;
                var originalCanvas = FindDescendant(
                    formalHudObject.transform,
                    "FormalBattleHudCanvas");
                Assert.That(originalCanvas.activeSelf, Is.True);
                var sceneCoordinator = formalHudObject.GetComponent(
                    RuntimeType("BattleHudSceneCoordinator"));
                Assert.That(sceneCoordinator, Is.Not.Null);
                var existingShop = sceneCoordinator.GetType()
                    .GetProperty("ShopReady")
                    ?.GetValue(sceneCoordinator);
                Assert.That(existingShop, Is.Not.Null);

                runtimeObject = new GameObject(
                    "HostFormalHudRuntimeTest");
                var runtime = runtimeObject.AddComponent(
                    RuntimeType("LanMatchRuntimeController"));
                InitializeRuntime(
                    runtime,
                    "InitializeHost",
                    host,
                    hostProfile,
                    runtimeAssets);
                yield return null;

                var lanHud = runtimeObject.GetComponentInChildren(
                    RuntimeType("LanMatchHudController"));
                Assert.That(
                    Property<bool>(lanHud, "UsesFormalBattleHud"),
                    Is.True);
                Assert.That(originalCanvas.activeSelf, Is.True);
                Assert.That(
                    FindDescendantOrNull(
                        runtimeObject.transform,
                        "LanMatchHudCanvas"),
                    Is.Null,
                    "LAN integration must not create a replacement canvas.");
                Assert.That(
                    originalCanvas.transform.Find("ShopReadyHud"),
                    Is.Not.Null);
                Assert.That(
                    originalCanvas.transform.Find("PlayerListPanel"),
                    Is.Not.Null);
                Assert.That(
                    originalCanvas.transform
                        .Find(
                            "PlayerListPanel/Player_"
                            + hostId
                            + "/Avatar")
                        .GetComponent<Image>()
                        .sprite
                        .name,
                    Is.EqualTo("icon_amiy"));
                Assert.That(
                    Property<bool>(existingShop, "IsExternalMode"),
                    Is.True);

                var beforePurchase = Snapshot(runtime);
                var offer = beforePurchase.OwnerPrivateState.ShopOffers
                    .First(item => !string.IsNullOrEmpty(item.UnitId));
                Assert.That(offer.TypeId, Is.Not.EqualTo("1000"));
                var runtimeUnits = UnitCatalogLoader.LoadFromResources(
                    "BattleData/unit-catalog-v1");
                Assert.That(runtimeUnits.Success, Is.True);
                Assert.That(
                    runtimeUnits.Catalog.TryGet(
                        offer.TypeId,
                        out var offeredCatalogEntry),
                    Is.True);
                var portrait = ((Component)existingShop).transform
                    .Find(
                        "ShopPanel/ShopSlot_"
                        + offer.SlotIndex
                        + "/PortraitClip/Portrait")
                    .GetComponent<Image>();
                Assert.That(portrait.sprite, Is.Not.Null);
                Assert.That(
                    portrait.sprite.name,
                    Is.EqualTo(
                        Resources.Load<Texture2D>(
                            offeredCatalogEntry.PortraitResourcePath).name));
                InvokePrivate(lanHud, "ConfirmPurchase", offer);
                InvokePrivate(lanHud, "ConfirmPurchase", offer);
                host.Tick();
                yield return null;
                var afterPurchase = Snapshot(runtime);
                var displayedSnapshot = stagingHud.GetType()
                    .GetProperty("DisplayedSnapshot")
                    .GetValue(stagingHud);
                var stagingSlots = (IEnumerable)displayedSnapshot.GetType()
                    .GetProperty("StagingSlots")
                    .GetValue(displayedSnapshot);
                var stagingSlot = stagingSlots.Cast<object>().Single(item =>
                    ((IEnumerable)item.GetType()
                            .GetProperty("UnitIds")
                            .GetValue(item))
                        .Cast<object>()
                        .Any(unitId => string.Equals(
                            unitId as string,
                            offer.UnitId,
                            StringComparison.Ordinal)));
                var buildSlotId = stagingHud.GetType().GetMethod(
                    "BuildSlotId",
                    BindingFlags.Static | BindingFlags.Public);
                Assert.That(buildSlotId, Is.Not.Null);
                InvokePrivate(
                    lanHud,
                    "HandleStagingSelected",
                    (string)buildSlotId.Invoke(null, new[] { stagingSlot }));
                var submitFormation = lanHud.GetType().GetMethod(
                    "TrySubmitSelectedAt",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(submitFormation, Is.Not.Null);
                Assert.That(
                    (bool)submitFormation.Invoke(
                        lanHud,
                        new object[] { afterPurchase.PublicState, 2, 2 }),
                    Is.True);
                host.Tick();
                yield return null;
                var afterDeployment = Snapshot(runtime);
                var deployed = afterDeployment.PublicState.Seats
                    .Single(item => item.PlayerId == hostId)
                    .Units.Single(item => item.UnitId == offer.UnitId);
                Assert.That(deployed.Zone, Is.EqualTo(MatchUnitZone.Deployed.ToString()));
                Assert.That(deployed.FormationX, Is.EqualTo(2));
                Assert.That(deployed.FormationY, Is.EqualTo(2));

                UnityEngine.Object.DestroyImmediate(runtimeObject);
                runtimeObject = null;
                yield return null;
                Assert.That(
                    originalCanvas.activeSelf,
                    Is.True,
                    "Disposal must leave the original formal canvas active.");
                Assert.That(
                    Property<bool>(existingShop, "IsExternalMode"),
                    Is.False,
                    "Disposal must restore the original shop data source.");

                var observer = sceneCoordinator.GetType()
                    .GetProperty("Observer")
                    ?.GetValue(sceneCoordinator);
                Assert.That(observer, Is.Not.Null);
                var playerList = originalCanvas.transform.Find(
                    "PlayerListPanel");
                var localPlayer = Property<object>(
                    observer,
                    "DisplayedPlayerState");
                var localPlayerId = Property<string>(
                    localPlayer,
                    "PlayerId");
                var remoteRow = playerList.Cast<Transform>()
                    .First(item =>
                        item.name.StartsWith(
                            "Player_",
                            StringComparison.Ordinal)
                        && !string.Equals(
                            item.name,
                            "Player_" + localPlayerId,
                            StringComparison.Ordinal));
                remoteRow.GetComponent<Button>().onClick.Invoke();
                Assert.That(
                    Property<bool>(
                        observer,
                        "IsObservingAnotherPlayer"),
                    Is.True,
                    "Disposal must restore the original player-list callback.");
            }
            finally
            {
                if (runtimeObject != null)
                    UnityEngine.Object.DestroyImmediate(runtimeObject);
                if (formalHudObject != null)
                    UnityEngine.Object.DestroyImmediate(formalHudObject);
                host.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator HostAndGuestRuntime_UseRealSocketAndReachSharedPlayback()
        {
            const string hostId = "lan-11111111111111111111111111111111";
            const string guestId = "lan-22222222222222222222222222222222";
            var hostProfile = new LobbyProfile(hostId, "Host", 0);
            var guestProfile = new LobbyProfile(guestId, "Guest", 1);
            CreateRuntimeConfiguration(
                out var configuration,
                out var runtimeAssets);
            var hostTask = LanRoomHost.StartAsync(
                hostProfile,
                configuration);
            yield return WaitForTask(hostTask, 5f);
            var host = hostTask.Result;
            LanRoomClient client = null;
            GameObject hostObject = null;
            GameObject guestObject = null;
            try
            {
                var joinTask = LanRoomClient.JoinAsync(
                    host.LoopbackEndpoint,
                    host.RoomCode,
                    guestProfile,
                    configuration,
                    new MemoryCredentialStore());
                yield return WaitForTask(joinTask, 5f);
                client = joinTask.Result;

                var readyTask = client.SetReadyAsync(true);
                yield return WaitForTask(readyTask, 5f);
                yield return WaitUntil(
                    () =>
                    {
                        host.Tick();
                        return host.Snapshot.Members.Single(item =>
                            item.Profile.PlayerId == guestId).IsReady;
                    },
                    5f,
                    "guest lobby readiness");
                Assert.That(
                    host.TryStart(hostId, out var startFailure),
                    Is.True,
                    startFailure.ToString());
                var initializedTask = client.WaitForMatchInitializedAsync(
                    TimeSpan.FromSeconds(5));
                yield return WaitForTask(initializedTask, 6f);
                client.Tick();
                host.Tick();

                var runtimeType = RuntimeType("LanMatchRuntimeController");
                hostObject = new GameObject("HostLanMatchRuntimeTest");
                guestObject = new GameObject("GuestLanMatchRuntimeTest");
                var hostRuntime = hostObject.AddComponent(runtimeType);
                var guestRuntime = guestObject.AddComponent(runtimeType);
                InitializeRuntime(
                    hostRuntime,
                    "InitializeHost",
                    host,
                    hostProfile,
                    runtimeAssets);
                InitializeRuntime(
                    guestRuntime,
                    "InitializeGuest",
                    client,
                    guestProfile,
                    runtimeAssets);

                SendReady(hostRuntime, "host-ready");
                SendReady(guestRuntime, "guest-ready");
                yield return WaitUntil(
                    () =>
                    {
                        host.Tick();
                        client.Tick();
                        return HasBattle(hostRuntime)
                            && HasBattle(guestRuntime)
                            && IsPlaying(hostRuntime)
                            && IsPlaying(guestRuntime);
                    },
                    15f,
                    "host and guest shared battle playback");

                var hostSnapshot = Snapshot(hostRuntime);
                var guestSnapshot = Snapshot(guestRuntime);
                Assert.That(hostSnapshot.SessionId, Is.EqualTo(guestSnapshot.SessionId));
                Assert.That(hostSnapshot.PublicState.RoundNumber, Is.EqualTo(1));
                Assert.That(guestSnapshot.PublicState.RoundNumber, Is.EqualTo(1));
                Assert.That(hostSnapshot.PublicState.Pairings, Is.Not.Empty);
                Assert.That(
                    hostSnapshot.PublicState.Pairings.Select(item => item.BattleId),
                    Is.EqualTo(guestSnapshot.PublicState.Pairings.Select(item => item.BattleId)));

                var hostHud = hostObject.GetComponentInChildren(
                    RuntimeType("LanMatchHudController"));
                Assert.That(
                    FindDescendantOrNull(
                        hostHud.transform,
                        "LanMatchHudCanvas"),
                    Is.Null,
                    "Headless loopback runtimes must not synthesize a replacement HUD.");
                var offer = hostSnapshot.OwnerPrivateState.ShopOffers
                    .First(item => !string.IsNullOrEmpty(item.UnitId));
                var beforePurchaseRevision =
                    host.SessionActor.ProjectHostState().StateRevision;
                InvokePrivate(hostHud, "ConfirmPurchase", offer);
                host.Tick();
                Assert.That(
                    host.SessionActor.ProjectHostState().StateRevision,
                    Is.EqualTo(beforePurchaseRevision),
                    "The first click must only arm purchase confirmation.");
                InvokePrivate(hostHud, "ConfirmPurchase", offer);
                host.Tick();
                Assert.That(
                    host.SessionActor.ProjectHostState().StateRevision,
                    Is.GreaterThan(beforePurchaseRevision),
                    "The second click must submit through the authority queue.");
            }
            finally
            {
                if (hostObject != null) UnityEngine.Object.DestroyImmediate(hostObject);
                if (guestObject != null) UnityEngine.Object.DestroyImmediate(guestObject);
                if (client != null)
                {
                    client.StopAsync().GetAwaiter().GetResult();
                }
                host.Dispose();
            }
        }

        private static object CreateBattleSet(
            ScopedSnapshotPayload snapshot,
            UnitCatalog units,
            AbilityCatalog abilities,
            IReadOnlyDictionary<string, string> hashes)
        {
            var type = RuntimeType("LanMatchBattleAdapter");
            var method = type.GetMethod(
                "TryCreate",
                BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[]
            {
                snapshot,
                units,
                abilities,
                hashes,
                null,
                null
            };
            Assert.That((bool)method.Invoke(null, arguments), Is.True, arguments[5] as string);
            Assert.That(arguments[4], Is.Not.Null);
            return arguments[4];
        }

        private static T Property<T>(object source, string name)
        {
            return (T)source.GetType().GetProperty(name).GetValue(source);
        }

        private static void CreateRuntimeConfiguration(
            out LanMatchSessionConfiguration configuration,
            out object runtimeAssets)
        {
            var method = RuntimeType("LanMatchRuntimeConfiguration").GetMethod(
                "TryCreateWithRuntimeAssets",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            var arguments = new object[] { null, null, null };
            Assert.That(
                (bool)method.Invoke(null, arguments),
                Is.True,
                arguments[2] as string);
            configuration = (LanMatchSessionConfiguration)arguments[0];
            runtimeAssets = arguments[1];
            Assert.That(runtimeAssets, Is.Not.Null);
        }

        private static void InitializeRuntime(
            Component runtime,
            string methodName,
            object connection,
            LobbyProfile profile,
            object runtimeAssets)
        {
            var method = runtime.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            var arguments = new[]
            {
                connection,
                profile,
                runtimeAssets,
                null
            };
            Assert.That(
                (bool)method.Invoke(runtime, arguments),
                Is.True,
                arguments[3] as string);
        }

        private static void SendReady(Component runtime, string commandId)
        {
            runtime.GetType().GetMethod("SendCommand").Invoke(
                runtime,
                new object[]
                {
                    new MatchCommandWirePayload
                    {
                        CommandId = commandId,
                        CommandKind =
                            MatchCommandKind.SetPreparationReady.ToString(),
                        DesiredReady = true
                    }
                });
        }

        private static bool HasBattle(Component runtime)
        {
            return Property<bool>(runtime, "HasBattle");
        }

        private static bool IsPlaying(Component runtime)
        {
            var state = Property<MultiBattlePresentationState>(
                runtime,
                "BattleState");
            return state == MultiBattlePresentationState.Playing
                || state == MultiBattlePresentationState.Buffering
                || state == MultiBattlePresentationState.Completed;
        }

        private static ScopedSnapshotPayload Snapshot(Component runtime)
        {
            return Property<ScopedSnapshotPayload>(runtime, "Snapshot");
        }

        private static GameObject FindDescendant(
            Transform root,
            string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child.gameObject;
                var nested = FindDescendantOrNull(child, name);
                if (nested != null) return nested;
            }
            Assert.Fail("Missing child: " + name);
            return null;
        }

        private static GameObject FindDescendantOrNull(
            Transform root,
            string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child.gameObject;
                var nested = FindDescendantOrNull(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        private static void InvokePrivate(
            Component target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, arguments);
        }

        private static IEnumerator WaitForTask(Task task, float timeoutSeconds)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(task.IsCompleted, Is.True, "Task timed out.");
            task.GetAwaiter().GetResult();
        }

        private static IEnumerator WaitUntil(
            Func<bool> condition,
            float timeoutSeconds,
            string description)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition()
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, description + " timed out.");
        }

        private static ScopedSnapshotPayload Snapshot(string typeId)
        {
            return new ScopedSnapshotPayload
            {
                SessionId = "session",
                StateRevision = 10,
                LocalConnectionState = MatchLocalConnectionState.Connected.ToString(),
                PublicState = new PublicMatchStateWire
                {
                    SessionId = "session",
                    StateRevision = 10,
                    Phase = MatchPhase.Battle.ToString(),
                    RoundNumber = 1,
                    Pairings = new[]
                    {
                        new PublicMatchPairingWire
                        {
                            BattleId = "official",
                            BattleIndex = 0,
                            Kind = MatchPairingKind.Official.ToString(),
                            HomePlayerId = "player-a",
                            AwayPlayerId = "player-b",
                            ShadowOwnerPlayerId = string.Empty
                        },
                        new PublicMatchPairingWire
                        {
                            BattleId = "shadow",
                            BattleIndex = 1,
                            Kind = MatchPairingKind.Shadow.ToString(),
                            HomePlayerId = "player-a",
                            AwayPlayerId = "player-c",
                            ShadowOwnerPlayerId = "player-a"
                        }
                    },
                    EndReason = MatchEndReason.None.ToString(),
                    FinalStandings = Array.Empty<MatchStandingWire>(),
                    Seats = new[]
                    {
                        Seat("player-a", typeId, 0, 2),
                        Seat("player-b", typeId, 2, 4),
                        Seat("player-c", typeId, 3, 6)
                    }
                }
            };
        }

        private static PublicMatchSeatWire Seat(
            string playerId,
            string typeId,
            int elite,
            int x)
        {
            return new PublicMatchSeatWire
            {
                SeatIndex = playerId[playerId.Length - 1] - 'a' + 1,
                PlayerId = playerId,
                DisplayName = playerId,
                AvatarId = "avatar-0",
                Life = 100,
                ConnectionState = PublicConnectionState.Online.ToString(),
                Units = new[]
                {
                    new MatchUnitWire
                    {
                        UnitId = playerId + "-unit",
                        TypeId = typeId,
                        Zone = MatchUnitZone.Deployed.ToString(),
                        EliteLevel = elite,
                        HasFormation = true,
                        FormationX = x,
                        FormationY = 2,
                        Buffs = Array.Empty<MatchBuffWire>()
                    }
                },
                TargetedUnitBuffs = Array.Empty<MatchTargetedBuffWire>(),
                GlobalBuffs = Array.Empty<MatchGlobalBuffWire>(),
                SourceEffects = Array.Empty<MatchSourceEffectWire>()
            };
        }

        private static Type RuntimeType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(item => item.GetType(name))
                .FirstOrDefault(item => item != null);
            Assert.That(type, Is.Not.Null, name);
            return type;
        }

        private sealed class MemoryCredentialStore :
            IReconnectCredentialStore
        {
            private ReconnectCredential credential;

            public bool TryLoad(out ReconnectCredential value)
            {
                value = credential;
                return value != null;
            }

            public void Save(ReconnectCredential value)
            {
                credential = value;
            }

            public void Clear()
            {
                credential = null;
            }
        }
    }
}
