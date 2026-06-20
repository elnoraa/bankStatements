---
name: 01-rabbitmq-reliability-improvements
status: pending
created_at: 2026-06-20T15:04:15.956451
---

# RabbitMQ Reliability Improvements

## Overview

Enhance the RabbitMQ messaging layer to achieve reliable **at-least-once delivery with idempotent consumption** (effectively exactly-once semantics), plus a **dead letter queue** for poison messages. This prevents infinite retries on corrupt PDFs and ensures no messages are lost.

**Key design decision**: Use RabbitMQ **policies** (not queue arguments) for dead-letter exchange, routing key, and delivery-limit. This means:
- ✅ No queue deletion needed — existing `process-statement` queue is not modified
- ✅ Zero-downtime migration — policy applies dynamically to the running queue
- ✅ Clean separation — topology changes live in the code, policy lives at the broker level

---

## Current Gaps

| Issue | Current Behaviour | Target |
|---|---|---|
| **Publisher confirms** | ❌ Not used — message can be lost before broker persists it | ✅ Await broker confirm after each publish |
| **Consumer retries** | ❌ Infinite retry on all failures | ✅ Max 5 retries, then DLQ |
| **Dead letter queue** | ❌ No DLX/DLQ configured | ✅ DLQ for poison messages (corrupt PDFs, permanent failures) |
| **Connection resilience** | ⚠️ No automatic recovery config | ✅ Enable automatic recovery with topology recovery |
| **Delivery limit** | ❌ No way to distinguish transient vs permanent failures | ✅ Broker-level delivery-limit policy |

---

## Files to Modify

| File | Change |
|---|---|
| `docker-compose.yml` | Add RabbitMQ policy definition via config file or startup script |
| `backend/Statements.WebAPI/Services/Messaging/RabbitMqPublisher.cs` | Add publisher confirms (`ConfirmSelectAsync` + `WaitForConfirmsAsync`), lazy async init, automatic recovery, persistent delivery mode |
| `backend/Statements.WebAPI/Services/Messaging/StatementProcessingBackgroundService.cs` | Declare DLX exchange + DLQ. Consumer still NACKs with `requeue=true` — the broker's delivery-limit policy dead-letters after N retries, not the app code. Add outer retry loop for connection resilience. |
| `backend/Statements.WebAPI/Services/Statements/StatementService.cs` | Handle `PublishAsync` failure gracefully — log and continue (statement stays as `uploaded` for manual retry) |
| `backend/Statements.WebAPI/Services/Statements/ProcessStatementConsumer.cs` | Already idempotent via `SELECT status` check — no change needed (just add a clarifying comment) |
| `backend/Statements.WebAPI/Contracts/Messages/ProcessStatementMessage.cs` | No change needed |
| `backend/Statements.WebAPI/appsettings.Development.json` | No change needed |
| `backend/Statements.WebAPI/Program.cs` | No change needed |

**New files**:

| File | Purpose |
|---|---|
| `rabbitmq/definitions.json` | RabbitMQ definitions file with policies (loaded on container startup) |
| `rabbitmq/Dockerfile` (optional) | Or just mount a config file into the existing rabbitmq container |

---

## Step-by-Step Implementation

### Step 0: RabbitMQ Broker Configuration — Apply Policy

The **simplest approach** is to apply the policy via `rabbitmqctl` by extending the RabbitMQ container's startup. Since the container uses the `rabbitmq:3.13-management` image, we can mount a custom `rabbitmq.conf` or use the management HTTP API.

**Recommended approach**: Add a **RabbitMQ advanced config file** that defines the policy on broker start.

Create a new directory `rabbitmq/` with:

**`rabbitmq/definitions.json`**:
```json
{
  "policies": [
    {
      "vhost": "/",
      "name": "statement-processing",
      "pattern": "^process-statement$",
      "apply-to": "queues",
      "definition": {
        "dead-letter-exchange": "process-statement.dlx",
        "dead-letter-routing-key": "process-statement.dlq",
        "delivery-limit": 5
      },
      "priority": 0
    }
  ],
  "exchanges": [
    {
      "name": "process-statement.dlx",
      "vhost": "/",
      "type": "direct",
      "durable": true,
      "auto_delete": false,
      "internal": false,
      "arguments": {}
    }
  ],
  "queues": [
    {
      "name": "process-statement.dlq",
      "vhost": "/",
      "durable": true,
      "auto_delete": false,
      "arguments": {}
    }
  ],
  "bindings": [
    {
      "source": "process-statement.dlx",
      "vhost": "/",
      "destination": "process-statement.dlq",
      "destination_type": "queue",
      "routing_key": "process-statement.dlq",
      "arguments": {}
    }
  ]
}
```

