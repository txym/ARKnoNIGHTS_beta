using ArknoNights.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ArknoNights.Battle.Tests
{
    public sealed class AndroidPlayerSettingsEditModeTests
    {
        [Test]
        public void AndroidPlayer_AllowsOnlyLandscapeAutorotation()
        {
            Assert.That(
                PlayerSettings.defaultInterfaceOrientation.ToString(),
                Is.EqualTo("AutoRotation"));
            Assert.That(
                PlayerSettings.allowedAutorotateToPortrait,
                Is.False);
            Assert.That(
                PlayerSettings.allowedAutorotateToPortraitUpsideDown,
                Is.False);
            Assert.That(
                PlayerSettings.allowedAutorotateToLandscapeLeft,
                Is.True);
            Assert.That(
                PlayerSettings.allowedAutorotateToLandscapeRight,
                Is.True);
        }

        [Test]
        public void AndroidRuntime_PreventsDeviceSleep()
        {
            var previousTimeout = Screen.sleepTimeout;
            try
            {
                Screen.sleepTimeout = SleepTimeout.SystemSetting;

                AndroidRuntimePolicy.ApplyForPlatform(
                    RuntimePlatform.WindowsPlayer);
                Assert.That(
                    Screen.sleepTimeout,
                    Is.EqualTo(SleepTimeout.SystemSetting));

                AndroidRuntimePolicy.ApplyForPlatform(
                    RuntimePlatform.Android);
                Assert.That(
                    Screen.sleepTimeout,
                    Is.EqualTo(SleepTimeout.NeverSleep));
            }
            finally
            {
                Screen.sleepTimeout = previousTimeout;
            }
        }
    }
}
