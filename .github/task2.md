    Please update the previously generated Azure Functions project (StravaAuthFunction, StravaCallbackFunction, StravaWebhookFunction) to use Azure Cosmos DB for persistent user data storage. The storage should associate a Discord user ID with:

        Strava athlete ID

        Access token

        Refresh token

        Token expiration (expires_at)

        Timestamp of when the data was last updated

There is already an IUserDataStorage interface that should be used/updated. Do not remove the InMemoryUserDataStorage, intead split out the interface and class from the same file.

📦 Requirements:
1. Cosmos DB Integration

    Use the SQL (Core) API.

    Use the Cosmos DB SDK for .NET (Microsoft.Azure.Cosmos).

    Read the following from environment variables:

        COSMOS_DB_CONNECTION_STRING

        COSMOS_DB_DATABASE_NAME

        COSMOS_DB_CONTAINER_NAME

2. Data Model

Create a C# class like:

public class StravaUserData
{
    public string Id { get; set; } // Discord user ID (partition key)
    public string AthleteId { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public long ExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

3. Updates to Functions
StravaCallbackFunction

    After verifying the state and exchanging the code for tokens:

        Save/update the user in Cosmos DB using the Discord user ID as the id/partition key.

        Use an upsert operation.

StravaWebhookFunction (POST)

    When an event is received:

        Look up the StravaUserData in Cosmos DB using AthleteId.

        If found, use AccessToken to fetch activity details.

        Optionally refresh the token if expired (use and update Cosmos DB with new tokens).

4. Best Practices

    Use CosmosClient as a singleton (static/shared instance).

    Use dependency injection (if isolated model) or a static helper class for Cosmos access.

    Implement retry logic or logging for failures.

5. Optional Enhancements

    Add a StravaUserDataService class to abstract Cosmos access.

    Include logs for reads, upserts, and any errors.