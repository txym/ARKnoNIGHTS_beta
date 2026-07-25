# Task 4 report — Android multicast lifecycle and permissions

## Implemented

- Added `ARKnoNIGHTS.Lobby.IMulticastLock` and `AndroidMulticastLockFactory`. Android creates a non-reference-counted Wi-Fi multicast lock named `ARKnoNIGHTS.LanLobby`; non-Android builds create no Java object and return an idempotent no-op lock.
- Added a dedicated Lobby PlayMode test assembly and the required non-Android acquire/release test.
- Added a custom Android main manifest with only `INTERNET`, `ACCESS_WIFI_STATE`, and `CHANGE_WIFI_MULTICAST_STATE`; enabled `ForceInternetPermission` and custom-main-manifest PlayerSettings.

## Approved plan adjustment

The lock implementation is at `Assets/Game/Runtime/Lobby/AndroidMulticastLock.cs`, rather than `Runtime/Initial`. `Runtime/Initial` is part of `Assembly-CSharp`, which a dedicated test assembly cannot reference. The approved Lobby placement keeps the lock free of scene, UI, and Battle dependencies and lets the test assembly reference it directly.

## TDD and validation

- Red: `Temp/TASK-4/red-factory-2/PlayModeResults.xml` records one failing test because `AndroidMulticastLockFactory` was absent.
- Green: `Temp/TASK-4/green-approved/PlayModeResults.xml` records `Passed`, total `1`, failed `0`, skipped `0` for `ArknoNights.Lobby.Tests.AndroidMulticastLockPlayModeTests`.
- Static manifest/PlayerSettings validation confirmed exactly the three required permissions, Unity application/activity placeholders, `ForceInternetPermission: 1`, and `useCustomMainManifest: 1`.
- `git diff --check` is clean for source/configuration files. Unity-generated `.meta` files retain Unity's standard trailing empty YAML values.

## Concerns

- The Editor test verifies the non-Android no-op path only. Android Java calls and real Wi-Fi multicast discovery require device validation on a same-Wi-Fi Android Player.
