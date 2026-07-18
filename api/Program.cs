using Microsoft.AspNetCore.Authentication;
using Npgsql;
using Proto.Api.Auth;
using Proto.Api.Options;
using Proto.Api.Services;
using Stytch.net.Clients;

var builder = WebApplication.CreateBuilder(args);

// Bind to 0.0.0.0 so the hosting platform can reach us — the .NET default is
// localhost:5000, which Railway can't route to. Only override when the platform
// hasn't set the URLs itself (UseUrls would otherwise win over ASPNETCORE_URLS,
// breaking local dev / the launch profile). Railway doesn't inject $PORT here,
// so default to 8080 — set the service's target port to 8080 to match.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// --- Configuration binding -------------------------------------------------
builder.Services.Configure<StytchOptions>(
    builder.Configuration.GetSection(StytchOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(
    builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<ResendOptions>(
    builder.Configuration.GetSection(ResendOptions.SectionName));

// --- Stytch B2B backend client ---------------------------------------------
// Registered lazily: the singleton is only constructed the first time a handler
// injects it, so the app still starts (and health/me stubs still work) before
// real credentials are supplied. Handlers that need it should require
// StytchOptions.IsConfigured and return 503 otherwise.
builder.Services.AddSingleton(sp =>
{
    var opts = builder.Configuration
        .GetSection(StytchOptions.SectionName)
        .Get<StytchOptions>() ?? new StytchOptions();

    return new B2BClient(new ClientConfig
    {
        ProjectId = opts.ProjectId,
        ProjectSecret = opts.ProjectSecret,
    });
});

// --- CORS: allow the React dev origin(s) -----------------------------------
const string WebCorsPolicy = "web";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// --- Data access + provisioning -------------------------------------------
// Npgsql against the Supabase session pooler. Registered only when configured
// so the app still starts (and the 503 guard works) without a connection
// string. The data source is a singleton (owns the connection pool).
var supabaseOptions = builder.Configuration
    .GetSection(SupabaseOptions.SectionName).Get<SupabaseOptions>() ?? new SupabaseOptions();
if (supabaseOptions.IsConfigured)
{
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(supabaseOptions.ConnectionString));
}
builder.Services.AddScoped<TenantRepository>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<InvitationsService>();
builder.Services.AddHttpClient<ResendClient>();

// --- Authentication: validate the Stytch session on [Authorize] endpoints ---
builder.Services.AddAuthentication(StytchAuth.Scheme)
    .AddScheme<AuthenticationSchemeOptions, StytchSessionAuthenticationHandler>(StytchAuth.Scheme, null);
builder.Services.AddAuthorization();

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors(WebCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
