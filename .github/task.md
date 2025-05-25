Please generate three C# Azure Functions that implement the following Strava OAuth and webhook flow, using either the in-process or isolated process programming model (your choice, but be consistent). Each function should be its own HTTP-triggered Azure Function with appropriate attributes and should use environment variables for configuration.

Function 1: StravaAuthFunction (GET /api/strava-auth)

    Accepts a query parameter user_id (Discord user ID).

    Uses HMAC-SHA256 to generate a signed state value in the format:
    <discord_user_id>:<base64url_signature>

    Uses a secret key from Environment.GetEnvironmentVariable("STATE_SIGNING_SECRET").

    Constructs a redirect URL to the Strava OAuth page:

    https://www.strava.com/oauth/authorize?client_id={CLIENT_ID}&response_type=code&redirect_uri={REDIRECT_URI}&scope=activity:read_all&state={signed_state}

    Reads CLIENT_ID and REDIRECT_URI from environment variables.

    Returns a redirect response to the Strava authorization URL.

Function 2: StravaCallbackFunction (GET /api/strava-callback)

    Accepts code and state query parameters.

    Verifies the state HMAC signature using the same secret key.

    Extracts discord_user_id from the state value.

    Makes a POST request to:

    https://www.strava.com/oauth/token

    with:

        client_id, client_secret, code, grant_type=authorization_code

    Parses the response JSON and stores:

        access_token, refresh_token, expires_at, athlete.id

    Store the data in a temporary in-memory dictionary or simulated storage structure keyed by Discord user ID.

    Returns a confirmation message ("Authorized with Strava").

Function 3: StravaWebhookFunction (GET/POST /api/strava-webhook)

    GET: Respond to Strava webhook verification:

        If hub.mode == "subscribe" and hub.verify_token matches STRAVA_VERIFY_TOKEN, return hub.challenge in JSON format.

    POST:

        Parse webhook payload.

        If aspect_type == "create" and object_type == "activity", extract owner_id and object_id.

        Optionally look up the corresponding Discord user.

        Placeholder for sending a Discord notification (e.g., logging or mock function).

Requirements

    Use the appropriate [FunctionName], [HttpTrigger], and ILogger for logging.

    Use environment variables for all secrets and settings:

        STRAVA_CLIENT_ID

        STRAVA_CLIENT_SECRET

        STRAVA_REDIRECT_URI

        STRAVA_VERIFY_TOKEN

        STATE_SIGNING_SECRET

    All functions should return appropriate status codes and messages for debugging or real use.

Optional

    Use a helper class for HMAC signing and verifying state.

    Show how to register the functions in FunctionApp.cs (if isolated model).

    Prefer async/await and use HttpClientFactory if appropriate.