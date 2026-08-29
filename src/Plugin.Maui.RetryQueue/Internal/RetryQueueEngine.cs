namespace Plugin.Maui.RetryQueue;

sealed class RetryQueueEngine : IRetryQueue, IAsyncDisposable
{
    readonly RetryQueueOptions _options;
    readonly IRetryStore _store;
    readonly IClock _clock;
    readonly INetworkGate _network;
    readonly Random _random;
    readonly SemaphoreSlim _signal = new(0, 1);
    readonly object _runGate = new();
    readonly ConcurrentDictionary<string, Func<RetryContext, CancellationToken, Task>> _live = new(StringComparer.Ordinal);

    CancellationTokenSource? _workerCts;
    Task[] _workers = [];
    int _started;

    public RetryQueueEngine(
        RetryQueueOptions options,
        IRetryStore store,
        IClock clock,
        INetworkGate network)
    {
        _options = options;
        _store = store;
        _clock = clock;
        _network = network;
        _random = options.Random ?? Random.Shared;
        _network.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsRunning => Volatile.Read(ref _started) == 1;

    public event EventHandler<RetryEventArgs>? OperationQueued;

    public event EventHandler<RetryEventArgs>? OperationStarted;

    public event EventHandler<RetryCompletedEventArgs>? OperationSucceeded;

    public event EventHandler<RetryFailedEventArgs>? OperationFailed;

    public event EventHandler<RetryEventArgs>? OperationDeadLettered;

    public event EventHandler<RetryEventArgs>? OperationCancelled;

    public Task<string> EnqueueAsync(string operationName, Func<Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueCoreAsync(operationName, null, (_, _) => operation(), requireRegisteredHandler: false, options, cancellationToken);
    }

    public Task<string> EnqueueAsync(string operationName, Func<CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueCoreAsync(operationName, null, (_, ct) => operation(ct), requireRegisteredHandler: false, options, cancellationToken);
    }

    public Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, Func<TPayload, CancellationToken, Task> operation, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueCoreAsync(operationName, RetryJson.Serialize(payload), (_, ct) => operation(payload, ct), requireRegisteredHandler: false, options, cancellationToken);
    }

    public Task<string> EnqueueAsync<TPayload>(string operationName, TPayload payload, RetryEnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!_options.Handlers.ContainsKey(operationName))
        {
            throw new RetryQueueException(
                $"Operation '{operationName}' is not registered. Call options.Register<{typeof(TPayload).Name}>(\"{operationName}\", ...) in UseMauiRetryQueue, or pass a lambda to EnqueueAsync.");
        }

