using SphereBackend.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace SphereBackend.Services
{
    public interface IBoardEventService
    {
        Task SubscribeAsync(int boardId, int userId, DateTime accessGrantedAtUtc, HttpContext context, CancellationToken ct);
        Task NotifyBoardAsync(int boardId, string action, object? data = null);
        Task DisconnectUserAsync(int boardId, int userId);
        Task DisconnectBoardAsync(int boardId);
    }

    public class BoardEventService : IBoardEventService, IHostedService
    {
        private const int SubscriberQueueCapacity = 256;
        private const int ReplayBufferCapacity = 512;
        private const int DistributedQueueCapacity = 2048;
        private const int MaxConnectionsPerUserPerBoard = 8;
        private const string DistributedChannelName = "board_events";
        private const int MaxNotifyPayloadBytes = 7900;
        private const string EnvelopeKindEvent = "event";
        private const string EnvelopeKindDisconnectUser = "disconnect_user";
        private const string EnvelopeKindDisconnectBoard = "disconnect_board";
        private const int ReplayPruneEveryNEvents = 256;

        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan DistributedReconnectDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReplayPruneInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ReplayBufferTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan CriticalDistributedPublishTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan AccessRecheckInterval = TimeSpan.FromMinutes(1);

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions DistributedDeserializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, SubscriberConnection>> _subscribers = new();
        private readonly ConcurrentDictionary<int, object> _boardConnectionGates = new();
        private readonly ConcurrentDictionary<int, ReplayBuffer> _replayBuffers = new();
        private readonly Channel<DistributedEnvelope> _distributedOutbound = Channel.CreateBounded<DistributedEnvelope>(
            new BoundedChannelOptions(DistributedQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly bool _enableDistributedRelay;
        private readonly string? _connectionString;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BoardEventService> _logger;

        private long _eventCounter;
        private long _replayPruneCounter;
        private CancellationTokenSource? _serviceCts;
        private Task? _publisherTask;
        private Task? _listenerTask;
        private Task? _replayPruneTask;
        private Task? _accessRecheckTask;

        public BoardEventService(
            IConfiguration configuration,
            ILogger<BoardEventService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _enableDistributedRelay = configuration.GetValue<bool>("Sse:EnableDistributedRelay");
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _scopeFactory = scopeFactory;
        }

        private bool IsDistributedEnabled => _enableDistributedRelay && !string.IsNullOrWhiteSpace(_connectionString);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _serviceCts.Token;
            _replayPruneTask = RunReplayPruneLoopAsync(ct);
            _accessRecheckTask = RunAccessRecheckLoopAsync(ct);

            if (!_enableDistributedRelay)
            {
                _logger.LogInformation("SSE distributed relay is disabled by configuration.");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _logger.LogWarning("SSE distributed relay is disabled because DefaultConnection is missing.");
                return Task.CompletedTask;
            }

            _publisherTask = RunDistributedPublisherAsync(ct);
            _listenerTask = RunDistributedListenerAsync(ct);

            _logger.LogInformation(
                "SSE distributed relay started on postgres channel '{Channel}' with instance id '{InstanceId}'.",
                DistributedChannelName,
                _instanceId);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _distributedOutbound.Writer.TryComplete();

            if (_serviceCts == null)
            {
                return;
            }

            _serviceCts.Cancel();

            var tasks = new[] { _publisherTask, _listenerTask, _replayPruneTask, _accessRecheckTask }
                .Where(t => t != null)
                .Cast<Task>()
                .ToArray();

            if (tasks.Length == 0)
            {
                return;
            }

            var stopTask = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(stopTask, Task.Delay(Timeout.Infinite, cancellationToken));
            if (completed != stopTask)
            {
                _logger.LogWarning("Timed out while stopping SSE distributed relay.");
                return;
            }

            await stopTask;
        }

        public async Task SubscribeAsync(int boardId, int userId, DateTime accessGrantedAtUtc, HttpContext context, CancellationToken ct)
        {
            var lastEventId = context.Request.Headers["Last-Event-ID"].ToString();
            var hasReplayCursor = string.IsNullOrWhiteSpace(lastEventId);
            var replayMessages = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(lastEventId))
            {
                replayMessages = GetReplayMessages(boardId, lastEventId, accessGrantedAtUtc, out hasReplayCursor);
            }

            var connectionId = Guid.NewGuid();
            var connection = new SubscriberConnection(userId);
            var boardConnections = _subscribers.GetOrAdd(boardId, _ => new ConcurrentDictionary<Guid, SubscriberConnection>());
            var boardGate = _boardConnectionGates.GetOrAdd(boardId, _ => new object());
            var limitExceeded = false;

            lock (boardGate)
            {
                var activeConnectionsForUser = boardConnections.Values.Count(c => c.UserId == userId);
                if (activeConnectionsForUser >= MaxConnectionsPerUserPerBoard)
                {
                    limitExceeded = true;
                }
                else
                {
                    boardConnections[connectionId] = connection;
                }
            }

            if (limitExceeded)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["Connection"] = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, context.RequestAborted);
            var linkedToken = linkedCts.Token;
            var heartbeatTask = RunHeartbeatAsync(boardId, connectionId, connection, linkedToken);
            var tokenExpiryTask = RunTokenExpiryDisconnectAsync(boardId, connectionId, connection, context.User, linkedToken);

            try
            {
                await context.Response.StartAsync(linkedToken);

                EnqueueWithOverflowHandling(boardId, connectionId, connection, BuildControlMessage("CONNECTED", null));
                if (!hasReplayCursor)
                {
                    EnqueueWithOverflowHandling(
                        boardId,
                        connectionId,
                        connection,
                        BuildControlMessage("RESYNC_REQUIRED", new { reason = "last_event_id_not_found" }));
                }
                else
                {
                    foreach (var replayPayload in replayMessages)
                    {
                        if (!EnqueueWithOverflowHandling(boardId, connectionId, connection, replayPayload))
                        {
                            break;
                        }
                    }
                }

                await using var writer = new StreamWriter(context.Response.Body, new UTF8Encoding(false), 1024, leaveOpen: true);
                await foreach (var payload in connection.Outbound.Reader.ReadAllAsync(linkedToken))
                {
                    await writer.WriteAsync(payload);
                    await writer.FlushAsync();
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SSE stream failed for board {BoardId}, user {UserId}, connection {ConnectionId}.",
                    boardId,
                    userId,
                    connectionId);
            }
            finally
            {
                linkedCts.Cancel();
                await heartbeatTask;
                await tokenExpiryTask;
                RemoveConnection(boardId, connectionId, connection);
            }
        }

        public async Task NotifyBoardAsync(int boardId, string action, object? data = null)
        {
            var eventId = CreateEventId();
            var payload = BuildEventMessage(eventId, action, data);
            BufferEvent(boardId, eventId, payload);
            DispatchLocal(boardId, payload);

            if (!IsDistributedEnabled)
            {
                return;
            }

            var envelope = new DistributedEnvelope
            {
                InstanceId = _instanceId,
                Kind = EnvelopeKindEvent,
                BoardId = boardId,
                EventId = eventId,
                Action = action,
                Data = data is null ? null : JsonSerializer.SerializeToElement(data, SerializerOptions)
            };

            await PublishDistributedWithFallbackAsync(envelope);
            return;
        }

        public async Task DisconnectUserAsync(int boardId, int userId)
        {
            DisconnectUserLocal(boardId, userId);

            if (!IsDistributedEnabled)
            {
                return;
            }

            var envelope = new DistributedEnvelope
            {
                InstanceId = _instanceId,
                Kind = EnvelopeKindDisconnectUser,
                BoardId = boardId,
                UserId = userId
            };

            await PublishDistributedWithFallbackAsync(envelope);
        }

        public async Task DisconnectBoardAsync(int boardId)
        {
            DisconnectBoardLocal(boardId);

            if (!IsDistributedEnabled)
            {
                return;
            }

            var envelope = new DistributedEnvelope
            {
                InstanceId = _instanceId,
                Kind = EnvelopeKindDisconnectBoard,
                BoardId = boardId
            };

            await PublishDistributedWithFallbackAsync(envelope);
        }

        private void DisconnectUserLocal(int boardId, int userId)
        {
            if (!_subscribers.TryGetValue(boardId, out var boardConnections) || boardConnections.IsEmpty)
            {
                return;
            }

            foreach (var entry in boardConnections.Where(x => x.Value.UserId == userId).ToArray())
            {
                RemoveConnection(boardId, entry.Key, entry.Value);
            }
        }

        private void DisconnectBoardLocal(int boardId)
        {
            if (!_subscribers.TryRemove(boardId, out var boardConnections) || boardConnections.IsEmpty)
            {
                _boardConnectionGates.TryRemove(boardId, out _);
                return;
            }

            foreach (var connection in boardConnections.Values)
            {
                connection.Outbound.Writer.TryComplete();
            }

            _boardConnectionGates.TryRemove(boardId, out _);
        }

        private async Task RunHeartbeatAsync(int boardId, Guid connectionId, SubscriberConnection connection, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, ct);
                    if (!EnqueueWithOverflowHandling(boardId, connectionId, connection, ": keepalive\n\n"))
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
        }

        private async Task RunTokenExpiryDisconnectAsync(
            int boardId,
            Guid connectionId,
            SubscriberConnection connection,
            ClaimsPrincipal user,
            CancellationToken ct)
        {
            var expClaim = user.FindFirst("exp")?.Value;
            if (!long.TryParse(expClaim, out var unixExp))
            {
                return;
            }

            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixExp).UtcDateTime;
            var delay = expiresAtUtc - DateTime.UtcNow;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            try
            {
                await Task.Delay(delay, ct);
                _logger.LogInformation(
                    "Disconnecting SSE connection {ConnectionId} for board {BoardId} because JWT access token expired.",
                    connectionId,
                    boardId);
                RemoveConnection(boardId, connectionId, connection);
            }
            catch (OperationCanceledException)
            {

            }
        }

        private void DispatchLocal(int boardId, string payload)
        {
            if (!_subscribers.TryGetValue(boardId, out var boardConnections) || boardConnections.IsEmpty)
            {
                return;
            }

            foreach (var entry in boardConnections)
            {
                EnqueueWithOverflowHandling(boardId, entry.Key, entry.Value, payload);
            }
        }

        private void RemoveConnection(int boardId, Guid connectionId, SubscriberConnection connection)
        {
            if (_subscribers.TryGetValue(boardId, out var boardConnections) &&
                boardConnections.TryRemove(connectionId, out var removed))
            {
                removed.Outbound.Writer.TryComplete();
                if (boardConnections.IsEmpty)
                {
                    _subscribers.TryRemove(boardId, out _);
                    _boardConnectionGates.TryRemove(boardId, out _);
                }

                return;
            }

            connection.Outbound.Writer.TryComplete();
        }

        private bool EnqueueWithOverflowHandling(int boardId, Guid connectionId, SubscriberConnection connection, string payload)
        {
            if (connection.Outbound.Writer.TryWrite(payload))
            {
                return true;
            }

            _logger.LogWarning(
                "Disconnecting slow SSE client {ConnectionId} for board {BoardId} because outbound queue is full.",
                connectionId,
                boardId);
            RemoveConnection(boardId, connectionId, connection);
            return false;
        }

        private bool TryEnqueueDistributed(DistributedEnvelope envelope)
        {
            if (_distributedOutbound.Writer.TryWrite(envelope))
            {
                return true;
            }

            _logger.LogWarning(
                "Distributed SSE enqueue rejected for {Kind} on board {BoardId} because relay queue is full.",
                envelope.Kind,
                envelope.BoardId);
            return false;
        }

        private async Task PublishDistributedWithFallbackAsync(DistributedEnvelope envelope)
        {
            if (TryEnqueueDistributed(envelope))
            {
                return;
            }

            _logger.LogWarning(
                "Publishing envelope {Kind} for board {BoardId} directly because relay queue is full.",
                envelope.Kind,
                envelope.BoardId);

            using var cts = new CancellationTokenSource(CriticalDistributedPublishTimeout);
            try
            {
                await PublishDistributedEnvelopeDirectAsync(envelope, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogError(
                    "Timed out publishing envelope {Kind} for board {BoardId} via direct fallback.",
                    envelope.Kind,
                    envelope.BoardId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish envelope {Kind} for board {BoardId} via direct fallback.",
                    envelope.Kind,
                    envelope.BoardId);
            }
        }

        private async Task PublishDistributedEnvelopeDirectAsync(DistributedEnvelope envelope, CancellationToken ct)
        {
            if (!TryBuildDistributedPayload(envelope, out var payload))
            {
                return;
            }

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new NpgsqlCommand(
                $"SELECT pg_notify('{DistributedChannelName}', @payload);",
                connection);
            command.Parameters.AddWithValue("payload", payload);
            await command.ExecuteNonQueryAsync(ct);
        }

        private bool TryBuildDistributedPayload(DistributedEnvelope envelope, out string payload)
        {
            payload = JsonSerializer.Serialize(envelope, SerializerOptions);
            if (Encoding.UTF8.GetByteCount(payload) <= MaxNotifyPayloadBytes)
            {
                return true;
            }

            if (envelope.Kind == EnvelopeKindEvent)
            {
                var resyncEnvelope = CreateResyncEnvelope(envelope.BoardId, envelope.Action);
                payload = JsonSerializer.Serialize(resyncEnvelope, SerializerOptions);
                _logger.LogWarning(
                    "Distributed SSE payload for {Action} on board {BoardId} exceeded PostgreSQL NOTIFY limit. Sending RESYNC_REQUIRED instead.",
                    envelope.Action,
                    envelope.BoardId);
            }

            if (Encoding.UTF8.GetByteCount(payload) <= MaxNotifyPayloadBytes)
            {
                return true;
            }

            _logger.LogWarning(
                "Skipping distributed SSE envelope {Kind} for board {BoardId} because payload exceeds PostgreSQL NOTIFY limit.",
                envelope.Kind,
                envelope.BoardId);
            payload = string.Empty;
            return false;
        }

        private async Task RunDistributedPublisherAsync(CancellationToken ct)
        {
            DistributedEnvelope? pendingEnvelope = null;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(_connectionString);
                    await connection.OpenAsync(ct);

                    while (!ct.IsCancellationRequested)
                    {
                        var outboundEnvelope = pendingEnvelope ?? await _distributedOutbound.Reader.ReadAsync(ct);
                        pendingEnvelope = null;

                        if (!TryBuildDistributedPayload(outboundEnvelope, out var payload))
                        {
                            continue;
                        }

                        try
                        {
                            await using var command = new NpgsqlCommand(
                                $"SELECT pg_notify('{DistributedChannelName}', @payload);",
                                connection);
                            command.Parameters.AddWithValue("payload", payload);
                            await command.ExecuteNonQueryAsync(ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            pendingEnvelope = outboundEnvelope;
                            throw;
                        }
                    }

                    return;
                }
                catch (ChannelClosedException)
                {
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Distributed SSE publisher failed. Reconnecting... pending envelope kind {Kind}, board {BoardId}.",
                        pendingEnvelope?.Kind,
                        pendingEnvelope?.BoardId);
                    await SafeDelayAfterFailure(ct);
                }
            }
        }

        private async Task RunDistributedListenerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(_connectionString);
                    connection.Notification += (_, args) => HandleDistributedPayload(args.Payload);
                    await connection.OpenAsync(ct);

                    await using var command = new NpgsqlCommand($"LISTEN {DistributedChannelName};", connection);
                    await command.ExecuteNonQueryAsync(ct);

                    while (!ct.IsCancellationRequested)
                    {
                        await connection.WaitAsync(ct);
                    }

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Distributed SSE listener failed. Reconnecting...");
                    await SafeDelayAfterFailure(ct);
                }
            }
        }

        private void HandleDistributedPayload(string payload)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<DistributedEnvelope>(payload, DistributedDeserializerOptions);
                if (envelope == null || envelope.InstanceId == _instanceId)
                {
                    return;
                }

                if (string.Equals(envelope.Kind, EnvelopeKindDisconnectUser, StringComparison.Ordinal))
                {
                    if (envelope.UserId.HasValue)
                    {
                        DisconnectUserLocal(envelope.BoardId, envelope.UserId.Value);
                    }
                    return;
                }

                if (string.Equals(envelope.Kind, EnvelopeKindDisconnectBoard, StringComparison.Ordinal))
                {
                    DisconnectBoardLocal(envelope.BoardId);
                    return;
                }

                if (!string.Equals(envelope.Kind, EnvelopeKindEvent, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Unknown distributed SSE envelope kind '{Kind}'.", envelope.Kind);
                    return;
                }

                var eventId = string.IsNullOrWhiteSpace(envelope.EventId)
                    ? CreateEventId()
                    : envelope.EventId;
                var message = BuildEventMessage(eventId, envelope.Action, envelope.Data);

                BufferEvent(envelope.BoardId, eventId, message);
                DispatchLocal(envelope.BoardId, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse distributed SSE payload.");
            }
        }

        private void BufferEvent(int boardId, string eventId, string payload)
        {
            var replayBuffer = _replayBuffers.GetOrAdd(boardId, _ => new ReplayBuffer());
            lock (replayBuffer.SyncRoot)
            {
                TouchReplayBuffer(replayBuffer);
                replayBuffer.Events.Enqueue(new ReplayEvent(eventId, payload, DateTime.UtcNow));
                while (replayBuffer.Events.Count > ReplayBufferCapacity)
                {
                    replayBuffer.Events.Dequeue();
                }
            }

            MaybePruneReplayBuffers();
        }

        private string[] GetReplayMessages(int boardId, string lastEventId, DateTime accessGrantedAtUtc, out bool cursorFound)
        {
            cursorFound = false;

            if (!_replayBuffers.TryGetValue(boardId, out var replayBuffer))
            {
                return Array.Empty<string>();
            }

            lock (replayBuffer.SyncRoot)
            {
                TouchReplayBuffer(replayBuffer);
                if (replayBuffer.Events.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var snapshot = replayBuffer.Events
                    .Where(x => x.CreatedAtUtc >= accessGrantedAtUtc)
                    .ToArray();

                var cursorIndex = Array.FindIndex(snapshot, x => string.Equals(x.EventId, lastEventId, StringComparison.Ordinal));
                if (cursorIndex < 0)
                {
                    return Array.Empty<string>();
                }

                cursorFound = true;
                return snapshot.Skip(cursorIndex + 1).Select(x => x.Payload).ToArray();
            }
        }

        private static void TouchReplayBuffer(ReplayBuffer replayBuffer)
        {
            Volatile.Write(ref replayBuffer.LastTouchedTicks, DateTime.UtcNow.Ticks);
        }

        private void MaybePruneReplayBuffers()
        {
            var counter = Interlocked.Increment(ref _replayPruneCounter);
            if (counter % ReplayPruneEveryNEvents != 0)
            {
                return;
            }

            PruneReplayBuffers();
        }

        private void PruneReplayBuffers()
        {
            var cutoffTicks = DateTime.UtcNow.Subtract(ReplayBufferTtl).Ticks;
            foreach (var entry in _replayBuffers)
            {
                if (_subscribers.ContainsKey(entry.Key))
                {
                    continue;
                }

                var lastTouchedTicks = Volatile.Read(ref entry.Value.LastTouchedTicks);
                if (lastTouchedTicks >= cutoffTicks)
                {
                    continue;
                }

                _replayBuffers.TryRemove(entry.Key, out _);
            }
        }

        private async Task RunReplayPruneLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(ReplayPruneInterval, ct);
                    PruneReplayBuffers();
                }
            }
            catch (OperationCanceledException)
            {

            }
        }

        private async Task RunAccessRecheckLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(AccessRecheckInterval, ct);
                    await RevalidateSubscriberAccessAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Periodic SSE access recheck failed.");
                    await SafeDelayAfterFailure(ct);
                }
            }
        }

        private async Task RevalidateSubscriberAccessAsync(CancellationToken ct)
        {
            var snapshots = SnapshotSubscribersForAccessCheck();
            if (snapshots.Count == 0)
            {
                return;
            }

            var boardIds = snapshots.Select(x => x.BoardId).Distinct().ToArray();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var boardOwners = await db.Boards
                .AsNoTracking()
                .Where(b => boardIds.Contains(b.Id))
                .Select(b => new { b.Id, b.UserId })
                .ToListAsync(ct);

            var acceptedMembers = await db.BoardMembers
                .AsNoTracking()
                .Where(m => boardIds.Contains(m.BoardId) && m.IsAccepted)
                .Select(m => new { m.BoardId, m.UserId })
                .ToListAsync(ct);

            var allowedUsersByBoard = boardOwners.ToDictionary(
                x => x.Id,
                x => new HashSet<int> { x.UserId });

            foreach (var member in acceptedMembers)
            {
                if (!allowedUsersByBoard.TryGetValue(member.BoardId, out var boardAllowedUsers))
                {
                    boardAllowedUsers = new HashSet<int>();
                    allowedUsersByBoard[member.BoardId] = boardAllowedUsers;
                }

                boardAllowedUsers.Add(member.UserId);
            }

            foreach (var snapshot in snapshots)
            {
                ct.ThrowIfCancellationRequested();

                if (!allowedUsersByBoard.TryGetValue(snapshot.BoardId, out var allowedUsers))
                {
                    foreach (var userId in snapshot.UserIds)
                    {
                        _logger.LogInformation(
                            "Disconnecting SSE user {UserId} from board {BoardId} because board access record was not found during periodic recheck.",
                            userId,
                            snapshot.BoardId);
                        DisconnectUserLocal(snapshot.BoardId, userId);
                    }

                    continue;
                }

                foreach (var userId in snapshot.UserIds)
                {
                    if (allowedUsers.Contains(userId))
                    {
                        continue;
                    }

                    _logger.LogInformation(
                        "Disconnecting SSE user {UserId} from board {BoardId} because access is no longer valid.",
                        userId,
                        snapshot.BoardId);
                    DisconnectUserLocal(snapshot.BoardId, userId);
                }
            }
        }

        private List<AccessCheckSnapshot> SnapshotSubscribersForAccessCheck()
        {
            var snapshots = new List<AccessCheckSnapshot>();
            foreach (var boardEntry in _subscribers)
            {
                if (boardEntry.Value.IsEmpty)
                {
                    continue;
                }

                var userIds = boardEntry.Value.Values
                    .Select(x => x.UserId)
                    .Distinct()
                    .ToArray();

                if (userIds.Length == 0)
                {
                    continue;
                }

                snapshots.Add(new AccessCheckSnapshot(boardEntry.Key, userIds));
            }

            return snapshots;
        }

        private DistributedEnvelope CreateResyncEnvelope(int boardId, string sourceAction)
        {
            return new DistributedEnvelope
            {
                InstanceId = _instanceId,
                Kind = EnvelopeKindEvent,
                BoardId = boardId,
                EventId = CreateEventId(),
                Action = "RESYNC_REQUIRED",
                Data = JsonSerializer.SerializeToElement(new
                {
                    reason = "payload_too_large",
                    sourceAction
                }, SerializerOptions)
            };
        }

        private string CreateEventId()
        {
            return $"{_instanceId}:{Interlocked.Increment(ref _eventCounter)}";
        }

        private static string BuildEventMessage(string eventId, string action, object? data)
        {
            var json = JsonSerializer.Serialize(new { action, data }, SerializerOptions);
            return $"id: {eventId}\ndata: {json}\n\n";
        }

        private static string BuildControlMessage(string action, object? data)
        {
            var json = JsonSerializer.Serialize(new { action, data }, SerializerOptions);
            return $"data: {json}\n\n";
        }

        private static async Task SafeDelayAfterFailure(CancellationToken ct)
        {
            try
            {
                await Task.Delay(DistributedReconnectDelay, ct);
            }
            catch (OperationCanceledException)
            {

            }
        }

        private sealed class SubscriberConnection
        {
            public SubscriberConnection(int userId)
            {
                UserId = userId;
                Outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(SubscriberQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            }

            public int UserId { get; }
            public Channel<string> Outbound { get; }
        }

        private sealed class DistributedEnvelope
        {
            public string InstanceId { get; set; } = string.Empty;
            public string Kind { get; set; } = EnvelopeKindEvent;
            public int BoardId { get; set; }
            public int? UserId { get; set; }
            public string EventId { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public JsonElement? Data { get; set; }
        }

        private sealed class ReplayBuffer
        {
            public object SyncRoot { get; } = new();
            public Queue<ReplayEvent> Events { get; } = new();
            public long LastTouchedTicks = DateTime.UtcNow.Ticks;
        }

        private sealed class ReplayEvent
        {
            public ReplayEvent(string eventId, string payload, DateTime createdAtUtc)
            {
                EventId = eventId;
                Payload = payload;
                CreatedAtUtc = createdAtUtc;
            }

            public string EventId { get; }
            public string Payload { get; }
            public DateTime CreatedAtUtc { get; }
        }

        private sealed class AccessCheckSnapshot
        {
            public AccessCheckSnapshot(int boardId, int[] userIds)
            {
                BoardId = boardId;
                UserIds = userIds;
            }

            public int BoardId { get; }
            public int[] UserIds { get; }
        }
    }
}