**Update `docker-compose.yml`** — mount this definitions file into the RabbitMQ container and set the `RABBITMQ_DEFINITIONS_FILE` env var:

```yaml
rabbitmq:
  image: rabbitmq:3.13-management
  ports:
    - "127.0.0.1:5672:5672"
    - "127.0.0.1:15672:15672"
  environment:
    RABBITMQ_DEFAULT_USER: ${RABBITMQ_DEFAULT_USER:-guest}
    RABBITMQ_DEFAULT_PASS: ${RABBITMQ_DEFAULT_PASS:-guest}
    RABBITMQ_DEFINITIONS_FILE: /etc/rabbitmq/definitions.json
    RABBITMQ_LOAD_DEFINITIONS: true
  volumes:
    - rabbitmq-data:/var/lib/rabbitmq
    - ./rabbitmq/definitions.json:/etc/rabbitmq/definitions.json:ro
  healthcheck:
    test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
    interval: 10s
    timeout: 5s
    retries: 5
  restart: unless-stopped
```

**Important**: This definitions file is loaded **on every RabbitMQ container start**, not just the first one. So it's safe to add to an existing deployment. The policy and DLX/DLQ will be created/reconciled on restart.

**Alternative (no restart needed)**: You can also apply the policy at runtime via the management HTTP API or `rabbitmqctl`:

```bash
# Inside the rabbitmq container:
rabbitmqctl set_policy statement-processing "^process-statement$" \
  '{"dead-letter-exchange":"process-statement.dlx","dead-letter-routing-key":"process-statement.dlq","delivery-limit":5}' \
  --apply-to queues

rabbitmqadmin declare exchange name=process-statement.dlx type=direct durable=true
rabbitmqadmin declare queue name=process-statement.dlq durable=true
rabbitmqadmin declare binding source=process-statement.dlx destination=process-statement.dlq routing_key=process-statement.dlq
```

For the plan, we'll do the **definitions.json approach** — it's self-documenting and applies automatically.

---

### Step 1: Update `RabbitMqPublisher.cs` — Add Publisher Confirms + Resilience

**Full new file structure:**

```csharp
using System.Text.Json;
using RabbitMQ.Client;
using Statements.WebAPI.Contracts.Messages;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Publishes messages to a RabbitMQ queue using the RabbitMQ.Client library.
/// Connection and channel are created lazily on first publish (async-safe).
/// Publisher confirms are enabled for at-least-once delivery semantics.
/// </summary>
public sealed class RabbitMqPublisher : IMessagePublisher, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _initialized;

    public RabbitMqPublisher(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
    {
        await EnsureInitializedAsync(cancellationToken);
        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message, message.GetType());

            var props = new BasicProperties
            {
                // Persistent delivery mode ensures messages survive broker restarts
                DeliveryMode = DeliveryModes.Persistent
            };

            await _channel!.BasicPublishAsync(
                exchange: "",
                routingKey: "process-statement",
                mandatory: true,
                body: body,
                cancellationToken: cancellationToken);

            // Wait for broker confirmation (publisher confirms)
            // This is the key to at-least-once delivery on the publish side
            bool confirmed = await _channel.WaitForConfirmsAsync(cancellationToken);
            if (!confirmed)
            {
                throw new InvalidOperationException(
                    $"Broker did not confirm message publication for type {typeof(T).Name}");
            }
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var factory = new ConnectionFactory
            {
                HostName = _configuration.GetValue<string>("RabbitMq:Host") ?? "localhost",
                UserName = _configuration.GetValue<string>("RabbitMq:Username") ?? "guest",
                Password = _configuration.GetValue<string>("RabbitMq:Password") ?? "guest",
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                TopologyRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                ContinuationTimeout = TimeSpan.FromSeconds(20)
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Enable publisher confirms on this channel
            await _channel.ConfirmSelectAsync(ct);

            // Declare the main queue (idempotent — no-op if already exists)
            await _channel.QueueDeclareAsync(
                queue: "process-statement",
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: ct);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.CloseAsync();
        if (_connection is not null)
            await _connection.CloseAsync();
    }
}
```

