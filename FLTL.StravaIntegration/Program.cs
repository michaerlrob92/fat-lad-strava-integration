using FLTL.StravaIntegration.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Register HttpClient
builder.Services.AddHttpClient();

// Register Cosmos DB client as singleton
builder.Services.AddSingleton<CosmosClient>(serviceProvider =>
{
    var connectionString = Environment.GetEnvironmentVariable("COSMOS_DB_CONNECTION_STRING");
    if (string.IsNullOrEmpty(connectionString))
        // Fall back to in-memory storage if Cosmos DB is not configured
        return null!;

    return new CosmosClient(connectionString, new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    });
});

// Register storage service - use Cosmos DB if configured, otherwise in-memory
builder.Services.AddSingleton<IUserDataStorage>(serviceProvider =>
{
    var cosmosClient = serviceProvider.GetService<CosmosClient>();
    var logger = serviceProvider.GetService<ILogger<CosmosUserDataStorage>>();

    if (cosmosClient != null && logger != null) return new CosmosUserDataStorage(cosmosClient, logger);

    // Fall back to in-memory storage
    return new InMemoryUserDataStorage();
});

// Register the user data service
builder.Services.AddScoped<StravaUserDataService>();

builder.Build().Run();