# Cosmos DB Partition Key Troubleshooting

## Issue: "PartitionKey extracted from document doesn't match the one specified in the header"

This error occurs when there's a mismatch between the partition key specified in the Cosmos DB container and what's being sent in the upsert operation.

## Root Cause

The error typically happens when:
1. The Cosmos DB container was created with a different partition key path
2. The JSON serialization doesn't match the expected field name
3. The document ID and partition key values don't align properly

## Solution Applied

### 1. Updated JSON Serialization
Added explicit `JsonPropertyName` attributes to the `StravaUserData` model:

```csharp
public class StravaUserData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // Discord user ID (partition key)
    
    [JsonPropertyName("athleteId")]
    public string AthleteId { get; set; } = string.Empty;
    
    // ... other properties with JsonPropertyName attributes
}
```

### 2. Improved Storage Logic
Enhanced the `StoreUserDataAsync` method to ensure proper partition key handling:

```csharp
// Ensure the partition key matches the document ID
userData.Id = discordUserId;
userData.UpdatedAt = DateTime.UtcNow;

// Use the same value for both the document ID and partition key
var partitionKey = new PartitionKey(discordUserId);
var response = await _container.UpsertItemAsync(userData, partitionKey);
```

## Verification Steps

### 1. Check Cosmos DB Container Configuration

Verify your Cosmos DB container was created with the correct partition key:

```bash
# Using Azure CLI
az cosmosdb sql container show \
  --account-name your-cosmos-account \
  --database-name StravaIntegration \
  --name Users \
  --resource-group your-resource-group \
  --query "resource.partitionKey"
```

**Expected Output:**
```json
{
  "paths": [
    "/id"
  ],
  "kind": "Hash"
}
```

### 2. Recreate Container if Needed

If the partition key is wrong, you'll need to recreate the container:

```bash
# Delete existing container (WARNING: This deletes all data!)
az cosmosdb sql container delete \
  --account-name your-cosmos-account \
  --database-name StravaIntegration \
  --name Users \
  --resource-group your-resource-group

# Create new container with correct partition key
az cosmosdb sql container create \
  --account-name your-cosmos-account \
  --database-name StravaIntegration \
  --name Users \
  --partition-key-path "/id" \
  --throughput 400 \
  --resource-group your-resource-group
```

### 3. Test the Fix

1. **Start the Function App**:
   ```powershell
   cd "c:\Users\mikew\RiderProjects\FLTL.StravaIntegration\FLTL.StravaIntegration"
   func start
   ```

2. **Test OAuth Flow**:
   - Navigate to: `http://localhost:7071/api/strava-auth?user_id=test123`
   - Complete the Strava authorization
   - Check the callback endpoint logs for successful storage

3. **Check Logs**:
   Look for these log messages:
   - ✅ `"Successfully stored user data for Discord user test123"`
   - ❌ `"Failed to store user data"` (with partition key error details)

## Alternative Container Creation (Portal)

If using the Azure Portal:

1. Go to your Cosmos DB account
2. Data Explorer → New Container
3. **Database ID**: `StravaIntegration` (or use existing)
4. **Container ID**: `Users`
5. **Partition key**: `/id` (exactly like this, with the forward slash)
6. **Throughput**: `400` RU/s (minimum)

## Additional Debugging

If the error persists, check:

1. **Environment Variables**:
   ```json
   {
     "COSMOS_DB_DATABASE_NAME": "StravaIntegration",
     "COSMOS_DB_CONTAINER_NAME": "Users"
   }
   ```

2. **Document Structure** in Cosmos DB:
   The stored document should look like:
   ```json
   {
     "id": "discord_user_123",
     "athleteId": "strava_athlete_456",
     "accessToken": "...",
     "refreshToken": "...",
     "expiresAt": 1234567890,
     "updatedAt": "2025-05-25T10:30:00Z"
   }
   ```

3. **Connection String**: Ensure it's valid and has read/write permissions

## Quick Test Commands

### Test In-Memory Fallback (Should Work)
```powershell
# Remove Cosmos DB config temporarily
$env:COSMOS_DB_CONNECTION_STRING = ""
func start
```

### Test Cosmos DB (After Fix)
```powershell
# Restore Cosmos DB config
$env:COSMOS_DB_CONNECTION_STRING = "your-connection-string"
func start
```

## Expected Behavior After Fix

1. **Successful Storage**: You should see `"Successfully stored user data"` logs
2. **No Partition Errors**: No more partition key mismatch errors
3. **Data Persistence**: User data should persist across function restarts
4. **Webhook Processing**: Athlete ID lookups should work correctly

If you continue to have issues after these changes, the problem might be:
- Incorrect Cosmos DB container partition key configuration
- Connection string permissions
- Network connectivity issues

Check the function logs for the specific error details and verify the container partition key path is exactly `/id`.
