using System.Text.Json;
using FLTL.StravaIntegration.Helpers;
using FLTL.StravaIntegration.Models;
using FLTL.StravaIntegration.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FLTL.StravaIntegration.Functions;

public class StravaCallbackFunction(
    ILogger<StravaCallbackFunction> logger,
    HttpClient httpClient,
    IUserDataStorage userDataStorage)
{
    [Function("StravaCallbackFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strava-callback")]
        HttpRequest req)
    {
        try
        {
            // Get code and state from query parameters
            if (!req.Query.TryGetValue("code", out var codeValues) ||
                string.IsNullOrEmpty(codeValues.FirstOrDefault()))
            {
                logger.LogWarning("Missing code parameter");
                return new BadRequestObjectResult("Missing code parameter");
            }

            if (!req.Query.TryGetValue("state", out var stateValues) ||
                string.IsNullOrEmpty(stateValues.FirstOrDefault()))
            {
                logger.LogWarning("Missing state parameter");
                return new BadRequestObjectResult("Missing state parameter");
            }

            var code = codeValues.First()!;
            var state = stateValues.First()!;

            logger.LogInformation("Processing callback with code and state");

            // Get signing secret
            var signingSecret = Environment.GetEnvironmentVariable("STATE_SIGNING_SECRET");
            if (string.IsNullOrEmpty(signingSecret))
            {
                logger.LogError("Missing STATE_SIGNING_SECRET environment variable");
                return new StatusCodeResult(500);
            }

            // Verify state signature
            if (!HmacHelper.VerifySignedState(state, signingSecret, out var discordUserId))
            {
                logger.LogWarning("Invalid state signature");
                return new BadRequestObjectResult("Invalid state");
            }

            logger.LogInformation("Processing callback for Discord user: {UserId}", discordUserId);

            // Get required environment variables for token exchange
            var clientId = Environment.GetEnvironmentVariable("STRAVA_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("STRAVA_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                logger.LogError("Missing required Strava client credentials");
                return new StatusCodeResult(500);
            }

            // Exchange code for tokens
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            });

            var tokenResponse = await httpClient.PostAsync("https://www.strava.com/oauth/token", tokenRequest);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync();
                logger.LogError("Token exchange failed: {StatusCode} - {Content}",
                    tokenResponse.StatusCode, errorContent);
                return new StatusCodeResult(500);
            }

            var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<StravaTokenResponse>(tokenResponseContent);

            if (tokenData == null)
            {
                logger.LogError("Failed to parse token response");
                return new StatusCodeResult(500);
            } // Store user data

            var userData = new StravaUserData
            {
                AccessToken = tokenData.AccessToken,
                RefreshToken = tokenData.RefreshToken,
                ExpiresAt = tokenData.ExpiresAt,
                AthleteId = tokenData.Athlete.Id.ToString()
            };

            await userDataStorage.StoreUserDataAsync(discordUserId, userData);

            logger.LogInformation("Successfully stored auth data for Discord user: {UserId}, Athlete: {AthleteId}",
                discordUserId, tokenData.Athlete.Id);

            return new OkObjectResult("Authorized with Strava");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Strava callback");
            return new StatusCodeResult(500);
        }
    }
}
