# API Endpoints Documentation

## Base URL
- **Local Development**: `http://localhost:7071/api`
- **Production**: `https://your-function-app.azurewebsites.net/api`

## Authentication
All endpoints are anonymous and require no authentication.

## Endpoints

### 1. Initiate Strava OAuth
**Endpoint**: `GET /strava-auth`

**Description**: Starts the Strava OAuth flow for a Discord user.

**Parameters**:
- `user_id` (required) - Discord user ID

**Example Request**:
```http
GET /api/strava-auth?user_id=123456789
```

**Response**:
- **302 Redirect** to Strava OAuth page
- **400 Bad Request** if user_id is missing
- **500 Internal Server Error** if configuration is invalid

---

### 2. OAuth Callback
**Endpoint**: `GET /strava-callback`

**Description**: Handles the OAuth callback from Strava and completes the authorization process.

**Parameters**:
- `code` (required) - Authorization code from Strava
- `state` (required) - Signed state value for verification

**Example Request**:
```http
GET /api/strava-callback?code=abc123&state=123456789:signature
```

**Response**:
```json
"Authorized with Strava"
```

**Error Responses**:
- **400 Bad Request** - Missing parameters or invalid state
- **500 Internal Server Error** - Token exchange failed

---

### 3. Webhook Management
**Endpoint**: `GET/POST /strava-webhook`

#### GET - Webhook Verification
**Description**: Verifies webhook subscription with Strava.

**Parameters**:
- `hub.mode` (required) - Should be "subscribe"
- `hub.verify_token` (required) - Verification token
- `hub.challenge` (required) - Challenge string to return

**Example Request**:
```http
GET /api/strava-webhook?hub.mode=subscribe&hub.verify_token=verify123&hub.challenge=challenge123
```

**Response**:
```json
{
  "hub_challenge": "challenge123"
}
```

#### POST - Webhook Events
**Description**: Processes webhook events from Strava.

**Request Body** (JSON):
```json
{
  "aspect_type": "create",
  "event_time": 1672531200,
  "object_id": 123456789,
  "object_type": "activity",
  "owner_id": 987654321,
  "subscription_id": 1
}
```

**Response**:
```json
"Event processed"
```

**Error Responses**:
- **400 Bad Request** - Invalid JSON or missing parameters
- **500 Internal Server Error** - Processing error

## Event Flow

### Complete OAuth Flow
1. **Initiate OAuth**:
   ```
   GET /api/strava-auth?user_id=discord123
   → Redirects to Strava
   ```

2. **User authorizes on Strava**:
   ```
   User grants permissions on Strava website
   ```

3. **OAuth Callback**:
   ```
   GET /api/strava-callback?code=auth_code&state=signed_state
   → Returns "Authorized with Strava"
   ```

4. **Webhook Events**:
   ```
   POST /api/strava-webhook
   Body: { "aspect_type": "create", "object_type": "activity", ... }
   → Processes activity and notifies Discord user
   ```

### Webhook Setup Flow
1. **Subscribe to webhook**:
   ```
   POST https://www.strava.com/api/v3/push_subscriptions
   {
     "client_id": "your_client_id",
     "client_secret": "your_client_secret",
     "callback_url": "https://your-app.com/api/strava-webhook",
     "verify_token": "your_verify_token"
   }
   ```

2. **Strava verifies webhook**:
   ```
   GET /api/strava-webhook?hub.mode=subscribe&hub.verify_token=your_token&hub.challenge=random
   → Returns challenge
   ```

3. **Receive activity events**:
   ```
   POST /api/strava-webhook
   → Process and forward to Discord
   ```

## Error Handling

### Common Error Responses

**400 Bad Request**:
```json
"Missing user_id parameter"
```

**500 Internal Server Error**:
```json
{
  "error": "Configuration error or processing failure"
}
```

## Rate Limits
- Strava API: 100 requests per 15 minutes, 1000 per day
- Azure Functions: Based on your consumption plan
- Consider implementing retry logic and caching

## Security Notes
- All state parameters are HMAC-signed to prevent tampering
- Environment variables protect sensitive configuration
- HTTPS should be used in production

## Testing

### Local Testing
```bash
# Test OAuth initiation
curl "http://localhost:7071/api/strava-auth?user_id=test123"

# Test webhook verification
curl "http://localhost:7071/api/strava-webhook?hub.mode=subscribe&hub.verify_token=test&hub.challenge=test123"

# Test webhook event
curl -X POST "http://localhost:7071/api/strava-webhook" \
  -H "Content-Type: application/json" \
  -d '{"aspect_type":"create","object_type":"activity","owner_id":123,"object_id":456}'
```

### Production Testing
Replace `localhost:7071` with your Azure Function App URL and ensure proper function keys are configured.
