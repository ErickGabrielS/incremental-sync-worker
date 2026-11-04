using Microsoft.EntityFrameworkCore;
using SyncWorker;
using SyncWorker.Data;
using SyncWorker.Erp;
using SyncWorker.Sync;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Sync") ?? "Data Source=sync.db";

builder.Services.AddDbContext<SyncDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

builder.Services
    .AddHttpClient<IErpClient, ErpClient>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Erp:BaseUrl"] ?? "http://localhost:5080/");
    })
    // Retry com backoff exponencial, timeout por tentativa e circuit breaker.
    .AddStandardResilienceHandler();

builder.Services.AddScoped<OrderUpserter>();
builder.Services.AddScoped<OrderSyncService>();
builder.Services.AddScoped<ReprocessService>();
builder.Services.AddHostedService<SyncBackgroundService>();

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    // Projeto de exemplo: cria o banco direto. Em produção, usar migrations.
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedStatusMappingsAsync(db);
}

await host.RunAsync();
