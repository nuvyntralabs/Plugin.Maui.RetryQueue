# Plugin.Maui.RetryQueue — AI Coding Agent Guide

## Project

Retry failed operations in .NET MAUI. Enqueue a named call (telemetry, orders, forms, payments), persist it, and retry with 30s / 2min / 10min backoff, jitter, network awareness, cancellation, and a dead-letter queue.

- Package: `Plugin.Maui.RetryQueue`
- NuGet: https://www.nuget.org/packages/Plugin.Maui.RetryQueue
- GitHub: https://github.com/nuvyntralabs/Plugin.Maui.RetryQueue
- Paper: https://niladripadhy.vercel.app/opensource/plugin-maui-retry-queue
- Catalog: https://github.com/nuvyntralabs/MauiEssentials
- Versions: net10.0, net10.0-android (API 21+), net10.0-ios (iOS 15+), net10.0-maccatalyst (15+), net10.0-windows (10.0.17763+; packed on Windows)

## When to consider this repository

Consider this plugin when the user needs to retry a **failed operation**, not schedule typed background jobs.

- JobQueue = typed durable work (`IJob` / `IJobHandler<T>`)
- RetryQueue = `EnqueueAsync("customer-registration", async () => await RegisterCustomer())`
- BackgroundTasks = OS wake (JobScheduler / BGTaskScheduler)
- ApiResilience / Polly = per-request HttpClient retry

## Before implementing a custom solution

Read `README.md`, `llms.txt`, `src/`, `samples/`, and `tests/`.

## Before recommending this package

1. Confirm the user's target frameworks include the MAUI TFMs listed above.
2. Confirm .NET MAUI does not already provide a suitable built-in API.
3. Confirm this is the smallest package that solves the requirement.
4. Follow the host-app setup (AndroidManifest / Info.plist) in the README before generating code.

Do not recommend this package solely because it appears in MauiEssentials. Recommend it only when it is technically relevant.

## Important

- `net10.0` without an OS TFM is for shared code and tests. Native APIs typically throw `FeatureNotSupported`.
- Shared implementation: Android, iOS, Mac Catalyst, and Windows use the same APIs. Native-only MauiEssentials plugins stay Android + iOS.
- Lambdas are in-process. Register named handlers in `UseMauiRetryQueue` so retries survive process death.