**Key changes:**
- Constructor no longer blocks — connection is created lazily on first `PublishAsync` call
- `ConfirmSelectAsync` enables publisher confirms
- `WaitForConfirmsAsync` waits for the broker to acknowledge the message was persisted
- `AutomaticRecoveryEnabled` + `TopologyRecoveryEnabled` for connection resilience
- `DeliveryMode = DeliveryModes.Persistent` ensures messages survive broker restarts
- `mandatory: true` so the broker returns unroutable messages

---

### Step 2: Update `StatementProcessingBackgroundService.cs` — Declare DLX/DLQ + Retry Resilience

**Full new file structure:**

```csharp
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Statements.WebAPI.Contracts.Messages;
using Statements.WebAPI.Services.Statements;

namespace Statements.WebAPI.Services.Messaging;

/// <summary>
/// Background service that listens to the RabbitMQ "process-statement" queue
/// and delegates processing to <see cref="ProcessStatementConsumer"/>.
///
/// Reliability features:
/// - Dead letter exchange + queue for poison messages (after delivery-limit retries)
/// - Automatic connection recovery with topology recovery
/// - Outer retry loop for total broker outage
/// </summary>
public sealed class StatementProcessingBackgroundService : BackgroundService
{
    private const string MainQueue = "process-statement";
    private const string DeadLetterExchange = "process-statement.dlx";
    private const string DeadLetterQueue = "process-statement.dlq";
    private const string DeadLetterRoutingKey = "process-statement.dlq";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatementProcessingBackgroundService> _logger;

    public StatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<StatementProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer connection lost, reconnecting in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration.GetValue<string>("RabbitMq:Host") ?? "localhost",
            UserName = _configuration.GetValue<string>("RabbitMq:Username") ?? "guest",
            Password = _configuration.GetValue<string>("RabbitMq:Password") ?? "guest",
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            TopologyRecoveryEnabled = true,
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            ContinuationTimeout = TimeSpan.FromSeconds(20)
        };

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare DLX exchange and DLQ (idempotent — managed by code so the DLQ
        // exists even before the RabbitMQ definitions file applies the policy)
        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: "direct",
            durable: true,
            cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: DeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: DeadLetterRoutingKey,
            cancellationToken: stoppingToken);

        // Declare the main queue (idempotent — no-op if already exists)
        // The DLX + delivery-limit is applied via RabbitMQ policy (definitions.json),
        // not queue arguments, so no queue deletion is needed.
        await channel.QueueDeclareAsync(
            queue: MainQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Process one message at a time
        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            ProcessStatementMessage? message = null;
            try
            {
                message = JsonSerializer.Deserialize<ProcessStatementMessage>(ea.Body.Span);
                if (message is null)
                {
                    // Bad message — NACK without requeue, it will be dead-lettered
                    await channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ProcessStatementConsumer>();
                await processor.ConsumeAsync(message, stoppingToken);

                // Success — acknowledge
                await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process statement {StatementId}", message?.StatementId);

                // NACK with requeue=true — the broker's delivery-limit policy (5)
                // will dead-letter the message to process-statement.dlx after
                // the limit is exceeded. This replaces the old infinite retry loop.
                await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);

                // Brief delay to avoid tight requeue loops on transient failures
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        };

        await channel.BasicConsumeAsync(
            MainQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Statement processing background service started, listening on queue '{Queue}' " +
            "with DLQ '{DeadLetterQueue}' and delivery-limit policy",
            MainQueue, DeadLetterQueue);

        // Keep running until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
    }
}
```

**Key changes:**
- Declares DLX exchange + DLQ + binding (idempotent — safe to run every startup)
- Removed `x-queue-type=quorum` and queue-level DLX arguments — instead relies on the RabbitMQ policy (applied via `definitions.json`)
- Consumer still NACKs with `requeue=true` — the **broker** enforces `delivery-limit: 5` and dead-letters automatically after the 5th retry
- Outer `while` loop reconnects on total broker outage (catches exceptions from connection setup)
- Added `AutomaticRecoveryEnabled` + `TopologyRecoveryEnabled` to `ConnectionFactory`

**How the delivery limit + DLQ flow works:**

```
1st NACK with requeue=true → broker requeues (delivery count: 1)
2nd NACK with requeue=true → broker requeues (delivery count: 2)
3rd NACK with requeue=true → broker requeues (delivery count: 3)
4th NACK with requeue=true → broker requeues (delivery count: 4)
5th NACK with requeue=true → broker: delivery-limit exceeded!
    → dead-letters to process-statement.dlx
    → routed to process-statement.dlq
    → sits in DLQ for manual inspection
```

---

