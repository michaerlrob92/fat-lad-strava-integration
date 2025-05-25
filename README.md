# Strava Integration Azure Functions

This project implements a complete Strava OAuth and webhook integration using Azure Functions with the isolated process model (.NET 9).

## Overview

The solution consists of three Azure Functions that handle the complete Strava OAuth flow and webhook processing:

1. **StravaAuthFunction** - Initiates OAuth authorization
2. **StravaCallbackFunction** - Handles OAuth callback and token exchange
3. **StravaWebhookFunction** - Processes Strava webhook events

## Functions

### 1. StravaAuthFunction (`GET /api/strava-auth`)

**Purpose**: Initiates the Strava OAuth flow for a Discord user.

**Parameters**:
- `user_id` (query parameter) - Discord user ID

**Flow**:
1. Generates a signed state value using HMAC-SHA256
2. Constructs Strava OAuth authorization URL
3. Redirects user to Strava for authorization

**Example Usage**:
```
GET http://localhost:7071/api/strava-auth?user_id=123456789
```

### 2. StravaCallbackFunction (`GET /api/strava-callback`)

**Purpose**: Handles the OAuth callback from Strava and exchanges authorization code for tokens.

**Parameters**:
- `code` (query parameter) - Authorization code from Strava
- `state` (query parameter) - Signed state value for verification

**Flow**:
1. Verifies the state signature
2. Exchanges authorization code for access tokens
3. Stores user data in memory
4. Returns confirmation message

**Example Response**:
```
"Authorized with Strava"
```

### 3. StravaWebhookFunction (`GET/POST /api/strava-webhook`)

**Purpose**: Handles Strava webhook verification and activity events.

**GET (Webhook Verification)**:
- Verifies webhook subscription with Strava
- Returns challenge token if verification succeeds

**POST (Webhook Events)**:
- Processes activity creation events
- Looks up Discord user by athlete ID
- Placeholder for Discord notifications

## Configuration

### Environment Variables

All configuration is handled through environment variables. Update your `local.settings.json` or Azure App Settings:

```json
{
  "Values": {
    "STRAVA_CLIENT_ID": "your_strava_client_id",
    "STRAVA_CLIENT_SECRET": "your_strava_client_secret", 
    "STRAVA_REDIRECT_URI": "https://your-function-app.azurewebsites.net/api/strava-callback",
    "STRAVA_VERIFY_TOKEN": "your_webhook_verify_token",
    "STATE_SIGNING_SECRET": "your_state_signing_secret",
    "COSMOS_DB_CONNECTION_STRING": "your_cosmos_db_connection_string",
    "COSMOS_DB_DATABASE_NAME": "StravaIntegration",
    "COSMOS_DB_CONTAINER_NAME": "Users"
  }
}
```

#### Cosmos DB Configuration (Optional)

For persistent storage, configure the following Cosmos DB settings:

- **COSMOS_DB_CONNECTION_STRING**: Your Cosmos DB account connection string (required for Cosmos DB storage)
- **COSMOS_DB_DATABASE_NAME**: Database name (default: "StravaIntegration")  
- **COSMOS_DB_CONTAINER_NAME**: Container name (default: "Users")

If Cosmos DB settings are not provided, the application will automatically fall back to in-memory storage for development/testing.

### Required Strava App Configuration

