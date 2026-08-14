using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace RunningPerformance.Api.Authentication;

public static class SupabaseAuthentication
{
    public static IServiceCollection AddSupabaseAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        bool isOpenApiGeneration = false)
    {
        var configuredUrl = configuration["Supabase:Url"] ?? configuration["SUPABASE_URL"];
        if (environment.IsProduction()
            && !isOpenApiGeneration
            && string.IsNullOrWhiteSpace(configuredUrl))
        {
            throw new InvalidOperationException("SUPABASE_URL is required in production.");
        }

        var fallbackUrl = environment.IsProduction()
            ? "https://openapi-generation.invalid"
            : "http://127.0.0.1:54321";
        var supabaseUrl = (configuredUrl ?? fallbackUrl).TrimEnd('/');
        var issuer = $"{supabaseUrl}/auth/v1";

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.MetadataAddress = $"{issuer}/.well-known/openid-configuration";
                options.Audience = "authenticated";
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = "authenticated",
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "email",
                    RoleClaimType = "role"
                };
            });

        var authenticatedAthlete = new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim("role", "authenticated")
            .RequireAssertion(context =>
                Guid.TryParse(context.User.FindFirst("sub")?.Value, out var ownerId)
                && ownerId != Guid.Empty)
            .Build();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticatedAthlete)
            .SetFallbackPolicy(authenticatedAthlete);

        return services;
    }
}
