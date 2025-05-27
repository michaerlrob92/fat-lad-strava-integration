using System.Text.Json;
using FLTL.StravaIntegration.Helpers;
using FLTL.StravaIntegration.Models;
using FLTL.StravaIntegration.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FLTL.StravaIntegration.Functions;

public class StravaCallbackFunction(
    ILogger<StravaCallbackFunction> logger,
    HttpClient httpClient,
    IUserDataStorage userDataStorage)
{    [Function("StravaCallbackFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "strava-callback")]
        HttpRequestData req)    {
        try
        {
            // Parse query parameters
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var code = query["code"];
            var state = query["state"];

            // Validate required parameters
            if (string.IsNullOrEmpty(code))
            {
                logger.LogWarning("Missing code parameter");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Missing code parameter");
                return badRequestResponse;
            }

            if (string.IsNullOrEmpty(state))
            {
                logger.LogWarning("Missing state parameter");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Missing state parameter");
                return badRequestResponse;
            }

            logger.LogInformation("Processing callback with code and state");

            // Get signing secret
            var signingSecret = Environment.GetEnvironmentVariable("STATE_SIGNING_SECRET");
            if (string.IsNullOrEmpty(signingSecret))
            {
                logger.LogError("Missing STATE_SIGNING_SECRET environment variable");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                return errorResponse;
            }

            // Verify state signature
            if (!HmacHelper.VerifySignedState(state, signingSecret, out var discordUserId))
            {
                logger.LogWarning("Invalid state signature");
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid state");
                return badRequestResponse;
            }

            logger.LogInformation("Processing callback for Discord user: {UserId}", discordUserId);

            // Get required environment variables for token exchange
            var clientId = Environment.GetEnvironmentVariable("STRAVA_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("STRAVA_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                logger.LogError("Missing required Strava client credentials");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                return errorResponse;
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
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                return errorResponse;
            }

            var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();
            var tokenData = JsonSerializer.Deserialize<StravaTokenResponse>(tokenResponseContent);

            if (tokenData == null)
            {
                logger.LogError("Failed to parse token response");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                return errorResponse;
            }

            // Store user data
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

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync("Authorized with Strava");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Strava callback");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }
}