1. Create a Strava API application at [https://www.strava.com/settings/api](https://www.strava.com/settings/api)
2. Set the authorization callback domain to your function app domain
3. Note your Client ID and Client Secret
4. Configure webhook endpoint URL: `https://your-function-app.azurewebsites.net/api/strava-webhook`

### Cosmos DB Setup (Optional)

For persistent storage, set up Azure Cosmos DB:

1. **Create Cosmos DB Account**:
   - Create an Azure Cosmos DB account with SQL API
   - Note the connection string from the Keys section

2. **Create Database and Container**:
   ```bash
   # Using Azure CLI
   az cosmosdb sql database create --account-name your-cosmos-account --name StravaIntegration --resource-group your-resource-group
   
   az cosmosdb sql container create --account-name your-cosmos-account --database-name StravaIntegration --name Users --partition-key-path "/id" --resource-group your-resource-group
   ```

3. **Data Model**:
   The application stores user data with the following schema:
   ```json
   {
     "id": "discord_user_id",           // Partition key
     "athleteId": "strava_athlete_id",  // Strava athlete ID as string
     "accessToken": "access_token",     // Current access token
     "refreshToken": "refresh_token",   // Token for refreshing access
     "expiresAt": "2025-05-25T10:30:00Z", // ISO 8601 expiration timestamp
     "updatedAt": "2025-05-25T09:00:00Z"  // Last updated timestamp
   }
   ```

If Cosmos DB is not configured, the application automatically falls back to in-memory storage.

## Project Structure

```
FLTL.StravaIntegration/
├── Functions/
│   ├── StravaAuthFunction.cs        # OAuth initiation
│   ├── StravaCallbackFunction.cs    # OAuth callback handling
│   └── StravaWebhookFunction.cs     # Webhook processing
├── Helpers/
│   └── HmacHelper.cs                # HMAC signing/verification utilities
├── Models/
│   └── StravaModels.cs              # Data models for Strava API responses
├── Services/
│   ├── IUserDataStorage.cs          # Storage interface definition
│   ├── UserDataStorage.cs           # In-memory storage implementation
│   ├── CosmosUserDataStorage.cs     # Cosmos DB storage implementation
│   └── StravaUserDataService.cs     # Service layer with token management
└── Program.cs                       # Application configuration and DI setup
```

## Key Features

### Security
- **HMAC-SHA256 State Signing**: Prevents CSRF attacks during OAuth flow
- **State Verification**: Validates OAuth callback authenticity
- **Base64URL Encoding**: Secure URL-safe encoding for state parameters

### Data Storage
- **Cosmos DB Integration**: Primary persistent storage using Azure Cosmos DB SQL API
- **In-Memory Fallback**: Automatic fallback to in-memory storage when Cosmos DB is not configured
- **Optimized Interface**: Simplified `IUserDataStorage` interface with 3 core methods for better maintainability
- **Efficient Queries**: Single-call athlete ID lookup reduces database round trips by 50%
- **Thread-Safe**: Uses `ConcurrentDictionary` for safe concurrent access in fallback mode
- **Token Management**: Automatic refresh of expired Strava access tokens
- **Service Layer**: `StravaUserDataService` provides abstraction over storage implementations

### Error Handling
- Comprehensive logging throughout all functions
- Proper HTTP status codes for different error scenarios
- Graceful handling of missing environment variables
- JSON parsing error handling for webhook payloads

### Extensibility
- **Interface-based Storage**: Easy to replace with database storage
- **Modular Design**: Clear separation of concerns
- **Dependency Injection**: Proper DI container usage
- **Async/Await**: Proper async programming patterns

## Development

### Prerequisites
- .NET 9 SDK
- Azure Functions Core Tools
- Valid Strava API application

### Running Locally

1. Clone the repository
2. Update `local.settings.json` with your Strava app credentials
3. Run the functions:
   ```bash
   cd FLTL.StravaIntegration
   func start
   ```

### Testing the Flow

1. **Start OAuth Flow**:
   ```
   GET http://localhost:7071/api/strava-auth?user_id=discord123
   ```

2. **Complete OAuth** (automatically handled after Strava authorization)

3. **Test Webhook Verification**:
   ```
   GET http://localhost:7071/api/strava-webhook?hub.mode=subscribe&hub.verify_token=your_verify_token&hub.challenge=test123
   ```

4. **Test Webhook Event** (POST with Strava event JSON)

## Deployment

### Azure Deployment

1. Create an Azure Function App with .NET 9 isolated runtime
2. Configure application settings with your Strava credentials
3. Deploy using Visual Studio, VS Code, or Azure CLI
4. Update Strava app settings with production URLs

### Production Considerations

- Configure Cosmos DB for persistent storage (see Cosmos DB Setup section)
- Implement proper Discord notification logic in webhook function
- Add authentication/authorization for function endpoints
- Configure monitoring and alerting
- Set up proper logging with Application Insights
- Monitor Cosmos DB RU consumption and scaling
- Implement retry policies for Cosmos DB operations

## Extension Points

### Discord Integration
The `SendDiscordNotification` method in `StravaWebhookFunction` is currently a placeholder. Implement with:
- Discord webhook calls
- Discord bot API integration
- Message queuing for reliable delivery

### Persistent Storage
Current implementation includes both in-memory and Cosmos DB storage:

**Cosmos DB (Recommended for Production)**:
- Globally distributed, multi-model database
- Automatic scaling and high availability
- Strong consistency guarantees
- Built-in security and compliance

**In-Memory Storage (Development/Testing)**:
- Simple concurrent dictionary
- Automatic fallback when Cosmos DB not configured
- Data lost on function app restart

**Migration Path**:
- Start development with in-memory storage
- Add Cosmos DB configuration for production deployment
- No code changes required - automatic detection and fallback

## Documentation

### Architecture & Implementation
- **[DATABASE_OPTIMIZATION.md](DATABASE_OPTIMIZATION.md)** - Query optimization and performance improvements
- **[INTERFACE_SIMPLIFICATION.md](INTERFACE_SIMPLIFICATION.md)** - Storage interface simplification details
- **[COSMOS_DB_SETUP.md](COSMOS_DB_SETUP.md)** - Comprehensive Cosmos DB setup guide
- **[IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md)** - Complete project status and changes

### Troubleshooting
- **[COSMOS_DB_PARTITION_KEY_FIX.md](COSMOS_DB_PARTITION_KEY_FIX.md)** - Fix for partition key mismatch errors

### Additional OAuth Scopes
Current implementation requests `activity:read_all`. You can extend to include:
- `activity:write` - Modify activities
- `profile:read_all` - Access profile information
- `profile:write` - Update profile

## License

[Add your license information here]
