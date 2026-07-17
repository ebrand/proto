using Proto.Api.Options;
using Stytch.net.Clients;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration binding -------------------------------------------------
builder.Services.Configure<StytchOptions>(
    builder.Configuration.GetSection(StytchOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(
    builder.Configuration.GetSection(SupabaseOptions.SectionName));

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

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(WebCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