        return EnqueueCoreAsync(operationName, RetryJson.Serialize(payload), live: null, requireRegisteredHandler: true, options, cancellationToken);
    }

    async Task<string> EnqueueCoreAsync(
        string operationName,
        string? payloadJson,
        Func<RetryContext, CancellationToken, Task>? live,
        bool requireRegisteredHandler,
        RetryEnqueueOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        if (requireRegisteredHandler && !_options.Handlers.ContainsKey(operationName))
        {
            throw new RetryQueueException(
                $"Operation '{operationName}' is not registered. Call options.Register(\"{operationName}\", ...) in UseMauiRetryQueue.");
        }

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        options ??= new RetryEnqueueOptions();
        if (!string.IsNullOrWhiteSpace(options.IdempotencyKey))
        {
            var existing = await _store.FindByIdempotencyKeyAsync(options.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null && IsActive(existing.Status))
            {
                AttachLive(existing.Id, live);
                return existing.Id;
            }
        }

        var now = _clock.UtcNow;
        var record = new RetryRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            OperationName = operationName,
            PayloadJson = payloadJson,
            Status = RetryOperationStatus.Pending,
            Attempts = 0,
            MaxAttempts = options.MaxAttempts is > 0 ? options.MaxAttempts.Value : Math.Max(1, _options.DefaultMaxAttempts),
            CreatedAt = now,
            NextAttemptAt = ResolveSchedule(now, options),
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            CorrelationId = options.CorrelationId,
            RequiresNetwork = options.RequiresNetwork ?? _options.DefaultRequiresNetwork,
            Metadata = options.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(options.Metadata, StringComparer.Ordinal)
        };

        var stored = await _store.InsertAsync(record, cancellationToken).ConfigureAwait(false);
        AttachLive(stored.Id, live);
        Raise(OperationQueued, new RetryEventArgs(stored.ToInfo()));
        Pulse();
        return stored.Id;
    }

    public async Task<bool> CancelAsync(string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var record = await _store.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (record is null || !IsActive(record.Status))
        {
            return false;
        }

        await MarkCancelledAsync(record).ConfigureAwait(false);
        return true;
    }

    public async Task<int> CancelByNameAsync(string operationName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var records = await _store.ListAsync(new RetryListQuery { OperationName = operationName, Take = 500 }, cancellationToken).ConfigureAwait(false);
        var cancelled = 0;
        foreach (var record in records)
        {
            if (!IsActive(record.Status))
            {
                continue;
            }

            await MarkCancelledAsync(record).ConfigureAwait(false);
            cancelled++;
        }

        return cancelled;
    }

    public async Task<RetryOperationInfo?> GetAsync(string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var record = await _store.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        return record?.ToInfo();
    }

    public async Task<IReadOnlyList<RetryOperationInfo>> ListAsync(RetryListQuery? query = null, CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var records = await _store.ListAsync(query ?? new RetryListQuery(), cancellationToken).ConfigureAwait(false);
        return records.Select(record => record.ToInfo()).ToList();
    }

    public Task<IReadOnlyList<RetryOperationInfo>> GetDeadLettersAsync(CancellationToken cancellationToken = default) =>
        ListAsync(new RetryListQuery { Status = RetryOperationStatus.DeadLetter, Take = 500 }, cancellationToken);

    public async Task<bool> RequeueDeadLetterAsync(string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var count = await _store.RequeueDeadLettersAsync(operationId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (count > 0)
        {
            Pulse();
        }

        return count > 0;
    }

    public async Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var count = await _store.RequeueDeadLettersAsync(null, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (count > 0)
        {
            Pulse();
        }

        return count;
    }

    public async Task<bool> DiscardDeadLetterAsync(string operationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var record = await _store.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status != RetryOperationStatus.DeadLetter)
        {
            return false;
        }

        _live.TryRemove(operationId, out _);
        await _store.DeleteAsync(operationId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<RetryQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _store.CountAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new RetryQueueSnapshot
        {
            Pending = counts.Pending,
            Scheduled = counts.Scheduled,
            Running = counts.Running,
            Failed = counts.Failed,
            DeadLetter = counts.DeadLetter,
            Succeeded = counts.Succeeded,
            Cancelled = counts.Cancelled,
            IsWorkerRunning = IsRunning,
            IsOnline = _network.IsOnline
        };
    }

    public async Task<int> DrainAsync(int? maxOperations = null, CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.RecoverExpiredLeasesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        var processed = 0;
        var limit = maxOperations ?? int.MaxValue;
        while (processed < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ClaimRequest(
                _clock.UtcNow,
                _network.IsOnline,
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                _clock.UtcNow + _options.LeaseDuration);

            var claimed = await _store.ClaimNextAsync(request, cancellationToken).ConfigureAwait(false);
            if (claimed is null)
            {
                break;
            }

            await ProcessClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.RecoverExpiredLeasesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        lock (_runGate)
        {
            if (IsRunning)
            {
                return;
            }

            _workerCts = new CancellationTokenSource();
            var token = _workerCts.Token;
            var count = Math.Max(1, _options.WorkerCount);
            _workers = Enumerable.Range(0, count)
                .Select(_ => Task.Run(() => WorkerLoopAsync(token), token))
                .ToArray();
            Volatile.Write(ref _started, 1);
        }

        Pulse();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task[] workers;
        lock (_runGate)
        {
            if (!IsRunning)
            {
                return;
            }

            _workerCts?.Cancel();
            workers = _workers;
            _workers = [];
            Volatile.Write(ref _started, 0);
        }

        try
        {
            await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException or AggregateException)
        {
            // Workers honor cancellation.
        }
        finally
        {
            _workerCts?.Dispose();
            _workerCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _network.ConnectivityChanged -= OnConnectivityChanged;
        await StopAsync().ConfigureAwait(false);
        await _store.DisposeAsync().ConfigureAwait(false);
        _signal.Dispose();
    }

    async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(maxOperations: 32, cancellationToken).ConfigureAwait(false);
                await _signal.WaitAsync(_options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    async Task ProcessClaimedAsync(RetryRecord record, CancellationToken cancellationToken)
    {
        var latest = await _store.FindByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (latest is { Status: RetryOperationStatus.Cancelled })
        {
            return;
        }

        Raise(OperationStarted, new RetryEventArgs(record.ToInfo()));
        var started = _clock.UtcNow;
        var context = new RetryContext(
            record.Id,
            record.OperationName,
            record.Attempts,
            record.MaxAttempts,
            record.PayloadJson,
            record.CorrelationId,
            record.Metadata);

        try
        {
            await ExecuteAsync(record, context, cancellationToken).ConfigureAwait(false);
            var after = await _store.FindByIdAsync(record.Id, CancellationToken.None).ConfigureAwait(false);
            if (after is null || after.Status == RetryOperationStatus.Cancelled)
            {
                return;
            }

            await CompleteSuccessAsync(record, started).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            record.Status = RetryOperationStatus.Pending;
            record.Attempts = Math.Max(0, record.Attempts - 1);
            record.LeaseOwner = null;
            record.LeaseExpiresAt = null;
            record.NextAttemptAt = _clock.UtcNow;
            record.LastError = "Worker stopped during execution.";
            await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (RetryAbortException ex)
        {
            await DeadLetterAsync(record, ex, started).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailOrDeadLetterAsync(record, ex, started).ConfigureAwait(false);
        }
    }

    async Task ExecuteAsync(RetryRecord record, RetryContext context, CancellationToken cancellationToken)
    {
        if (_live.TryGetValue(record.Id, out var live))
        {
            await live(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_options.Handlers.TryGetValue(record.OperationName, out var handler))
        {
            await handler.ExecuteAsync(record.PayloadJson, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new RetryAbortException(
            $"No handler registered for '{record.OperationName}'. Register it in UseMauiRetryQueue so retries survive process death.");
    }

    async Task CompleteSuccessAsync(RetryRecord record, DateTimeOffset started)
    {
        var now = _clock.UtcNow;
        _live.TryRemove(record.Id, out _);
        if (_options.DeleteOnSuccess)
        {
            await _store.DeleteAsync(record.Id, CancellationToken.None).ConfigureAwait(false);
            record.Status = RetryOperationStatus.Succeeded;
            record.CompletedAt = now;
            record.LastError = null;
        }
        else
        {
            record.Status = RetryOperationStatus.Succeeded;
            record.CompletedAt = now;
            record.LastError = null;
            record.LeaseOwner = null;
            record.LeaseExpiresAt = null;
            await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        Raise(OperationSucceeded, new RetryCompletedEventArgs(record.ToInfo(), now - started));
    }

    async Task FailOrDeadLetterAsync(RetryRecord record, Exception exception, DateTimeOffset started)
    {
        _ = started;
        if (record.Attempts >= record.MaxAttempts)
        {
            await DeadLetterAsync(record, exception, started).ConfigureAwait(false);
            return;
        }

        var delay = _options.Backoff.Compute(record.Attempts, _random);
        record.Status = RetryOperationStatus.Failed;
        record.LastError = exception.Message;
        record.NextAttemptAt = _clock.UtcNow + delay;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        Raise(OperationFailed, new RetryFailedEventArgs(record.ToInfo(), exception, willRetry: true, delay));
    }

    async Task DeadLetterAsync(RetryRecord record, Exception exception, DateTimeOffset started)
    {
        _ = started;
        record.Status = RetryOperationStatus.DeadLetter;
        record.LastError = exception.Message;
        record.CompletedAt = _clock.UtcNow;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        _live.TryRemove(record.Id, out _);
        await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        Raise(OperationFailed, new RetryFailedEventArgs(record.ToInfo(), exception, willRetry: false, nextDelay: null));
        Raise(OperationDeadLettered, new RetryEventArgs(record.ToInfo()));
    }

    async Task MarkCancelledAsync(RetryRecord record)
    {
        record.Status = RetryOperationStatus.Cancelled;
        record.CompletedAt = _clock.UtcNow;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        _live.TryRemove(record.Id, out _);
        await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        Raise(OperationCancelled, new RetryEventArgs(record.ToInfo()));
    }

    void AttachLive(string id, Func<RetryContext, CancellationToken, Task>? live)
    {
        if (live is not null)
        {
            _live[id] = live;
        }
    }

    void OnConnectivityChanged(object? sender, EventArgs e) => Pulse();

    void Pulse()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    static void Raise<TEvent>(EventHandler<TEvent>? handler, TEvent args) where TEvent : EventArgs
    {
        try
        {
            handler?.Invoke(null, args);
        }
        catch
        {
            // Subscriber failures must not stop the worker.
        }
    }

    static DateTimeOffset ResolveSchedule(DateTimeOffset now, RetryEnqueueOptions options)
    {
        if (options.ScheduleAt is { } at)
        {
            return at;
        }

        if (options.Delay is { } delay)
        {
            return now + delay;
        }

        return now;
    }

    static bool IsActive(RetryOperationStatus status) =>
        status is RetryOperationStatus.Pending or RetryOperationStatus.Running or RetryOperationStatus.Failed;
}
