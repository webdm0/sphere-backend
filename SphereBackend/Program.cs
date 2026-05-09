using HashidsNet;
using SphereBackend.Data;
using SphereBackend.Features.Auth;
using SphereBackend.Features.Boards;
using SphereBackend.Features.Cards;
using SphereBackend.Features.Columns;
using SphereBackend.Features.Events;
using SphereBackend.Features.Users.SearchUsers;
using SphereBackend.Infrastructure.Http;
using SphereBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Resend;
using System.Globalization;
using System.Security.Claims;
using System.Text;

LoadDotEnvIfExists();

var builder = WebApplication.CreateBuilder(args);

ConfigurePortFromEnvironment(builder);

var allowSseQueryStringToken = builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue<bool>("Sse:AllowQueryStringToken");
var enableForwardedHeaders = builder.Configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED");

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt:Key is required.");
}

var sessionHintKey = builder.Configuration["SessionHint:Key"];
if (string.IsNullOrWhiteSpace(sessionHintKey))
{
    throw new InvalidOperationException("SessionHint:Key is required.");
}

if (string.Equals(sessionHintKey, jwtKey, StringComparison.Ordinal))
{
    throw new InvalidOperationException("SessionHint:Key must be different from Jwt:Key.");
}

var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnection))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
}

var resendApiKey = builder.Configuration["Resend:ApiKey"];
if (string.IsNullOrWhiteSpace(resendApiKey))
{
    throw new InvalidOperationException("Resend:ApiKey is required.");
}

var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"];
if (string.IsNullOrWhiteSpace(frontendBaseUrl))
{
    throw new InvalidOperationException("Frontend:BaseUrl is required.");
}

var resendFromEmail = builder.Configuration["Resend:FromEmail"];
if (string.IsNullOrWhiteSpace(resendFromEmail) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Resend:FromEmail is required.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var path = context.HttpContext.Request.Path;
                if (path.StartsWithSegments("/api/events"))
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    var hasBearerHeader = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);

                    if (!hasBearerHeader && allowSseQueryStringToken)
                    {
                        var accessToken = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrWhiteSpace(accessToken))
                        {
                            context.Token = accessToken;
                        }
                    }
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var idHasher = context.HttpContext.RequestServices.GetRequiredService<IIdHasher>();
                var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrWhiteSpace(userIdClaim) || !idHasher.TryDecode(userIdClaim, out _))
                {
                    context.Fail("Invalid user id claim.");
                    return Task.CompletedTask;
                }

                var isDemoClaim = context.Principal?.FindFirst(DemoUserPolicy.IsDemoClaimType)?.Value;
                if (bool.TryParse(isDemoClaim, out var isDemo) && isDemo)
                {
                    var demoExpiresAtClaim = context.Principal?.FindFirst(DemoUserPolicy.ExpiresAtClaimType)?.Value;
                    if (!long.TryParse(demoExpiresAtClaim, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAtUnix))
                    {
                        context.Fail("Invalid demo expiration claim.");
                        return Task.CompletedTask;
                    }

                    if (DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix) <= DateTimeOffset.UtcNow)
                    {
                        context.Fail("Demo session expired.");
                    }
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(options =>
{
    options.ApiToken = resendApiKey;
});
builder.Services.AddTransient<IResend, ResendClient>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiProblemDetailsResultFilter>();
});

if (enableForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = ApiProblemDetailsFactory.CreateValidation(context.ModelState, context.HttpContext);
        return new BadRequestObjectResult(problem);
    };
});
builder.Services.AddAutoMapper(typeof(Program).Assembly);

builder.Services.AddSingleton<IHashids>(_ =>
{
    var salt = builder.Configuration["Hashids:Salt"];
    if (string.IsNullOrWhiteSpace(salt))
    {
        throw new InvalidOperationException("Hashids:Salt is required.");
    }

    var minLengthConfig = builder.Configuration["Hashids:MinHashLength"];
    if (!int.TryParse(minLengthConfig, out var minLength) || minLength <= 0)
    {
        throw new InvalidOperationException("Hashids:MinHashLength must be a positive integer.");
    }

    return new Hashids(salt, minLength);
});

