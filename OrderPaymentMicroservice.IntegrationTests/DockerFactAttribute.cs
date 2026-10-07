using System.Diagnostics;
using Xunit;

namespace OrderPaymentMicroservice.IntegrationTests;

/// <summary>Skips PostgreSQL container tests when the Docker daemon is unavailable.</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(IsDockerAvailable);

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value)
            Skip = "Docker daemon is unavailable; start Docker Desktop to run PostgreSQL integration tests.";
    }

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
