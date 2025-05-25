using FLTL.StravaIntegration.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FLTL.StravaIntegration.Functions;

public class StravaAuthFunction(ILogger<StravaAuthFunction> logger)
{
    [Function("StravaAuthFunction")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strava-auth")]
        HttpRequest req)
    {
        try
        {
            // Get user_id from query parameters
            if (!req.Query.TryGetValue("user_id", out var userIdValues) ||
                string.IsNullOrEmpty(userIdValues.FirstOrDefault()))
            {
                logger.LogWarning("Missing user_id parameter");
                return new BadRequestObjectResult("Missing user_id parameter");
            }

            var discordUserId = userIdValues.First()!;
            logger.LogInformation("Processing auth request for Discord user: {UserId}", discordUserId);

            // Get required environment variables
            var clientId = Environment.GetEnvironmentVariable("STRAVA_CLIENT_ID");
            var redirectUri = Environment.GetEnvironmentVariable("STRAVA_REDIRECT_URI");
            var signingSecret = Environment.GetEnvironmentVariable("STATE_SIGNING_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri) ||
                string.IsNullOrEmpty(signingSecret))
            {
                logger.LogError("Missing required environment variables");
                return new StatusCodeResult(500);
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

            // Return redirect response
            return new RedirectResult(stravaAuthUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Strava auth request");
            return new StatusCodeResult(500);
        }
    }
}