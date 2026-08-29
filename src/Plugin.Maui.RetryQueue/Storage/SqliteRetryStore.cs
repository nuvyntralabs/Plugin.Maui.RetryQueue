using SQLite;

namespace Plugin.Maui.RetryQueue.Storage;

/// <summary>
/// SQLite-backed durable store. Failed operations survive process death.
/// </summary>
public sealed class SqliteRetryStore : IRetryStore
{
    readonly string _path;
    readonly SemaphoreSlim _gate = new(1, 1);
    SQLiteAsyncConnection? _connection;

    public SqliteRetryStore(RetryQueueOptions options)
        : this(StoragePath.Resolve(options))
    {
    }

    public SqliteRetryStore(string databasePath)
    {
        _path = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SQLiteAsyncConnection(
                _path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            await _connection.CreateTableAsync<OperationRow>().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RetryRecord> InsertAsync(RetryRecord record, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
            {
                var existing = await db.Table<OperationRow>()
                    .Where(row => row.IdempotencyKey == record.IdempotencyKey)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);
                if (existing is not null && IsActive((RetryOperationStatus)existing.Status))
                {
                    return existing.ToRecord();
                }
            }

            await db.InsertAsync(OperationRow.FromRecord(record)).ConfigureAwait(false);
            return record.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RetryRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.FindAsync<OperationRow>(id).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<RetryRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Table<OperationRow>()
            .Where(item => item.IdempotencyKey == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<RetryRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RetryRecord? claimed = null;
            await db.RunInTransactionAsync(conn =>
            {
                var now = request.UtcNow.ToString("O");
                var online = request.IsOnline ? 1 : 0;
                var pending = (int)RetryOperationStatus.Pending;
                var failed = (int)RetryOperationStatus.Failed;
                var rows = conn.Query<OperationRow>(
                    """
                    SELECT * FROM Operations
                    WHERE Status IN (?, ?)
                      AND NextAttemptAtUtc <= ?
                      AND (RequiresNetwork = 0 OR ? = 1)
                    ORDER BY CreatedAtUtc ASC
                    LIMIT 1
                    """,
                    pending,
                    failed,
                    now,
                    online);

                var row = rows.FirstOrDefault();
                if (row is null)
                {
                    return;
                }

                row.Status = (int)RetryOperationStatus.Running;
                row.Attempts += 1;
                row.StartedAtUtc = request.UtcNow.ToString("O");
                row.LeaseOwner = request.LeaseOwner;
                row.LeaseExpiresAtUtc = request.LeaseExpiresAt.ToString("O");
                conn.Update(row);
                claimed = row.ToRecord();
            }).ConfigureAwait(false);

            return claimed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(RetryRecord record, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.UpdateAsync(OperationRow.FromRecord(record)).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await db.DeleteAsync<OperationRow>(id).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RetryRecord>> ListAsync(RetryListQuery query, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Table<OperationRow>().ToListAsync().ConfigureAwait(false);
        IEnumerable<OperationRow> items = rows;
        if (query.Status is { } status)
        {
            var value = (int)status;
            items = items.Where(row => row.Status == value);
        }

        if (!string.IsNullOrWhiteSpace(query.OperationName))
        {
            items = items.Where(row => string.Equals(row.OperationName, query.OperationName, StringComparison.Ordinal));
        }

        var take = query.Take > 0 ? query.Take : 100;
        return items
            .OrderByDescending(row => row.CreatedAtUtc)
            .Take(take)
            .Select(row => row.ToRecord())
            .ToList();
    }

    public async Task<RetryQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Table<OperationRow>().ToListAsync().ConfigureAwait(false);
        var now = utcNow.ToString("O");
        var pending = 0;
        var scheduled = 0;
        var running = 0;
        var failed = 0;
        var dead = 0;
        var succeeded = 0;
        var cancelled = 0;

        foreach (var row in rows)
        {
            switch ((RetryOperationStatus)row.Status)
            {
                case RetryOperationStatus.Pending:
                    if (string.CompareOrdinal(row.NextAttemptAtUtc, now) > 0)
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

        return new RetryQueueCounts(pending, scheduled, running, failed, dead, succeeded, cancelled);
    }

    public async Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var running = (int)RetryOperationStatus.Running;
            var rows = await db.Table<OperationRow>()
                .Where(row => row.Status == running)
                .ToListAsync()
                .ConfigureAwait(false);

            var now = utcNow.ToString("O");
            var recovered = 0;
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.LeaseExpiresAtUtc) &&
                    string.CompareOrdinal(row.LeaseExpiresAtUtc, now) > 0)
                {
                    continue;
                }

                row.Status = (int)RetryOperationStatus.Pending;
                row.LeaseOwner = null;
                row.LeaseExpiresAtUtc = null;
                row.NextAttemptAtUtc = now;
                row.LastError = "Recovered after expired lease / process death.";
                await db.UpdateAsync(row).ConfigureAwait(false);
                recovered++;
            }

            return recovered;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RequeueDeadLettersAsync(string? operationId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dead = (int)RetryOperationStatus.DeadLetter;
            var rows = await db.Table<OperationRow>()
                .Where(row => row.Status == dead)
                .ToListAsync()
                .ConfigureAwait(false);

            var count = 0;
            foreach (var row in rows)
            {
                if (operationId is not null && !string.Equals(row.Id, operationId, StringComparison.Ordinal))
                {
                    continue;
                }

                row.Status = (int)RetryOperationStatus.Pending;
                row.Attempts = 0;
                row.LastError = null;
                row.NextAttemptAtUtc = utcNow.ToString("O");
                row.LeaseOwner = null;
                row.LeaseExpiresAtUtc = null;
                await db.UpdateAsync(row).ConfigureAwait(false);
                count++;
            }

            return count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }

        _gate.Dispose();
    }

    async Task<SQLiteAsyncConnection> GetDbAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return _connection!;
    }

