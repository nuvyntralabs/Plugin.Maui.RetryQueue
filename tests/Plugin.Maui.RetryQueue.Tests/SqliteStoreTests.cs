namespace Plugin.Maui.RetryQueue.Tests;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task Sqlite_round_trips_an_operation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"retryqueue-{Guid.NewGuid():N}.db3");
        try
        {
            await using var store = new SqliteRetryStore(path);
            var now = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            var inserted = await store.InsertAsync(new RetryRecord
            {
                Id = "op-1",
                OperationName = "customer-registration",
                PayloadJson = """{"name":"Ada"}""",
                Status = RetryOperationStatus.Pending,
                Attempts = 0,
                MaxAttempts = 5,
                CreatedAt = now,
                NextAttemptAt = now,
                RequiresNetwork = true,
                IdempotencyKey = "customer:ada"
            });

            Assert.Equal("op-1", inserted.Id);

            var found = await store.FindByIdempotencyKeyAsync("customer:ada");
            Assert.Equal("customer-registration", found!.OperationName);
            Assert.Equal("""{"name":"Ada"}""", found.PayloadJson);

            var claimed = await store.ClaimNextAsync(new ClaimRequest(now, IsOnline: true, "test", now.AddMinutes(2)));
            Assert.Equal(RetryOperationStatus.Running, claimed!.Status);
            Assert.Equal(1, claimed.Attempts);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
