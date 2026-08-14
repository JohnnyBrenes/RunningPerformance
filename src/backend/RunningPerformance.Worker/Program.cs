using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RunningPerformance.Application.Ingestion;
using RunningPerformance.Infrastructure.Database;
using RunningPerformance.Infrastructure.Jobs;
using RunningPerformance.Infrastructure.Storage;

var builder = Host.CreateApplicationBuilder(args);
var options = builder.Configuration
    .GetSection(HistoricalImportOptions.SectionName)
    .Get<HistoricalImportOptions>() ?? new();
options.Validate();
var fitOptions = builder.Configuration
    .GetSection(FitIngestionOptions.SectionName)
    .Get<FitIngestionOptions>() ?? new();
fitOptions.Validate();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(fitOptions);
builder.Services.AddSingleton<NormalizedActivityCsvValidator>();
builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new NpgsqlDataSourceBuilder(
        DatabaseConfiguration.ResolveConnectionString(configuration)).Build();
});
builder.Services.AddSingleton<OwnerDataSource>();
builder.Services.AddHttpClient<SupabaseObjectStore>();
builder.Services.AddSingleton<CsvIngestionQueue>();
builder.Services.AddSingleton<FitIngestionQueue>();
builder.Services.AddHostedService<IngestionWorker>();
await builder.Build().RunAsync();