builder.Services.AddSingleton<IIdHasher, IdHasher>();
builder.Services.AddScoped<IGetBoardHandler, GetBoardHandler>();
builder.Services.AddScoped<IGetBoardColumnsHandler, GetBoardColumnsHandler>();
builder.Services.AddScoped<IGetArchivedBoardColumnsHandler, GetArchivedBoardColumnsHandler>();
builder.Services.AddScoped<IGetBoardCardsHandler, GetBoardCardsHandler>();
builder.Services.AddScoped<IGetArchivedBoardCardsHandler, GetArchivedBoardCardsHandler>();
builder.Services.AddScoped<ICreateBoardHandler, CreateBoardHandler>();
builder.Services.AddScoped<IGetBoardsHandler, GetBoardsHandler>();
builder.Services.AddScoped<IGetArchivedBoardsHandler, GetArchivedBoardsHandler>();
builder.Services.AddScoped<ILeaveBoardHandler, LeaveBoardHandler>();
builder.Services.AddScoped<IUpdateBoardHandler, UpdateBoardHandler>();
builder.Services.AddScoped<IArchiveBoardHandler, ArchiveBoardHandler>();
builder.Services.AddScoped<IRestoreBoardHandler, RestoreBoardHandler>();
builder.Services.AddScoped<IDeleteBoardHandler, DeleteBoardHandler>();
builder.Services.AddScoped<IInviteBoardUserHandler, InviteBoardUserHandler>();
builder.Services.AddScoped<IAcceptBoardInviteHandler, AcceptBoardInviteHandler>();
builder.Services.AddScoped<IDeclineBoardInviteHandler, DeclineBoardInviteHandler>();
builder.Services.AddScoped<IGetBoardMembersHandler, GetBoardMembersHandler>();
builder.Services.AddScoped<IAddBoardMemberHandler, AddBoardMemberHandler>();
builder.Services.AddScoped<IRemoveBoardMemberHandler, RemoveBoardMemberHandler>();
builder.Services.AddScoped<IReorderColumnsHandler, ReorderColumnsHandler>();
builder.Services.AddScoped<ICreateColumnHandler, CreateColumnHandler>();
builder.Services.AddScoped<IUpdateColumnHandler, UpdateColumnHandler>();
builder.Services.AddScoped<IRestoreColumnHandler, RestoreColumnHandler>();
builder.Services.AddScoped<IDeleteColumnForeverHandler, DeleteColumnForeverHandler>();
builder.Services.AddScoped<ICreateCardHandler, CreateCardHandler>();
builder.Services.AddScoped<IUpdateCardHandler, UpdateCardHandler>();
builder.Services.AddScoped<IRestoreCardHandler, RestoreCardHandler>();
builder.Services.AddScoped<IDeleteCardForeverHandler, DeleteCardForeverHandler>();
builder.Services.AddScoped<IGetCardHandler, GetCardHandler>();
builder.Services.AddScoped<IReorderCardsHandler, ReorderCardsHandler>();
builder.Services.AddScoped<IAuthFlowService, AuthFlowService>();
builder.Services.AddScoped<IAppCleanupService, AppCleanupService>();
builder.Services.AddScoped<IRegisterHandler, RegisterHandler>();
builder.Services.AddScoped<IConfirmEmailHandler, ConfirmEmailHandler>();
builder.Services.AddScoped<IResendConfirmationHandler, ResendConfirmationHandler>();
builder.Services.AddScoped<ILoginHandler, LoginHandler>();
builder.Services.AddScoped<ILogoutHandler, LogoutHandler>();
builder.Services.AddScoped<IValidateSessionHandler, ValidateSessionHandler>();
builder.Services.AddScoped<IRefreshSessionHandler, RefreshSessionHandler>();
builder.Services.AddScoped<ICreateDemoSessionHandler, CreateDemoSessionHandler>();
builder.Services.AddScoped<ISearchUsersHandler, SearchUsersHandler>();
builder.Services.AddScoped<ISubscribeBoardEventsHandler, SubscribeBoardEventsHandler>();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<BoardEventService>();
builder.Services.AddSingleton<IBoardEventService>(sp => sp.GetRequiredService<BoardEventService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<BoardEventService>());

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(defaultConnection));

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("AllowedOrigins must contain at least one origin.");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddHostedService<CleanupService>();

var app = builder.Build();

if (allowSseQueryStringToken)
{
    app.Logger.LogWarning("SSE query-string token fallback is enabled. Use only for local development.");
}

if (enableForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is not null)
        {
            app.Logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = ApiProblemDetailsFactory.Create(
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",
            context);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, cancellationToken: context.RequestAborted);
    });
});

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

app.UseAuthentication();

app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapControllers();

app.Run();

static void ConfigurePortFromEnvironment(WebApplicationBuilder builder)
{
    var port = Environment.GetEnvironmentVariable("PORT");
    if (string.IsNullOrWhiteSpace(port))
    {
        return;
    }

    if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort) ||
        parsedPort <= 0)
    {
        throw new InvalidOperationException("PORT must be a positive integer.");
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{parsedPort}");
}

static void LoadDotEnvIfExists()
{
    var envPath = ResolveDotEnvPath();
    if (envPath is null)
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line["export ".Length..].Trim();
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        if (key.Length == 0)
        {
            continue;
        }

        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1];
        }

        value = value
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

static string? ResolveDotEnvPath()
{
    var currentDirectory = Directory.GetCurrentDirectory();
    var candidates = new[]
    {
        Path.Combine(currentDirectory, ".env"),
        Path.Combine(currentDirectory, "SphereBackend", ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env")
    };

    return candidates.FirstOrDefault(File.Exists);
}
