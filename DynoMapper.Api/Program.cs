using DynoMapper.Core;
using DynoMapper.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ── DynoMapper Registration ───────────────────────────────────────────
// Option A: Inline config
builder.Services.AddDynoMapper(options =>
{
    options.ConnectionString = builder.Configuration["DynoMapper:ConnectionString"]!;
    options.Provider = DbProvider.SqlServer; // SqlServer | PostgreSQL | MySQL
    options.CommandTimeoutSeconds = 30;
    options.Retry.MaxAttempts = 3;
    options.Retry.DelayMs = 200;
    options.Retry.UseJitter = true;
});

// Option B: From appsettings.json
//builder.Services.AddDynoMapper(builder.Configuration);

// ── MVC / Swagger ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();  // ← maps your UsersController
app.Run();