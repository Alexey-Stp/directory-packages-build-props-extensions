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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly ILogger<DotNetProcessRunner> _logger;

    public DotNetProcessRunner(ILogger<DotNetProcessRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessResult> RunAsync(
        string command,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        _logger.LogDebug(
            "Running process: {Command} {Arguments} in {WorkingDirectory}",
            command, arguments, workingDirectory);

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

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
                StandardError = stderr
            };

            if (result.Success)
            {
                _logger.LogDebug(
                    "Process completed successfully: {Command} {Arguments}",
                    command, arguments);
            }
            else
            {
                _logger.LogWarning(
                    "Process exited with code {ExitCode}: {Command} {Arguments}\nStdErr: {StdErr}",
                    process.ExitCode, command, arguments, stderr);
            }

            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The timeout fired, not the caller's token.
            _logger.LogError(
                "Process timed out after {Timeout}s: {Command} {Arguments}",
                DefaultTimeout.TotalSeconds, command, arguments);

            TryKillProcess(process);

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = $"Process timed out after {DefaultTimeout.TotalSeconds} seconds."
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Process cancelled by caller: {Command} {Arguments}",
                command, arguments);

            TryKillProcess(process);

            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(
                ex,
                "Failed to run process: {Command} {Arguments}",
                command, arguments);

            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = ex.Message
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
            _logger.LogWarning(ex, "Failed to kill timed-out process");
        }
    }
}
