namespace Plugin.Maui.RetryQueue.Tests;

public sealed class RetryEngineTests
{
    [Fact]
    public async Task Enqueue_lambda_then_drain_deletes_on_success()
    {
        var (queue, _, _, store, _) = Harness.Create();
        var ran = 0;

        var id = await queue.EnqueueAsync("customer-registration", async () =>
        {
            ran++;
            await Task.CompletedTask;
        });

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Equal(1, ran);
        Assert.Null(await store.FindByIdAsync(id));
    }

    [Fact]
    public async Task Failure_retries_with_backoff_then_succeeds()
    {
        var (queue, clock, _, store, _) = Harness.Create();
        var attempt = 0;

        var id = await queue.EnqueueAsync("customer-registration", async () =>
        {
            attempt++;
            if (attempt < 3)
            {
                throw new InvalidOperationException($"transient {attempt}");
            }

            await Task.CompletedTask;
        });

        Assert.Equal(1, await queue.DrainAsync());
        var afterFirst = await store.FindByIdAsync(id);
        Assert.NotNull(afterFirst);
        Assert.Equal(RetryOperationStatus.Failed, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(1), afterFirst.NextAttemptAt);

        Assert.Equal(0, await queue.DrainAsync());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await queue.DrainAsync());
        var afterSecond = await store.FindByIdAsync(id);
        Assert.Equal(RetryOperationStatus.Failed, afterSecond!.Status);
        Assert.Equal(2, afterSecond.Attempts);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Null(await store.FindByIdAsync(id));
        Assert.Equal(3, attempt);
    }

    [Fact]
    public async Task Exhausted_retries_go_to_dead_letter()
    {
        var (queue, clock, _, _, _) = Harness.Create(options => options.DefaultMaxAttempts = 2);

        var id = await queue.EnqueueAsync("order-submit", async () =>
            throw new InvalidOperationException("gateway timeout"));

        await queue.DrainAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await queue.DrainAsync();

        var dead = await queue.GetDeadLettersAsync();
        var operation = Assert.Single(dead);
        Assert.Equal(id, operation.Id);
        Assert.Equal(RetryOperationStatus.DeadLetter, operation.Status);
        Assert.Equal(2, operation.Attempts);
        Assert.Equal("gateway timeout", operation.LastError);
    }

    [Fact]
    public async Task Abort_skips_retries()
    {
        var (queue, _, _, _, _) = Harness.Create();

        var id = await queue.EnqueueAsync("customer-registration", async ct =>
        {
            _ = ct;
            throw new RetryAbortException("duplicate customer");
        });
        await queue.DrainAsync();

        var operation = await queue.GetAsync(id);
        Assert.Equal(RetryOperationStatus.DeadLetter, operation!.Status);
        Assert.Equal(1, operation.Attempts);
        Assert.Equal("duplicate customer", operation.LastError);
    }

    [Fact]
    public async Task Requeue_dead_letter_runs_again()
    {
        var (queue, _, _, _, _) = Harness.Create(options => options.DefaultMaxAttempts = 1);

        var id = await queue.EnqueueAsync("telemetry", async () => throw new InvalidOperationException("offline"));
        await queue.DrainAsync();
        Assert.True(await queue.RequeueDeadLetterAsync(id));

        var pending = await queue.GetAsync(id);
        Assert.Equal(RetryOperationStatus.Pending, pending!.Status);
        Assert.Equal(0, pending.Attempts);
    }

    [Fact]
    public async Task Idempotency_key_returns_existing_operation()
    {
        var (queue, _, _, _, _) = Harness.Create();
        var options = new RetryEnqueueOptions { IdempotencyKey = "customer:42" };

        var first = await queue.EnqueueAsync("customer-registration", async () => await Task.CompletedTask, options);
        var second = await queue.EnqueueAsync("customer-registration", async () => await Task.CompletedTask, options);

        Assert.Equal(first, second);
        var snapshot = await queue.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Pending);
    }

    [Fact]
    public async Task Cancel_prevents_execution()
    {
        var (queue, _, _, _, _) = Harness.Create();
        var ran = 0;

        var id = await queue.EnqueueAsync("customer-registration", async () =>
        {
            ran++;
            await Task.CompletedTask;
        });
        Assert.True(await queue.CancelAsync(id));
        await queue.DrainAsync();

        Assert.Equal(0, ran);
        Assert.Equal(RetryOperationStatus.Cancelled, (await queue.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Cancel_by_name_cancels_matching_operations()
    {
        var (queue, _, _, _, _) = Harness.Create();

        await queue.EnqueueAsync("customer-registration", async () => await Task.CompletedTask);
        await queue.EnqueueAsync("customer-registration", async () => await Task.CompletedTask);
        await queue.EnqueueAsync("telemetry", async () =>
        {
            Counters.Telemetry++;
            await Task.CompletedTask;
        });

        Assert.Equal(2, await queue.CancelByNameAsync("customer-registration"));
        await queue.DrainAsync();

        var remaining = await queue.ListAsync();
        Assert.DoesNotContain(remaining, item => item.OperationName == "customer-registration" && item.Status != RetryOperationStatus.Cancelled);
        Assert.Equal(1, Counters.Telemetry);
    }

    [Fact]
    public async Task Delayed_operation_waits_for_clock()
    {
        var (queue, clock, _, _, _) = Harness.Create();
        var ran = 0;

        await queue.EnqueueAsync("telemetry", async () =>
        {
            ran++;
            await Task.CompletedTask;
        }, new RetryEnqueueOptions { Delay = TimeSpan.FromMinutes(5), RequiresNetwork = false });

        Assert.Equal(0, await queue.DrainAsync());
        var snapshot = await queue.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Scheduled);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Requires_network_skips_when_offline()
    {
        var (queue, _, network, _, _) = Harness.Create();
        var ran = 0;
        network.IsOnline = false;

        await queue.EnqueueAsync("order-submit", async () =>
        {
            ran++;
            await Task.CompletedTask;
        });
        Assert.Equal(0, await queue.DrainAsync());
        Assert.Equal(0, ran);

        network.IsOnline = true;
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Equal(1, ran);
    }

    [Fact]
    public async Task Payload_enqueue_uses_registered_handler()
    {
        var (queue, _, _, _, _) = Harness.Create();

        await queue.EnqueueAsync("order-submit", new CustomerDto { Name = "Ada" }, new RetryEnqueueOptions
        {
            RequiresNetwork = false
        });
        await queue.DrainAsync();

        Assert.Equal(1, Counters.Orders);
        Assert.Equal(["Ada"], Counters.Payloads);
    }

    [Fact]
    public async Task Payload_enqueue_without_handler_throws()
    {
        var (queue, _, _, _, _) = Harness.Create();

        await Assert.ThrowsAsync<RetryQueueException>(() =>
            queue.EnqueueAsync("unknown-op", new CustomerDto { Name = "x" }));
    }

    [Fact]
    public async Task Registered_handler_runs_when_lambda_is_gone()
    {
        var (queue, _, _, store, _) = Harness.Create();

        var id = await queue.EnqueueAsync("customer-registration", new CustomerDto { Name = "hidden" }, new RetryEnqueueOptions
        {
            RequiresNetwork = false
        });

        // Simulate process death: no live lambda, only the persisted name + registered handler.
        var record = await store.FindByIdAsync(id);
        Assert.NotNull(record);

        await queue.DrainAsync();
        Assert.Equal(1, Counters.Registrations);
    }

    [Fact]
    public async Task Unregistered_name_without_lambda_dead_letters()
    {
        var clock = new FakeClock();
        var store = new InMemoryRetryStore();
        await store.InsertAsync(new RetryRecord
        {
            Id = "orphan",
            OperationName = "missing-handler",
            Status = RetryOperationStatus.Pending,
            Attempts = 0,
            MaxAttempts = 5,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
            RequiresNetwork = false
        });

        var (queue, _, _, _, _) = Harness.Create(options =>
        {
            options.Store = store;
            options.Clock = clock;
        });

        await queue.DrainAsync();
        var dead = await queue.GetAsync("orphan");
        Assert.Equal(RetryOperationStatus.DeadLetter, dead!.Status);
        Assert.Contains("No handler registered", dead.LastError);
    }

    [Fact]
    public async Task Expired_lease_is_recovered_on_drain()
    {
        var clock = new FakeClock();
        var store = new InMemoryRetryStore();
        await store.InsertAsync(new RetryRecord
        {
            Id = "stuck",
            OperationName = "telemetry",
            Status = RetryOperationStatus.Running,
            Attempts = 1,
            MaxAttempts = 5,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
            LeaseOwner = "dead-process",
            LeaseExpiresAt = clock.UtcNow.AddMinutes(-1),
            RequiresNetwork = false
        });

        var (queue, _, _, _, _) = Harness.Create(options =>
        {
            options.Store = store;
            options.Clock = clock;
        });

        await queue.DrainAsync();
        Assert.Equal(1, Counters.Telemetry);
        Assert.Null(await store.FindByIdAsync("stuck"));
    }

    [Fact]
    public async Task Retain_succeeded_operations_when_delete_disabled()
    {
        var (queue, _, _, _, _) = Harness.Create(options => options.DeleteOnSuccess = false);

        var id = await queue.EnqueueAsync("telemetry", async () => await Task.CompletedTask, new RetryEnqueueOptions
        {
            RequiresNetwork = false
        });
        await queue.DrainAsync();

        var operation = await queue.GetAsync(id);
        Assert.Equal(RetryOperationStatus.Succeeded, operation!.Status);
    }

    [Fact]
    public async Task Discard_removes_dead_letter()
    {
        var (queue, _, _, _, _) = Harness.Create(options => options.DefaultMaxAttempts = 1);

        var id = await queue.EnqueueAsync("telemetry", async () => throw new InvalidOperationException("nope"));
        await queue.DrainAsync();
        Assert.True(await queue.DiscardDeadLetterAsync(id));
        Assert.Null(await queue.GetAsync(id));
    }
}