    static bool IsActive(RetryOperationStatus status) =>
        status is RetryOperationStatus.Pending or RetryOperationStatus.Running or RetryOperationStatus.Failed;

    [Table("Operations")]
    sealed class OperationRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        [Indexed]
        public string OperationName { get; set; } = "";

        public string? PayloadJson { get; set; }

        [Indexed]
        public int Status { get; set; }

        public int Attempts { get; set; }

        public int MaxAttempts { get; set; }

        public string CreatedAtUtc { get; set; } = "";

        [Indexed]
        public string NextAttemptAtUtc { get; set; } = "";

        public string? StartedAtUtc { get; set; }

        public string? CompletedAtUtc { get; set; }

        public string? LastError { get; set; }

        [Indexed]
        public string? IdempotencyKey { get; set; }

        public string? CorrelationId { get; set; }

        public int RequiresNetwork { get; set; }

        public string? MetadataJson { get; set; }

        public string? LeaseOwner { get; set; }

        public string? LeaseExpiresAtUtc { get; set; }

        public static OperationRow FromRecord(RetryRecord record) => new()
        {
            Id = record.Id,
            OperationName = record.OperationName,
            PayloadJson = record.PayloadJson,
            Status = (int)record.Status,
            Attempts = record.Attempts,
            MaxAttempts = record.MaxAttempts,
            CreatedAtUtc = record.CreatedAt.ToString("O"),
            NextAttemptAtUtc = record.NextAttemptAt.ToString("O"),
            StartedAtUtc = record.StartedAt?.ToString("O"),
            CompletedAtUtc = record.CompletedAt?.ToString("O"),
            LastError = record.LastError,
            IdempotencyKey = string.IsNullOrWhiteSpace(record.IdempotencyKey) ? null : record.IdempotencyKey,
            CorrelationId = record.CorrelationId,
            RequiresNetwork = record.RequiresNetwork ? 1 : 0,
            MetadataJson = RetryJson.WriteMetadata(record.Metadata),
            LeaseOwner = record.LeaseOwner,
            LeaseExpiresAtUtc = record.LeaseExpiresAt?.ToString("O")
        };

        public RetryRecord ToRecord() => new()
        {
            Id = Id,
            OperationName = OperationName,
            PayloadJson = PayloadJson,
            Status = (RetryOperationStatus)Status,
            Attempts = Attempts,
            MaxAttempts = MaxAttempts,
            CreatedAt = Parse(CreatedAtUtc),
            NextAttemptAt = Parse(NextAttemptAtUtc),
            StartedAt = ParseNullable(StartedAtUtc),
            CompletedAt = ParseNullable(CompletedAtUtc),
            LastError = LastError,
            IdempotencyKey = IdempotencyKey,
            CorrelationId = CorrelationId,
            RequiresNetwork = RequiresNetwork != 0,
            Metadata = RetryJson.ReadMetadata(MetadataJson),
            LeaseOwner = LeaseOwner,
            LeaseExpiresAt = ParseNullable(LeaseExpiresAtUtc)
        };

        static DateTimeOffset Parse(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        static DateTimeOffset? ParseNullable(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }
}
