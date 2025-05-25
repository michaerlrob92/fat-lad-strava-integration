using System.Net;
using FLTL.StravaIntegration.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace FLTL.StravaIntegration.Services;

/// <summary>
///     Cosmos DB implementation of user data storage with optimized queries.
///     Uses Discord user ID as partition key for efficient read/write operations.
/// </summary>
public class CosmosUserDataStorage : IUserDataStorage
{
    private readonly Container _container;
    private readonly ILogger<CosmosUserDataStorage> _logger;

    public CosmosUserDataStorage(CosmosClient cosmosClient, ILogger<CosmosUserDataStorage> logger)
    {
        _logger = logger;

        var databaseName = Environment.GetEnvironmentVariable("COSMOS_DB_DATABASE_NAME");
        var containerName = Environment.GetEnvironmentVariable("COSMOS_DB_CONTAINER_NAME");

        if (string.IsNullOrEmpty(databaseName) || string.IsNullOrEmpty(containerName))
            throw new InvalidOperationException("Missing required Cosmos DB environment variables: COSMOS_DB_DATABASE_NAME and COSMOS_DB_CONTAINER_NAME");

        _container = cosmosClient.GetContainer(databaseName, containerName);
    }    public async Task StoreUserDataAsync(string discordUserId, StravaUserData userData)
    {
        try
        {
            // Ensure the partition key matches the document ID
            userData.Id = discordUserId;
            userData.UpdatedAt = DateTime.UtcNow;

            _logger.LogDebug("Storing user data: DiscordUserId={DiscordUserId}, AthleteId={AthleteId}", 
                discordUserId, userData.AthleteId);

            // Use the same value for both the document ID and partition key
            var partitionKey = new PartitionKey(discordUserId);
            var response = await _container.UpsertItemAsync(userData, partitionKey);

            _logger.LogInformation(
                "Successfully stored user data for Discord user {DiscordUserId}, Athlete {AthleteId}. RU consumed: {RequestCharge}",
                discordUserId, userData.AthleteId, response.RequestCharge);
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to store user data for Discord user {DiscordUserId}: {StatusCode} - {Message}. " +
                "Partition key issue? Make sure container partition key is '/id'",
                discordUserId, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error storing user data for Discord user {DiscordUserId}", discordUserId);
            throw;
        }
    }

    public async Task<StravaUserData?> GetUserDataAsync(string discordUserId)
    {
        try
        {
            var response =
                await _container.ReadItemAsync<StravaUserData>(discordUserId, new PartitionKey(discordUserId));

            _logger.LogInformation(
                "Successfully retrieved user data for Discord user {DiscordUserId}. RU consumed: {RequestCharge}",
                discordUserId, response.RequestCharge);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("User data not found for Discord user {DiscordUserId}", discordUserId);
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve user data for Discord user {DiscordUserId}: {StatusCode} - {Message}",
                discordUserId, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrieving user data for Discord user {DiscordUserId}",
                discordUserId);
            throw;
        }
    }

    public async Task<StravaUserData?> GetUserDataByAthleteIdAsync(string athleteId)
    {
        try
        {
            // Optimized: Get full user data in a single query instead of two separate calls
            var query = new QueryDefinition("SELECT * FROM c WHERE c.athleteId = @athleteId")
                .WithParameter("@athleteId", athleteId);

            var iterator = _container.GetItemQueryIterator<StravaUserData>(query);
            var results = new List<StravaUserData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);

                _logger.LogInformation("Query for athlete {AthleteId} consumed {RequestCharge} RU",
                    athleteId, response.RequestCharge);
            }

            if (results.Count > 0)
            {
                var userData = results.First();
                _logger.LogInformation("Found user data for athlete {AthleteId}, Discord user {DiscordUserId}",
                    athleteId, userData.Id);
                return userData;
            }

            _logger.LogInformation("No user data found for athlete {AthleteId}", athleteId);
            return null;
        }
        catch (CosmosException ex)
        {
            _logger.LogError(ex, "Failed to query user data by athlete ID {AthleteId}: {StatusCode} - {Message}",
                athleteId, ex.StatusCode, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error querying user data by athlete ID {AthleteId}", athleteId);
            throw;
        }
    }
}