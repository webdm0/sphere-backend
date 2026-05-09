using SphereBackend.Data;
using SphereBackend.Models;
using SphereBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Resend;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SphereBackend.Features.Auth
{
    public interface IAuthFlowService
    {
        string GenericRegisterResponseMessage { get; }
        string GenericResendResponseMessage { get; }
        string NormalizeForStorage(string value);
        string NormalizeForLookup(string value);
        bool IsConfirmationEmailCooldownActive(User user);
        void MarkConfirmationEmailSent(User user);
        UserBanInfo GetBanInfo(string cacheKey);
        void RegisterAttempt(string cacheKey, UserBanInfo info);
        void RemoveBanInfo(string cacheKey);
        string GetClientIp(HttpContext httpContext);
        bool IsUniqueConstraintViolation(DbUpdateException ex);
        Task SendConfirmationEmailAsync(User user, string token, IResend resend);
        Task<string> CreateSessionAndGetTokenAsync(User user, HttpContext httpContext, CancellationToken cancellationToken = default);
        void ClearAuthCookies(HttpContext httpContext);
        void SetRefreshCookie(HttpContext httpContext, string refreshToken, DateTime expires);
        void SetSessionHintCookie(HttpContext httpContext, UserSession session);
        string ReadRefreshTokenFromCookie(HttpContext httpContext);
        string HashRefresh(string token);
        string GenerateAccessToken(User user);
    }

    public sealed class AuthFlowService : IAuthFlowService
    {
        private const string UseCrossSiteAuthCookiesConfigKey = "Cookies:UseCrossSiteAuth";
        private const string DevelopmentResendFromEmail = "onboarding@resend.dev";
        private const string RefreshTokenCookieName = "refreshToken";
        private const string SessionHintCookieName = "__session_hint";
        private const string XForwardedForHeaderName = "X-Forwarded-For";
        private const int SessionHintVersion = 1;
        private const int DefaultSessionHintTtlSeconds = 20;
        private const int MinSessionHintTtlSeconds = 15;
        private const int MaxSessionHintTtlSeconds = 30;
        private static readonly TimeSpan ConfirmationEmailCooldown = TimeSpan.FromMinutes(2);

        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;
        private readonly IMemoryCache _cache;
        private readonly IIdHasher _idHasher;

        public AuthFlowService(
            AppDbContext context,
            IConfiguration configuration,
            IWebHostEnvironment env,
            IMemoryCache cache,
            IIdHasher idHasher)
        {
            _context = context;
            _configuration = configuration;
            _env = env;
            _cache = cache;
            _idHasher = idHasher;
        }

        public string GenericRegisterResponseMessage => "If the request is valid, please check your email for next steps.";
        public string GenericResendResponseMessage => "If an unconfirmed account exists for this email, a new link has been sent.";

        public string NormalizeForStorage(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        public string NormalizeForLookup(string value)
        {
            return NormalizeForStorage(value).ToLowerInvariant();
        }

        public bool IsConfirmationEmailCooldownActive(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            return user.LastConfirmationEmailSentAt.HasValue &&
                DateTime.UtcNow - user.LastConfirmationEmailSentAt.Value < ConfirmationEmailCooldown;
        }

        public void MarkConfirmationEmailSent(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            user.LastConfirmationEmailSentAt = DateTime.UtcNow;
        }

        public UserBanInfo GetBanInfo(string cacheKey)
        {
            if (!_cache.TryGetValue<UserBanInfo>(cacheKey, out var cachedBanInfo) || cachedBanInfo is null)
            {
                cachedBanInfo = new UserBanInfo { Attempts = 0 };
            }

            return cachedBanInfo;
        }

        public void RegisterAttempt(string cacheKey, UserBanInfo info)
        {
            info.Attempts++;
            UpdateBanStatus(info);
            _cache.Set(cacheKey, info, TimeSpan.FromHours(1));
        }

        public void RemoveBanInfo(string cacheKey)
        {
            _cache.Remove(cacheKey);
        }

        public string GetClientIp(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var forwardedFor = httpContext.Request.Headers[XForwardedForHeaderName].ToString();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var firstIp = forwardedFor
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(firstIp))
                {
                    return firstIp;
                }
            }

            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        public bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
        }

        public async Task SendConfirmationEmailAsync(User user, string token, IResend resend)
        {
            var frontendBaseUrl = _configuration["Frontend:BaseUrl"]
                ?? throw new InvalidOperationException("Frontend:BaseUrl is required.");
            var resendFromEmail = GetResendFromEmail();

            var normalizedFrontendBaseUrl = frontendBaseUrl.TrimEnd('/');
            var callbackUrl = $"{normalizedFrontendBaseUrl}/confirm-email?token={token}";
            var filePath = Path.Combine(_env.ContentRootPath, "EmailTemplates", "ConfirmEmail.html");

            string htmlBody;
            try
            {
                htmlBody = await File.ReadAllTextAsync(filePath);
                htmlBody = htmlBody
                    .Replace("{{Username}}", user.Username)
                    .Replace("{{CallbackUrl}}", callbackUrl);
            }
            catch (Exception)
            {
                htmlBody = $"Welcome! Confirm here: {callbackUrl}";
            }

            var message = new EmailMessage
            {
                From = resendFromEmail,
                Subject = "Confirm your registration",
                HtmlBody = htmlBody
            };

            message.To.Add(user.Email);
            await resend.EmailSendAsync(message);
        }

        private string GetResendFromEmail()
        {
            var configuredFromEmail = _configuration["Resend:FromEmail"];
            if (!string.IsNullOrWhiteSpace(configuredFromEmail))
            {
                return configuredFromEmail;
            }

            if (_env.IsDevelopment())
            {
                return DevelopmentResendFromEmail;
            }

            throw new InvalidOperationException("Resend:FromEmail is required.");
        }

        public async Task<string> CreateSessionAndGetTokenAsync(
            User user,
            HttpContext httpContext,
            CancellationToken cancellationToken = default)
        {
            if (user.Sessions.Count >= 5)
            {
                var oldest = user.Sessions.OrderBy(s => s.CreatedAt).First();
                _context.UserSessions.Remove(oldest);
            }

            var refreshToken = GenerateRefreshToken();
            var session = new UserSession
            {
                RefreshTokenHash = HashRefresh(refreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                UserAgent = httpContext.Request.Headers["User-Agent"].ToString(),
                IpAddress = GetClientIp(httpContext)
            };

            user.Sessions.Add(session);
            await _context.SaveChangesAsync(cancellationToken);

            SetRefreshCookie(httpContext, refreshToken, session.ExpiresAt);
            SetSessionHintCookie(httpContext, session);
            return GenerateAccessToken(user);
        }

        public void ClearAuthCookies(HttpContext httpContext)
        {
            ClearRefreshCookie(httpContext);
            ClearSessionHintCookie(httpContext);
        }

        public void SetSessionHintCookie(HttpContext httpContext, UserSession session)
        {
            var ttlSeconds = GetSessionHintTtlSeconds();
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
            var signedHint = GenerateSessionHintToken(session, expiresAt);

            httpContext.Response.Cookies.Append(SessionHintCookieName, signedHint, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = GetAuthCookieSameSite(),
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(ttlSeconds),
                Expires = expiresAt
            });
        }

        public string ReadRefreshTokenFromCookie(HttpContext httpContext)
        {
            if (!httpContext.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var token))
            {
                return string.Empty;
            }

            return token ?? string.Empty;
        }

        public string HashRefresh(string token)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(hashBytes);
        }

        public string GenerateAccessToken(User user)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, _idHasher.Encode(user.Id)),
                new Claim("username", user.Username),
                new Claim("email", user.Email),
                new Claim(DemoUserPolicy.IsDemoClaimType, user.IsDemo ? bool.TrueString : bool.FalseString)
            };

            var claimList = claims.ToList();
            if (user.IsDemo)
            {
                var expiresAtUnixSeconds = new DateTimeOffset(DemoUserPolicy.GetExpiresAtUtc(user)).ToUnixTimeSeconds();
                claimList.Add(new Claim(DemoUserPolicy.ExpiresAtClaimType, expiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture)));
            }

            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claimList,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private void UpdateBanStatus(UserBanInfo info)
        {
            if (info.Attempts == 3) info.BannedUntil = DateTime.UtcNow.AddMinutes(1);
            else if (info.Attempts == 4) info.BannedUntil = DateTime.UtcNow.AddMinutes(5);
            else if (info.Attempts >= 5) info.BannedUntil = DateTime.UtcNow.AddMinutes(15);
        }

        public void SetRefreshCookie(HttpContext httpContext, string refreshToken, DateTime expires)
        {
            httpContext.Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = GetAuthCookieSameSite(),
                Expires = expires,
                Path = "/"
            });
        }

        private void ClearRefreshCookie(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Append(RefreshTokenCookieName, string.Empty, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = GetAuthCookieSameSite(),
                Path = "/",
                MaxAge = TimeSpan.Zero,
                Expires = DateTimeOffset.UnixEpoch
            });
        }

        private void ClearSessionHintCookie(HttpContext httpContext)
        {
            httpContext.Response.Cookies.Append(SessionHintCookieName, string.Empty, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = GetAuthCookieSameSite(),
                Path = "/",
                MaxAge = TimeSpan.Zero,
                Expires = DateTimeOffset.UnixEpoch
            });
        }

        private SameSiteMode GetAuthCookieSameSite()
        {
            return _configuration.GetValue<bool>(UseCrossSiteAuthCookiesConfigKey)
                ? SameSiteMode.None
                : SameSiteMode.Lax;
        }

        private int GetSessionHintTtlSeconds()
        {
            var configuredTtl = _configuration.GetValue<int?>("SessionHint:TtlSeconds");
            if (!configuredTtl.HasValue || configuredTtl.Value <= 0)
            {
                return DefaultSessionHintTtlSeconds;
            }

            return Math.Clamp(configuredTtl.Value, MinSessionHintTtlSeconds, MaxSessionHintTtlSeconds);
        }

        private string GenerateSessionHintToken(UserSession session, DateTimeOffset expiresAt)
        {
            var sessionHintKey = _configuration["SessionHint:Key"] ?? throw new InvalidOperationException("SessionHint:Key is required.");
            var now = DateTimeOffset.UtcNow;

            var claims = new List<Claim>
            {
                new("sid", _idHasher.Encode(session.Id)),
                new("ver", SessionHintVersion.ToString(CultureInfo.InvariantCulture)),
                new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sessionHintKey));
            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["SessionHint:Issuer"] ?? _configuration["Jwt:Issuer"],
                audience: _configuration["SessionHint:Audience"] ?? _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: expiresAt.UtcDateTime,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToHexString(randomNumber).ToLower();
        }
    }
}