### Step 3: Handle `PublishAsync` Failure in `StatementService.cs`

In `UploadAsync` (around line 233 in the original), wrap the publish call:

```csharp
// ---- OLD ----
await _messagePublisher.PublishAsync(new ProcessStatementMessage { ... }, cancellationToken);

// ---- NEW ----
try
{
    await _messagePublisher.PublishAsync(new ProcessStatementMessage
    {
        StatementId = statement.Id,
        StoredFileName = storedFileName,
        UserId = userId,
        BankAccountId = bankAccountId
    }, cancellationToken);

    _logger.LogInformation(
        "Statement {StatementId} uploaded and message published for background processing",
        statement.Id);
}
catch (Exception ex)
{
    _logger.LogError(ex,
        "Failed to publish message for statement {StatementId}. " +
        "Statement remains in 'uploaded' status and can be retried from the UI.",
        statement.Id);
    // Don't throw — the file was saved and the DB record created.
    // The user can retry processing from the statement management UI.
    // The statement stays as 'uploaded' (not 'failed') so the retry endpoint picks it up.
}
```

This prevents a broker outage from causing the entire upload to fail. The user gets their file uploaded, and can retry processing later.

---

### Step 4: Idempotency — Already Handled, Add Comment

In `ProcessStatementConsumer.cs`, the first thing `ConsumeAsync` does is check the statement status. Add a clarifying comment:

```csharp
// Line 43 — add or update comment:
// Idempotency check: at-least-once delivery means we may see duplicates if
// the broker redelivers a message that was already processed (e.g., ACK lost
// due to network issue). Checking status prevents double-processing.
var currentStatus = await _dbExecutor.QuerySingleOrDefaultAsync<string>(...);
```

---

## Architecture Diagram (After Changes)

```
Publisher (RabbitMqPublisher)
  │
  │  [ConfirmSelectAsync + WaitForConfirmsAsync]
  │  [DeliveryMode = Persistent]
  │  [AutomaticRecoveryEnabled]
  │
  ▼
┌───────────────────────────────────────┐
│  Exchange: (default / direct)          │
│  Queue: "process-statement"            │
│                                       │
│  [Policy: statement-processing]        │
│  ├─ delivery-limit: 5                  │
│  ├─ dead-letter-exchange:              │
│  │     process-statement.dlx           │
│  └─ dead-letter-routing-key:           │
│        process-statement.dlq           │
└──────────────────┬────────────────────┘
                   │
             ┌─────┴─────┐
         ≤5 NACKs    >5 NACKs (broker enforces)
             │             │
       Consumer ────→ Dead Letter Exchange
       (requeue)       (process-statement.dlx)
                             │
                        ┌────┘
                        ▼
                   ┌──────────────────┐
                   │ Queue:           │
                   │ process-stmt.dlq │ (durable)
                   └──────────────────┘
                        │
                  Manual inspection /
                  alerting / logging
                        │
              Idempotent consumer
              (SELECT status guard)
```

---

## Migration Path (Zero Downtime)

Since we're using **policies** instead of queue arguments, there's **no need to delete the existing queue**. The migration is:

1. **Add `rabbitmq/definitions.json`** to the repo
2. **Update `docker-compose.yml`** to mount the definitions file
3. **Deploy code changes** (updated publisher + consumer)
4. **Restart the RabbitMQ container** (or just apply the policy at runtime via `rabbitmqctl`)
5. **Restart the backend** container

The existing `process-statement` queue is untouched. The policy dynamically applies to it. The DLX + DLQ are created fresh. Messages that fail more than 5 times start landing in the DLQ automatically.

---

## Testing Plan

| Test | Description | Category |
|---|---|---|
| **Publisher confirm success** | Mock `WaitForConfirmsAsync` returning true — verify message is published and confirmed | Unit |
| **Publisher confirm failure** | Mock `WaitForConfirmsAsync` returning false — verify exception thrown | Unit |
| **Publisher connection recovery** | Kill RabbitMQ container, restart it, verify publisher reconnects and publishes successfully | Integration |
| **DLQ routing** | Publish a poison message (e.g., a non-existent file reference that always fails). Verify it retries up to 5 times then lands in the DLQ. | Integration |
| **Idempotency** | Publish the same `ProcessStatementMessage` twice. Verify only one set of transactions is inserted (status guard). | Integration |
| **Graceful publish failure** | Stop RabbitMQ, upload a file. Verify the statement is saved as `'uploaded'` and the upload API returns success. | Integration |
