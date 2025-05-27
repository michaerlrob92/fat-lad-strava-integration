using FLTL.StravaIntegration.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FLTL.StravaIntegration.Functions;

public class StravaAuthFunction(ILogger<StravaAuthFunction> logger)
{    [Function("StravaAuthFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strava-auth")]
        HttpRequestData req)
    {
        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var userId = query["user_id"];

            // Validate user_id parameter
            if (string.IsNullOrEmpty(userId))
            {
                logger.LogWarning("Missing user_id parameter");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Missing user_id parameter");
                return badRequestResponse;
            }

            var discordUserId = userId;
            logger.LogInformation("Processing auth request for Discord user: {UserId}", discordUserId);

            // Get required environment variables
            var clientId = Environment.GetEnvironmentVariable("STRAVA_CLIENT_ID");
            var redirectUri = Environment.GetEnvironmentVariable("STRAVA_REDIRECT_URI");
            var signingSecret = Environment.GetEnvironmentVariable("STATE_SIGNING_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri) ||
                string.IsNullOrEmpty(signingSecret))
            {
                logger.LogError("Missing required environment variables");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                return errorResponse;
            }

            // Generate signed state
            var signedState = HmacHelper.GenerateSignedState(discordUserId, signingSecret);

            // Construct Strava OAuth URL
            var stravaAuthUrl = $"https://www.strava.com/oauth/authorize" +
                                $"?client_id={clientId}" +
                                $"&response_type=code" +
                                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                                $"&scope=activity:read_all" +
                                $"&state={Uri.EscapeDataString(signedState)}";

            logger.LogInformation("Redirecting to Strava OAuth for user: {UserId}", discordUserId);

            // Create redirect response
            var response = req.CreateResponse(HttpStatusCode.Redirect);
            response.Headers.Add("Location", stravaAuthUrl);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Strava auth request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }
}