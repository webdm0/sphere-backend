using SphereBackend.Data;
using SphereBackend.Features.Auth;
using SphereBackend.Features.Boards;
using SphereBackend.Features.Users.SearchUsers;
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

public sealed class DemoSessionEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DemoSessionEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDemoSession_CreatesConfirmedDemoUser_AndReturnsToken()
    {
        await _factory.ResetDatabaseAsync(_ => { });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.10");

        var response = await client.PostAsync("/api/auth/demo", content: null);
        var body = await response.Content.ReadFromJsonAsync<DemoSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();

        Assert.True(user.IsDemo);
        Assert.True(user.IsEmailConfirmed);
        Assert.StartsWith("demo_", user.Username, StringComparison.Ordinal);
        Assert.InRange(
            body.DemoExpiresAtUtc,
            user.CreatedAt.Add(DemoUserPolicy.Lifetime).AddSeconds(-1),
            user.CreatedAt.Add(DemoUserPolicy.Lifetime).AddSeconds(1));
    }

    [Fact]
    public async Task CreateDemoSession_RemovesExpiredDemoUsers_AndUnassignsCards()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                new User
                {
                    Id = 2,
                    Username = "demo_old",
                    Email = "demo_old@example.invalid",
                    NormalizedUsername = "demo_old",
                    NormalizedEmail = "demo_old@example.invalid",
                    PasswordHash = "hash",
                    IsEmailConfirmed = true,
                    IsDemo = true,
                    CreatedAt = DateTime.UtcNow.Subtract(DemoUserPolicy.Lifetime).AddMinutes(-5)
                });

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Board",
                UserId = 1
            });

            db.Columns.Add(new Column
            {
                Id = 20,
                Title = "Todo",
                BoardId = 10,
                Order = 0
            });

            db.Cards.Add(new Card
            {
                Id = 30,
                Title = "Task",
                ColumnId = 20,
                BoardId = 10,
                Order = 0,
                AssigneeId = 2,
                CreatedById = 1,
                UpdatedById = 1
            });
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.11");

        var response = await client.PostAsync("/api/auth/demo", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.OrderBy(u => u.Id).ToListAsync();
        var card = await db.Cards.SingleAsync(c => c.Id == 30);

        Assert.DoesNotContain(users, u => u.Username == "demo_old");
        Assert.Single(users.Where(u => u.IsDemo));
        Assert.Null(card.AssigneeId);
    }

    [Fact]
    public async Task Logout_WithDemoSession_DeletesDemoUser_AndUnassignsCards()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.Add(CreateUser(1, "owner"));
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.13");

        var createResponse = await client.PostAsync("/api/auth/demo", content: null);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var demoUser = await db.Users.SingleAsync(u => u.IsDemo);

            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Board",
                UserId = 1
            });

            db.Columns.Add(new Column
            {
                Id = 20,
                Title = "Todo",
                BoardId = 10,
                Order = 0
            });

            db.Cards.Add(new Card
            {
                Id = 30,
                Title = "Task",
                ColumnId = 20,
                BoardId = 10,
                Order = 0,
                AssigneeId = demoUser.Id,
                CreatedById = 1,
                UpdatedById = 1
            });

            await db.SaveChangesAsync();
        }

        var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await verificationDb.Users.OrderBy(u => u.Id).ToListAsync();
        var card = await verificationDb.Cards.SingleAsync(c => c.Id == 30);

        Assert.Single(users);
        Assert.Equal("owner", users[0].Username);
        Assert.Null(card.AssigneeId);
    }

    [Fact]
    public async Task SearchUsers_WithDemoAccount_ReturnsForbidden()
    {
        await _factory.ResetDatabaseAsync(_ => { });

        var client = _factory.CreateAuthenticatedClient(userId: 1, isDemo: true);

        var response = await client.GetAsync("/api/users/search?query=de");
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status403Forbidden, body!.Status);
        Assert.Equal(DemoUserPolicy.RestrictedFeatureMessage, body.Detail);
    }

    [Fact]
    public async Task CreateBoard_WithDemoAccountAndUserIds_ReturnsForbidden()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "demo_owner"),
                CreateUser(2, "teammate"));
        });

        using var scope = _factory.Services.CreateScope();
        var idHasher = scope.ServiceProvider.GetRequiredService<IIdHasher>();
        var teammateId = idHasher.Encode(2);

        var client = _factory.CreateAuthenticatedClient(userId: 1, isDemo: true);
        var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardDto
        {
            Title = "Demo board",
            UserIds = [teammateId]
        });
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status403Forbidden, body!.Status);
        Assert.Equal(DemoUserPolicy.RestrictedFeatureMessage, body.Detail);

        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Boards.ToListAsync());
        Assert.Empty(await db.BoardMembers.ToListAsync());
    }

    [Fact]
    public async Task CreateBoard_WithDemoTargetMember_ReturnsBadRequest()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                new User
                {
                    Id = 2,
                    Username = "demo_target",
                    Email = "demo_target@example.invalid",
                    NormalizedUsername = "demo_target",
                    NormalizedEmail = "demo_target@example.invalid",
                    PasswordHash = "hash",
                    IsEmailConfirmed = true,
                    IsDemo = true
                });
        });

        using var scope = _factory.Services.CreateScope();
        var idHasher = scope.ServiceProvider.GetRequiredService<IIdHasher>();
        var demoUserId = idHasher.Encode(2);

        var client = _factory.CreateAuthenticatedClient(userId: 1);
        var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardDto
        {
            Title = "Board",
            UserIds = [demoUserId]
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Some selected users can't be invited.", body, StringComparison.Ordinal);

        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Boards.ToListAsync());
        Assert.Empty(await db.BoardMembers.ToListAsync());
    }

    [Fact]
    public async Task CreateDemoSession_WhenRequestedTooManyTimes_ReturnsTooManyRequests()
    {
        await _factory.ResetDatabaseAsync(_ => { });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.12");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var okResponse = await client.PostAsync("/api/auth/demo", content: null);
            Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
        }

        var response = await client.PostAsync("/api/auth/demo", content: null);
        var body = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status429TooManyRequests, body!.Status);
        Assert.Equal("Too Many Requests", body.Title);
    }

    [Fact]
    public async Task SearchUsers_ForNormalUser_ExcludesDemoAccounts()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.AddRange(
                CreateUser(1, "owner"),
                CreateUser(2, "delta"),
                new User
                {
                    Id = 3,
                    Username = "demo_delta",
                    Email = "demo_delta@example.invalid",
                    NormalizedUsername = "demo_delta",
                    NormalizedEmail = "demo_delta@example.invalid",
                    PasswordHash = "hash",
                    IsEmailConfirmed = true,
                    IsDemo = true
                });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1);

        var response = await client.GetAsync("/api/users/search?query=de");
        var body = await response.Content.ReadFromJsonAsync<List<SearchUserItem>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Single(body!);
        Assert.Equal("delta", body[0].Username);
    }

    [Fact]
    public async Task GetBoardMembers_WithDemoAccount_ReturnsSelfMember()
    {
        await _factory.ResetDatabaseAsync(db =>
        {
            db.Users.Add(CreateUser(1, "demo_owner"));
            db.Boards.Add(new Board
            {
                Id = 10,
                Title = "Demo board",
                UserId = 1
            });
        });

        var client = _factory.CreateAuthenticatedClient(userId: 1, isDemo: true);

        var response = await client.GetAsync($"/api/boards/{EncodeId(10)}/members");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EncodeId(1), body.GetProperty("ownerId").GetString());

        var members = body.GetProperty("members").EnumerateArray().ToList();
        Assert.Single(members);
        Assert.Equal(EncodeId(1), members[0].GetProperty("userId").GetString());
        Assert.Equal("demo_owner", members[0].GetProperty("username").GetString());
        Assert.True(members[0].GetProperty("isAccepted").GetBoolean());
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

    private sealed class DemoSessionResponse
    {
        public string AccessToken { get; init; } = string.Empty;
        public DateTime DemoExpiresAtUtc { get; init; }
    }
}
