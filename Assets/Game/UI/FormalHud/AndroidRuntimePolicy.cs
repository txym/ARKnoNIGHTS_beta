using UnityEngine;

namespace ArknoNights.UI
{
    public static class AndroidRuntimePolicy
    {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyOnStartup()
        {
            ApplyForPlatform(Application.platform);
        }

        public static void ApplyForPlatform(RuntimePlatform platform)
        {
            if (platform == RuntimePlatform.Android)
            {
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }
    }
}
