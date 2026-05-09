using SphereBackend.Data;
using SphereBackend.Features.Cards;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SphereBackend.IntegrationTests;

public sealed class OrderingAndRestoreEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrderingAndRestoreEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReorderCards_MovesCardBetweenColumns_AndReassignsOrders()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardWithTwoActiveColumns(db);

            db.Cards.AddRange(
                new Card
                {
                    Id = 1000,
                    Title = "Source first",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = 100,
                    Order = 0,
                    CreatedById = 1,
                    UpdatedById = 1
                },
                new Card
                {
                    Id = 1001,
                    Title = "Move me",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = 100,
                    Order = 1,
                    CreatedById = 1,
                    UpdatedById = 1
                },
                new Card
                {
                    Id = 2000,
                    Title = "Target existing",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = 200,
                    Order = 0,
                    CreatedById = 1,
                    UpdatedById = 1
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var request = new
        {
            targetColumnId = EncodeId(200),
            cards = new[]
            {
                new { id = EncodeId(1001), order = 0 },
                new { id = EncodeId(2000), order = 1 }
            }
        };

        var response = await client.PutAsJsonAsync("/api/cards/reorder", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cards = await db.Cards
            .AsNoTracking()
            .Where(c => c.BoardId == 10)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var sourceFirst = cards.Single(c => c.Id == 1000);
        var moved = cards.Single(c => c.Id == 1001);
        var targetExisting = cards.Single(c => c.Id == 2000);

        Assert.Equal(100, sourceFirst.ColumnId);
        Assert.Equal(0, sourceFirst.Order);
        Assert.Equal(200, moved.ColumnId);
        Assert.Equal(0, moved.Order);
        Assert.Equal(200, targetExisting.ColumnId);
        Assert.Equal(1, targetExisting.Order);
    }

    [Fact]
    public async Task ReorderColumns_ReordersActiveColumnsWithinBoard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardWithOwnerMembership(db);

            db.Columns.AddRange(
                new Column { Id = 100, Title = "First", BoardId = 10, Order = 0 },
                new Column { Id = 200, Title = "Second", BoardId = 10, Order = 1 },
                new Column { Id = 300, Title = "Third", BoardId = 10, Order = 2 });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var request = new
        {
            boardId = EncodeId(10),
            columns = new[]
            {
                new { id = EncodeId(300), order = 0 },
                new { id = EncodeId(100), order = 1 }
            }
        };

        var response = await client.PutAsJsonAsync("/api/columns/reorder", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columns = await db.Columns
            .AsNoTracking()
            .Where(c => c.BoardId == 10)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Order })
            .ToListAsync();

        Assert.Equal(new[] { 300, 100, 200 }, columns.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, columns.Select(c => c.Order));
    }

    [Fact]
    public async Task ReorderColumns_UsesPayloadOrderValues_NotArrayPosition()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardWithOwnerMembership(db);

            db.Columns.AddRange(
                new Column { Id = 100, Title = "First", BoardId = 10, Order = 0 },
                new Column { Id = 200, Title = "Second", BoardId = 10, Order = 1 },
                new Column { Id = 300, Title = "Third", BoardId = 10, Order = 2 });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var request = new
        {
            boardId = EncodeId(10),
            columns = new[]
            {
                new { id = EncodeId(100), order = 1 },
                new { id = EncodeId(300), order = 0 }
            }
        };

        var response = await client.PutAsJsonAsync("/api/columns/reorder", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var columns = await db.Columns
            .AsNoTracking()
            .Where(c => c.BoardId == 10)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Order })
            .ToListAsync();

        Assert.Equal(new[] { 300, 100, 200 }, columns.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, columns.Select(c => c.Order));
    }

    [Fact]
    public async Task RestoreCard_WhenOriginalColumnIsArchived_FallsBackToActiveColumn()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardWithOwnerMembership(db);

            db.Columns.AddRange(
                new Column
                {
                    Id = 100,
                    Title = "Archived original",
                    BoardId = 10,
                    Order = 0,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc)
                },
                new Column
                {
                    Id = 200,
                    Title = "Fallback active",
                    BoardId = 10,
                    Order = 1
                });

            db.Cards.AddRange(
                new Card
                {
                    Id = 1000,
                    Title = "Restore me",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = null,
                    PreviousColumnId = 100,
                    Order = 0,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 5, 0, DateTimeKind.Utc),
                    ArchivedManually = true,
                    CreatedById = 1,
                    UpdatedById = 1
                },
                new Card
                {
                    Id = 2000,
                    Title = "Already active",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = 200,
                    Order = 0,
                    CreatedById = 1,
                    UpdatedById = 1
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.PostAsync($"/api/cards/{EncodeId(1000)}/restore", content: null);
        var body = await response.Content.ReadFromJsonAsync<RestoreCardResponseDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("original_archived", body!.RestoreContext);
        Assert.NotNull(body.Card);
        Assert.NotNull(body.TargetColumn);
        Assert.Equal(EncodeId(200), body.Card!.ColumnId);
        Assert.Equal(EncodeId(200), body.TargetColumn!.Id);
        Assert.Equal(new[] { EncodeId(1000), EncodeId(2000) }, body.TargetColumn.Cards.Select(c => c.Id));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == 1000);

        Assert.Equal(200, card.ColumnId);
        Assert.Null(card.PreviousColumnId);
        Assert.Null(card.ArchivedAt);
        Assert.False(card.ArchivedManually);
    }

    [Fact]
    public async Task RestoreColumn_RestoresColumn_AndAutoArchivedCardsOnly()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardWithOwnerMembership(db);

            db.Columns.AddRange(
                new Column
                {
                    Id = 100,
                    Title = "Archived column",
                    BoardId = 10,
                    Order = 1,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 0, 0, DateTimeKind.Utc)
                },
                new Column
                {
                    Id = 200,
                    Title = "Active column",
                    BoardId = 10,
                    Order = 0
                });

            db.Cards.AddRange(
                new Card
                {
                    Id = 1000,
                    Title = "Auto archived first",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = null,
                    PreviousColumnId = 100,
                    Order = 5,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 5, 0, DateTimeKind.Utc),
                    ArchivedManually = false,
                    CreatedById = 1,
                    UpdatedById = 1
                },
                new Card
                {
                    Id = 1001,
                    Title = "Auto archived second",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = null,
                    PreviousColumnId = 100,
                    Order = 9,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 6, 0, DateTimeKind.Utc),
                    ArchivedManually = false,
                    CreatedById = 1,
                    UpdatedById = 1
                },
                new Card
                {
                    Id = 1002,
                    Title = "Manual archived should stay hidden",
                    Content = string.Empty,
                    BoardId = 10,
                    ColumnId = null,
                    PreviousColumnId = 100,
                    Order = 3,
                    ArchivedAt = new DateTime(2026, 4, 23, 9, 7, 0, DateTimeKind.Utc),
                    ArchivedManually = true,
                    CreatedById = 1,
                    UpdatedById = 1
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.PostAsync($"/api/columns/{EncodeId(100)}/restore", content: null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        var restoredColumn = body.EnumerateArray()
            .Single(column => column.GetProperty("id").GetString() == EncodeId(100));
        var restoredCards = restoredColumn.GetProperty("cards").EnumerateArray().ToList();

        Assert.Equal(new[] { EncodeId(1000), EncodeId(1001) }, restoredCards.Select(card => card.GetProperty("id").GetString()));
        Assert.Equal(new[] { 0, 1 }, restoredCards.Select(card => card.GetProperty("order").GetInt32()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var restoredColumnEntity = await db.Columns.AsNoTracking().FirstAsync(c => c.Id == 100);
        var cards = await db.Cards
            .AsNoTracking()
            .Where(c => c.PreviousColumnId == 100 || c.ColumnId == 100 || c.Id == 1002)
            .OrderBy(c => c.Id)
            .ToListAsync();

        Assert.Null(restoredColumnEntity.ArchivedAt);

        var autoFirst = cards.Single(c => c.Id == 1000);
        var autoSecond = cards.Single(c => c.Id == 1001);
        var manualArchived = cards.Single(c => c.Id == 1002);

        Assert.Equal(100, autoFirst.ColumnId);
        Assert.Equal(0, autoFirst.Order);
        Assert.Null(autoFirst.ArchivedAt);
        Assert.Equal(100, autoSecond.ColumnId);
        Assert.Equal(1, autoSecond.Order);
        Assert.Null(autoSecond.ArchivedAt);
        Assert.Null(manualArchived.ColumnId);
        Assert.Equal(100, manualArchived.PreviousColumnId);
        Assert.NotNull(manualArchived.ArchivedAt);
        Assert.True(manualArchived.ArchivedManually);
    }

    private string EncodeId(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var idHasher = scope.ServiceProvider.GetRequiredService<IIdHasher>();
        return idHasher.Encode(id);
    }

    private static void SeedBoardWithTwoActiveColumns(AppDbContext db)
    {
        SeedBoardWithOwnerMembership(db);

        db.Columns.AddRange(
            new Column { Id = 100, Title = "Source", BoardId = 10, Order = 0 },
            new Column { Id = 200, Title = "Target", BoardId = 10, Order = 1 });
    }

    private static void SeedBoardWithOwnerMembership(AppDbContext db)
    {
        db.Users.Add(CreateUser(1, "owner"));

        db.Boards.Add(new Board
        {
            Id = 10,
            Title = "Roadmap",
            UserId = 1
        });

        db.BoardMembers.Add(new BoardMember
        {
            UserId = 1,
            BoardId = 10,
            IsAccepted = true,
            Order = 0,
            DateAdded = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc)
        });
    }

    private static User CreateUser(int id, string username)
    {
        return new User
        {
            Id = id,
            Username = username,
            Email = $"{username}@example.com",
            NormalizedUsername = username,
            NormalizedEmail = $"{username}@example.com",
            PasswordHash = "hash",
            IsEmailConfirmed = true
        };
    }
}
