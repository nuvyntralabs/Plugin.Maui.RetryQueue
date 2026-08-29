namespace Plugin.Maui.RetryQueue.Storage;

/// <summary>
/// Process-local store for tests and demos. Does not survive process death.
/// </summary>
public sealed class InMemoryRetryStore : IRetryStore
{
    readonly object _gate = new();
    readonly Dictionary<string, RetryRecord> _operations = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _idempotency = new(StringComparer.Ordinal);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<RetryRecord> InsertAsync(RetryRecord record, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey) &&
                _idempotency.TryGetValue(record.IdempotencyKey, out var existingId) &&
                _operations.TryGetValue(existingId, out var existing) &&
                IsActive(existing.Status))
            {
                return Task.FromResult(existing.Clone());
            }

            var copy = record.Clone();
            _operations[copy.Id] = copy;
            if (!string.IsNullOrWhiteSpace(copy.IdempotencyKey))
            {
                _idempotency[copy.IdempotencyKey] = copy.Id;
            }

            return Task.FromResult(copy.Clone());
        }
    }

    public Task<RetryRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_operations.TryGetValue(id, out var record) ? record.Clone() : null);
        }
    }

    public Task<RetryRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_idempotency.TryGetValue(key, out var id) && _operations.TryGetValue(id, out var record))
            {
                return Task.FromResult<RetryRecord?>(record.Clone());
            }

            return Task.FromResult<RetryRecord?>(null);
        }
    }

    public Task<RetryRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var next = _operations.Values
                .Where(operation => IsClaimable(operation, request))
                .OrderBy(operation => operation.CreatedAt)
                .FirstOrDefault();

            if (next is null)
            {
                return Task.FromResult<RetryRecord?>(null);
            }

            next.Status = RetryOperationStatus.Running;
            next.Attempts += 1;
            next.StartedAt = request.UtcNow;
            next.LeaseOwner = request.LeaseOwner;
            next.LeaseExpiresAt = request.LeaseExpiresAt;
            return Task.FromResult<RetryRecord?>(next.Clone());
        }
    }

    public Task UpdateAsync(RetryRecord record, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _operations[record.Id] = record.Clone();
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
            {
                _idempotency[record.IdempotencyKey] = record.Id;
            }

            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_operations.Remove(id, out var removed) && !string.IsNullOrWhiteSpace(removed.IdempotencyKey))
            {
                _idempotency.Remove(removed.IdempotencyKey);
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<RetryRecord>> ListAsync(RetryListQuery query, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<RetryRecord> items = _operations.Values;
            if (query.Status is { } status)
            {
                items = items.Where(operation => operation.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query.OperationName))
            {
                items = items.Where(operation => string.Equals(operation.OperationName, query.OperationName, StringComparison.Ordinal));
            }

            var take = query.Take > 0 ? query.Take : 100;
            var list = items
                .OrderByDescending(operation => operation.CreatedAt)
                .Take(take)
                .Select(operation => operation.Clone())
                .ToList();

            return Task.FromResult<IReadOnlyList<RetryRecord>>(list);
        }
    }

    public Task<RetryQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var pending = 0;
            var scheduled = 0;
            var running = 0;
            var failed = 0;
            var dead = 0;
            var succeeded = 0;
            var cancelled = 0;

            foreach (var operation in _operations.Values)
            {
                switch (operation.Status)
                {
                    case RetryOperationStatus.Pending:
                        if (operation.NextAttemptAt > utcNow)
                        {
                            scheduled++;
                        }
                        else
                        {
                            pending++;
                        }

                        break;
                    case RetryOperationStatus.Running:
                        running++;
                        break;
                    case RetryOperationStatus.Failed:
                        failed++;
                        break;
                    case RetryOperationStatus.DeadLetter:
                        dead++;
                        break;
                    case RetryOperationStatus.Succeeded:
                        succeeded++;
                        break;
                    case RetryOperationStatus.Cancelled:
                        cancelled++;
                        break;
                }
            }

            return Task.FromResult(new RetryQueueCounts(pending, scheduled, running, failed, dead, succeeded, cancelled));
        }
    }

    public Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var recovered = 0;
            foreach (var operation in _operations.Values)
            {
                if (operation.Status != RetryOperationStatus.Running)
                {
                    continue;
                }

                if (operation.LeaseExpiresAt is { } expires && expires > utcNow)
                {
                    continue;
                }

                operation.Status = RetryOperationStatus.Pending;
                operation.LeaseOwner = null;
                operation.LeaseExpiresAt = null;
                operation.NextAttemptAt = utcNow;
                operation.LastError = "Recovered after expired lease / process death.";
                recovered++;
            }

            return Task.FromResult(recovered);
        }
    }

    public Task<int> RequeueDeadLettersAsync(string? operationId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var operation in _operations.Values)
            {
                if (operation.Status != RetryOperationStatus.DeadLetter)
                {
                    continue;
                }

                if (operationId is not null && !string.Equals(operation.Id, operationId, StringComparison.Ordinal))
                {
                    continue;
                }

                operation.Status = RetryOperationStatus.Pending;
                operation.Attempts = 0;
                operation.LastError = null;
                operation.NextAttemptAt = utcNow;
                operation.LeaseOwner = null;
                operation.LeaseExpiresAt = null;
                count++;
            }

            return Task.FromResult(count);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static bool IsActive(RetryOperationStatus status) =>
        status is RetryOperationStatus.Pending or RetryOperationStatus.Running or RetryOperationStatus.Failed;

    static bool IsClaimable(RetryRecord operation, ClaimRequest request)
    {
        if (operation.Status is not (RetryOperationStatus.Pending or RetryOperationStatus.Failed))
        {
            return false;
        }

        if (operation.NextAttemptAt > request.UtcNow)
        {
            return false;
        }

        if (operation.RequiresNetwork && !request.IsOnline)
        {
            return false;
        }

        return true;
    }
}
