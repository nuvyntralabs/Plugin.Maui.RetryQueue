# Changelog

## 1.0.0

- Failed-operation retry queue for .NET MAUI on iOS and Android
- `EnqueueAsync("name", async () => await Work())` for telemetry, orders, forms, and payments
- Default backoff schedule: 30 seconds → 2 minutes → 10 minutes, with jitter
- Exponential, constant, and custom delay schedules
- SQLite persistence, max attempts, cancellation, and a dead-letter queue
- Network gate, process-death lease recovery, and `DrainAsync` for tests or OS wakes
- Named handler registration so retries survive process death
