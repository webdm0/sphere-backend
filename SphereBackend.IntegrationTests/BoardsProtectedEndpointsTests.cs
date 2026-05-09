using SphereBackend.Data;
using SphereBackend.Features.Boards;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;

namespace SphereBackend.IntegrationTests;

public sealed class BoardsProtectedEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BoardsProtectedEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBoard_WithoutAuthentication_ReturnsUnauthorized()
    {
        await _factory.ResetDatabaseAsync(_ => { });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/boards/any");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBoard_WithInvalidHash_ReturnsBadRequest()
    {
        await _factory.ResetDatabaseAsync(_ => { });

        var client = _factory.CreateAuthenticatedClient(userId: 1);

        var response = await client.GetAsync("/api/boards/not-a-valid-id");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status400BadRequest, body!.Status);
        Assert.Equal("Invalid request.", body.Detail);
    }

    [Fact]
    public async Task GetBoard_WithAuthenticatedOwner_ReturnsBoard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.Add(new User
            {
                Id = 1,
                Username = "owner",
                Email = "owner@example.com",
                NormalizedUsername = "owner",
                NormalizedEmail = "owner@example.com",
                PasswordHash = "hash",
                IsEmailConfirmed = true
            });

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });
        });

        var boardId = EncodeId(10);
        var client = _factory.CreateAuthenticatedClient(userId: 1);

        var response = await client.GetAsync($"/api/boards/{boardId}");
        var body = await response.Content.ReadFromJsonAsync<BoardShortDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(boardId, body!.Id);
        Assert.Equal("Roadmap", body.Title);
        Assert.False(body.IsArchived);
    }

    [Fact]
    public async Task GetBoard_WithAcceptedMember_ReturnsBoard()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                CreateUser(2, "member"));

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });

            db.BoardMembers.Add(new BoardMember
            {
                UserId = 2,
                BoardId = 10,
                IsAccepted = true,
                Order = 0
            });
        });

        var boardId = EncodeId(10);
        var client = _factory.CreateAuthenticatedClient(userId: 2);

        var response = await client.GetAsync($"/api/boards/{boardId}");
        var body = await response.Content.ReadFromJsonAsync<BoardShortDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(boardId, body!.Id);
        Assert.Equal("Roadmap", body.Title);
    }

    [Fact]
    public async Task GetBoard_WithAuthenticatedNonMember_ReturnsNotFound()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                new User
                {
                    Id = 1,
                    Username = "owner",
                    Email = "owner@example.com",
                    NormalizedUsername = "owner",
                    NormalizedEmail = "owner@example.com",
                    PasswordHash = "hash",
                    IsEmailConfirmed = true
                },
                new User
                {
                    Id = 2,
                    Username = "outsider",
                    Email = "outsider@example.com",
                    NormalizedUsername = "outsider",
                    NormalizedEmail = "outsider@example.com",
                    PasswordHash = "hash",
                    IsEmailConfirmed = true
                });

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });
        });

        var boardId = EncodeId(10);
        var client = _factory.CreateAuthenticatedClient(userId: 2);

        var response = await client.GetAsync($"/api/boards/{boardId}");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, body!.Status);
        Assert.Equal("Board not found.", body.Detail);
    }

    [Fact]
    public async Task GetBoard_WithPendingMember_ReturnsNotFound()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                CreateUser(2, "pending"));

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Roadmap",
                UserId = 1
            });

            db.BoardMembers.Add(new BoardMember
            {
                UserId = 2,
                BoardId = 10,
                IsAccepted = false,
                Order = 0
            });
        });

        var boardId = EncodeId(10);
        var client = _factory.CreateAuthenticatedClient(userId: 2);

        var response = await client.GetAsync($"/api/boards/{boardId}");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, body!.Status);
        Assert.Equal("Board not found.", body.Detail);
    }

    [Fact]
    public async Task RestoreBoard_WithArchivedBoard_KeepsOriginalBoardOrder()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.Add(CreateUser(1, "owner"));

            db.Boards.AddRange(
                new Board
                {
                    Id = 10,
                    Title = "First",
                    UserId = 1
                },
                new Board
                {
                    Id = 20,
                    Title = "Archived middle",
                    UserId = 1,
                    ArchivedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Board
                {
                    Id = 30,
                    Title = "Last",
                    UserId = 1
                });

            db.BoardMembers.AddRange(
                new BoardMember
                {
                    UserId = 1,
                    BoardId = 10,
                    IsAccepted = true,
                    Order = 0
                },
                new BoardMember
                {
                    UserId = 1,
                    BoardId = 20,
                    IsAccepted = true,
                    Order = 1
                },
                new BoardMember
                {
                    UserId = 1,
                    BoardId = 30,
                    IsAccepted = true,
                    Order = 2
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);

        var restoreResponse = await client.PostAsync($"/api/boards/{EncodeId(20)}/restore", content: null);
        var boardsResponse = await client.GetAsync("/api/boards");
        var boards = await boardsResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, boardsResponse.StatusCode);
        Assert.Equal(3, boards.GetArrayLength());
        Assert.Equal(EncodeId(10), boards[0].GetProperty("id").GetString());
        Assert.Equal(EncodeId(20), boards[1].GetProperty("id").GetString());
        Assert.Equal(EncodeId(30), boards[2].GetProperty("id").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var restoredBoard = await db.Boards.FindAsync(20);

        Assert.NotNull(restoredBoard);
        Assert.Null(restoredBoard!.ArchivedAt);
    }
    private string EncodeId(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var idHasher = scope.ServiceProvider.GetRequiredService<IIdHasher>();
        return idHasher.Encode(id);
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
