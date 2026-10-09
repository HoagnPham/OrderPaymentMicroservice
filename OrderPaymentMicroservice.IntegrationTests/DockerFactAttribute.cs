using System.Diagnostics;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

/// <summary>Skips PostgreSQL tests only when neither Docker nor local binaries are available.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(IsDockerAvailable);

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value && !LocalPostgresServer.IsAvailable)
            Skip = "Neither Docker nor a local PostgreSQL server binary is available.";
    }

    internal static bool IsDockerAvailableForTests => DockerAvailable.Value;

    private static bool IsDockerAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info --format {{.ServerVersion}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            return process != null && process.WaitForExit(3_000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
