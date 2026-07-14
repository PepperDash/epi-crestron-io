using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace EpiCrestronIo.Tests
{
    /// <summary>
    /// The registration-retry recovery is bound to Crestron SDK types (CTimer, GlsOccupancySensorBase,
    /// the base CustomActivate) so it cannot be exercised off-processor. These source-scan tests act as
    /// regression guards: they fail if the recovery wiring is removed or weakened, which would silently
    /// reintroduce the "occupancy sensor offline at startup never recovers without a restart" bug.
    /// </summary>
    public class GlsOccupancySensorRetryRegressionTests
    {
        private static string ControllerSource =>
            File.ReadAllText(LocateRepoFile("src/GlsOccupancySensorBaseController.cs"));

        [Fact]
        public void Controller_OverridesCustomActivate()
        {
            ControllerSource.Should().Contain("public override bool CustomActivate()",
                "the controller must hook activation to detect a failed initial registration");
        }

        [Fact]
        public void Controller_StartsRetryWhenSensorNotRegistered()
        {
            var src = ControllerSource;
            src.Should().Contain("!OccSensor.Registered");
            src.Should().Contain("StartRegistrationRetryTimer()",
                "a sensor that fails to register at startup must be retried in the background");
        }

        [Fact]
        public void Controller_RetryReRegistersUntilSuccess()
        {
            var src = ControllerSource;
            src.Should().Contain("AttemptRegistration");
            src.Should().Contain("RegisterWithLogging(Key)",
                "the retry must actually re-attempt the cresnet registration");
        }

        [Fact]
        public void Controller_FinalizesActivationOnRecovery()
        {
            var src = ControllerSource;
            src.Should().Contain("CommunicationMonitor.Start()",
                "base CustomActivate bails before starting the monitor on a failed reg; recovery must start it");
            src.Should().Contain("ApplySettingsToSensorFromConfig()",
                "config must be applied once the sensor finally registers");
        }

        [Fact]
        public void Controller_DisposesRetryTimerOnProgramStop()
        {
            var src = ControllerSource;
            src.Should().Contain("StopRegistrationRetryTimer()");
            src.Should().Contain("HandleProgramStatusEvent",
                "the retry timer must be stopped on program stop so it can't touch the sensor during teardown");
        }

        [Fact]
        public void Controller_KeepsUnregisterOnStop()
        {
            ControllerSource.Should().Contain(".UnRegister()",
                "releasing the cresnet ID on stop must be preserved so the next start can re-register");
        }

        private static string LocateRepoFile(string relativePath)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"Could not locate '{relativePath}' from {AppContext.BaseDirectory}");
        }
    }
}
