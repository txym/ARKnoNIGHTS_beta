using UnityEngine;

namespace ArknoNights.Lobby
{
    public interface IMulticastLock
    {
        bool IsHeld { get; }

        void Acquire();

        void Release();
    }

    public static class AndroidMulticastLockFactory
    {
        private const string LockTag = "ARKnoNIGHTS.LanLobby";

        public static IMulticastLock Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var context = new AndroidJavaClass("android.content.Context"))
            using (var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", context.GetStatic<string>("WIFI_SERVICE")))
            {
                var multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", LockTag);
                multicastLock.Call("setReferenceCounted", false);
                return new AndroidMulticastLock(multicastLock);
            }
#else
            return new NoOpMulticastLock();
#endif
        }

        private sealed class NoOpMulticastLock : IMulticastLock
        {
            public bool IsHeld => false;

            public void Acquire() { }

            public void Release() { }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidMulticastLock : IMulticastLock
        {
            private readonly AndroidJavaObject multicastLock;
            private bool isHeld;

            public AndroidMulticastLock(AndroidJavaObject multicastLock)
            {
                this.multicastLock = multicastLock;
            }

            public bool IsHeld => isHeld;

            public void Acquire()
            {
                if (isHeld) return;
                multicastLock.Call("acquire");
                isHeld = true;
            }

            public void Release()
            {
                if (!isHeld) return;
                multicastLock.Call("release");
                isHeld = false;
            }
        }
#endif
    }
}
