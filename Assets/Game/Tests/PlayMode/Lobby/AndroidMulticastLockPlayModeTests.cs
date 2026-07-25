using System.Collections;
using ArknoNights.Lobby;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ArknoNights.Lobby.Tests
{
    public sealed class AndroidMulticastLockPlayModeTests
    {
        [UnityTest]
        public IEnumerator NonAndroidMulticastLock_IsNoOpAndReleasesIdempotently()
        {
            var lockHandle = AndroidMulticastLockFactory.Create();

            lockHandle.Acquire();
            lockHandle.Release();
            lockHandle.Release();

            Assert.That(lockHandle.IsHeld, Is.False);
            yield return null;
        }
    }
}
