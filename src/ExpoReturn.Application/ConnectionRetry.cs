namespace ExpoReturn.Application;

/// <summary>
/// Start-up connection to the database (mandatory: the application never runs without it).
/// One attempt when the application opens; if it fails, three retries: immediately, after 5 s and after 10 s.
/// </summary>
public static class ConnectionRetry
{
    /// <summary>Wait before each retry (retry 1, 2, 3).</summary>
    public static IReadOnlyList<TimeSpan> RetryDelays { get; } = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };

    /// <param name="Retry">1-based retry about to start.</param>
    public sealed record RetryInfo(int Retry, int Retries, TimeSpan Wait, string LastError);

    public sealed record Outcome<T>(T? Result, string? LastError, int Attempts) where T : class
    {
        public bool Connected => Result is not null;
    }

    /// <summary>Runs the first attempt only. The caller shows the retry window if it failed.</summary>
    public static async Task<Outcome<T>> FirstAttemptAsync<T>(Func<T> connect) where T : class
    {
        try { return new Outcome<T>(await Task.Run(connect), null, 1); }
        catch (Exception ex) { return new Outcome<T>(null, Message(ex), 1); }
    }

    /// <summary>
    /// The three retries. <paramref name="beforeRetry"/> is told which retry comes and how long it waits;
    /// <paramref name="wait"/> does the waiting (the window counts down); cancelling stops at once.
    /// </summary>
    public static async Task<Outcome<T>> RetryAsync<T>(Func<T> connect, string firstError, Action<RetryInfo>? beforeRetry,
                                                       Func<TimeSpan, CancellationToken, Task> wait, CancellationToken ct) where T : class
    {
        var error = firstError;
        for (var i = 0; i < RetryDelays.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            beforeRetry?.Invoke(new RetryInfo(i + 1, RetryDelays.Count, RetryDelays[i], error));
            if (RetryDelays[i] > TimeSpan.Zero) await wait(RetryDelays[i], ct);
            ct.ThrowIfCancellationRequested();
            try { return new Outcome<T>(await Task.Run(connect, ct), null, i + 2); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { error = Message(ex); }
        }
        return new Outcome<T>(null, error, RetryDelays.Count + 1);
    }

    /// <summary>The useful message (reflection and aggregate wrappers removed).</summary>
    public static string Message(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException or AggregateException && ex.InnerException is not null) ex = ex.InnerException;
        return ex.Message.Trim();
    }
}
