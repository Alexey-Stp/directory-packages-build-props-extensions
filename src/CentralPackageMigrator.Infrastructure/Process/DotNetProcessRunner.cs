using System.Diagnostics;
using CentralPackageMigrator.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Infrastructure.Process;

/// <summary>
/// Production implementation of <see cref="IProcessRunner"/> that launches
/// external processes using <see cref="System.Diagnostics.Process"/>.
/// </summary>
public sealed class DotNetProcessRunner : IProcessRunner
{
    private readonly ILogger<DotNetProcessRunner> _logger;

    public DotNetProcessRunner(ILogger<DotNetProcessRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string display = string.Join(' ', arguments);
        _logger.LogDebug(
            "Running process: {Command} {Arguments} in {WorkingDirectory}",
            command, display, workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();

            // Read stdout and stderr concurrently to avoid deadlocks
            // when the process writes more than the OS buffer size to either stream.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);

            var result = new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Duration = stopwatch.Elapsed
            };

            if (result.Success)
            {
                _logger.LogDebug("Process completed successfully: {Command} {Arguments}", command, display);
            }
            else
            {
                _logger.LogDebug(
                    "Process exited with code {ExitCode}: {Command} {Arguments}",
                    process.ExitCode, command, display);
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The timeout fired, not the caller's token.
            _logger.LogError(
                "Process timed out after {Timeout}s: {Command} {Arguments}",
                timeout.TotalSeconds, command, display);

            TryKillProcess(process);

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = $"Process timed out after {timeout.TotalSeconds} seconds.",
                TimedOut = true,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Process cancelled by caller: {Command} {Arguments}", command, display);
            TryKillProcess(process);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Failed to run process: {Command} {Arguments}", command, display);

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private void TryKillProcess(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "Failed to kill process");
        }
    }
}
