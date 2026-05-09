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

public sealed class CardsAndMembersEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CardsAndMembersEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCard_WithAuthenticatedOwner_ReturnsCard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardCardGraph(db);
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.GetAsync($"/api/cards/{EncodeId(1000)}");
        var body = await response.Content.ReadFromJsonAsync<CardDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(EncodeId(1000), body!.Id);
        Assert.Equal("Initial card", body.Title);
        Assert.Equal("Initial content", body.Content);
        Assert.Equal(EncodeId(100), body.ColumnId);
        Assert.Equal(EncodeId(10), body.BoardId);
    }

    [Fact]
    public async Task GetCard_WithAcceptedMember_ReturnsCard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardCardGraph(db);
            db.Users.Add(CreateUser(2, "member"));
            db.BoardMembers.Add(new BoardMember
            {
                UserId = 2,
                BoardId = 10,
                IsAccepted = true,
                Order = 1,
                DateAdded = new DateTime(2026, 4, 23, 10, 5, 0, DateTimeKind.Utc)
            });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 2);
        var response = await client.GetAsync($"/api/cards/{EncodeId(1000)}");
        var body = await response.Content.ReadFromJsonAsync<CardDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(EncodeId(1000), body!.Id);
        Assert.Equal(EncodeId(10), body.BoardId);
    }

    [Fact]
    public async Task UpdateCard_WithRegularFields_UpdatesCard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardCardGraph(db);
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var request = new
        {
            title = "Updated card",
            content = "Updated content",
            priority = "high",
            startAt = new DateOnly(2026, 4, 24),
            dueAt = new DateOnly(2026, 4, 30)
        };

        var response = await client.PatchAsync($"/api/cards/{EncodeId(1000)}", JsonContent.Create(request));
        var body = await response.Content.ReadFromJsonAsync<CardDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("Updated card", body!.Title);
        Assert.Equal("Updated content", body.Content);
        Assert.Equal("high", body.Priority);
        Assert.Equal(new DateOnly(2026, 4, 24), body.StartAt);
        Assert.Equal(new DateOnly(2026, 4, 30), body.DueAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == 1000);

        Assert.Equal("Updated card", card.Title);
        Assert.Equal("Updated content", card.Content);
        Assert.Equal("high", card.Priority);
        Assert.Equal(new DateOnly(2026, 4, 24), card.StartAt);
        Assert.Equal(new DateOnly(2026, 4, 30), card.DueAt);
    }

    [Fact]
    public async Task UpdateCard_WhenArchiving_ArchivesCardAndRemovesItFromColumn()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardCardGraph(db);
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.PatchAsync(
            $"/api/cards/{EncodeId(1000)}",
            JsonContent.Create(new { isArchived = true }));
        var body = await response.Content.ReadFromJsonAsync<CardDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body!.IsArchived);
        Assert.Null(body.ColumnId);
        Assert.NotNull(body.ArchivedAt);
        Assert.Equal(EncodeId(100), body.PreviousColumnId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == 1000);

        Assert.Null(card.ColumnId);
        Assert.Equal(100, card.PreviousColumnId);
        Assert.NotNull(card.ArchivedAt);
        Assert.True(card.ArchivedManually);
    }

    [Fact]
    public async Task UpdateCard_WhenAssigningNonMember_ReturnsBadRequest()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            SeedBoardCardGraph(db);
            db.Users.Add(CreateUser(3, "outsider"));
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.PatchAsync(
            $"/api/cards/{EncodeId(1000)}",
            JsonContent.Create(new { assigneeId = EncodeId(3) }));
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status400BadRequest, body!.Status);
        Assert.Equal("Assignee must be a board member.", body.Detail);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == 1000);
        Assert.Null(card.AssigneeId);
    }

    [Fact]
    public async Task GetBoardMembers_ForAcceptedMember_HidesOtherUsersEmails()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                CreateUser(2, "member"),
                CreateUser(3, "teammate"));

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });

            db.BoardMembers.AddRange(
                new BoardMember
                {
                    UserId = 1,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 0,
                    DateAdded = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc)
                },
                new BoardMember
                {
                    UserId = 2,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 1,
                    DateAdded = new DateTime(2026, 4, 23, 10, 5, 0, DateTimeKind.Utc)
                },
                new BoardMember
                {
                    UserId = 3,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 2,
                    DateAdded = new DateTime(2026, 4, 23, 10, 10, 0, DateTimeKind.Utc)
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 2);
        var response = await client.GetAsync($"/api/boards/{EncodeId(10)}/members");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EncodeId(1), body.GetProperty("ownerId").GetString());

        var members = body.GetProperty("members").EnumerateArray()
            .ToDictionary(
                member => member.GetProperty("userId").GetString()!,
                member => member);

        Assert.Equal(3, members.Count);
        Assert.Null(GetOptionalString(members[EncodeId(1)], "email"));
        Assert.Equal("member@example.com", GetOptionalString(members[EncodeId(2)], "email"));
        Assert.Null(GetOptionalString(members[EncodeId(3)], "email"));
    }

    [Fact]
    public async Task GetBoardMembers_ForOwner_ShowsAllEmails()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                CreateUser(2, "member"),
                CreateUser(3, "teammate"));

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });

            db.BoardMembers.AddRange(
                new BoardMember
                {
                    UserId = 1,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 0,
                    DateAdded = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc)
                },
                new BoardMember
                {
                    UserId = 2,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 1,
                    DateAdded = new DateTime(2026, 4, 23, 10, 5, 0, DateTimeKind.Utc)
                },
                new BoardMember
                {
                    UserId = 3,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 2,
                    DateAdded = new DateTime(2026, 4, 23, 10, 10, 0, DateTimeKind.Utc)
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.GetAsync($"/api/boards/{EncodeId(10)}/members");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EncodeId(1), body.GetProperty("ownerId").GetString());

        var members = body.GetProperty("members").EnumerateArray()
            .ToDictionary(
                member => member.GetProperty("userId").GetString()!,
                member => member);

        Assert.Equal("owner@example.com", GetOptionalString(members[EncodeId(1)], "email"));
        Assert.Equal("member@example.com", GetOptionalString(members[EncodeId(2)], "email"));
        Assert.Equal("teammate@example.com", GetOptionalString(members[EncodeId(3)], "email"));
    }

    private string EncodeId(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var idHasher = scope.ServiceProvider.GetRequiredService<IIdHasher>();
        return idHasher.Encode(id);
    }

    private static void SeedBoardCardGraph(AppDbContext db)
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

        db.Columns.Add(new Column
        {
            Id = 100,
            Title = "Todo",
            BoardId = 10,
            Order = 0
        });

        db.Cards.Add(new Card
        {
            Id = 1000,
            Title = "Initial card",
            Content = "Initial content",
            BoardId = 10,
            ColumnId = 100,
            Order = 0,
            CreatedById = 1,
            UpdatedById = 1,
            CreatedAt = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc)
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

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetString();
    }
}
