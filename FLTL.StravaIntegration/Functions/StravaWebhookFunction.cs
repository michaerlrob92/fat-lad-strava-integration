using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FLTL.StravaIntegration.Models;
using FLTL.StravaIntegration.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FLTL.StravaIntegration.Functions;

public class StravaWebhookFunction(
    ILogger<StravaWebhookFunction> logger,
    StravaUserDataService userDataService,
    IHttpClientFactory factory)
{
    private readonly HttpClient _httpClient = factory.CreateClient();
      [Function("StravaWebhookFunction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "strava-webhook")]
        HttpRequestData req)    {
        try
        {
            return req.Method.ToUpper() switch
            {
                "GET" => await HandleVerification(req),
                "POST" => await HandleWebhookEvent(req),
                _ => await CreateBadRequestResponse(req, "Unsupported method")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing webhook request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }
    }

    private async Task<HttpResponseData> HandleVerification(HttpRequestData req)
    {
        logger.LogInformation("Processing webhook verification");

        // Parse query parameters
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var mode = query["hub.mode"];
        var verifyToken = query["hub.verify_token"];
        var challenge = query["hub.challenge"];

        // Validate required parameters
        if (string.IsNullOrEmpty(mode) || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge))
        {
            logger.LogWarning("Missing verification parameters");
            return await CreateBadRequestResponse(req, "Missing verification parameters");
        }

        // Get expected verify token from environment
        var expectedToken = Environment.GetEnvironmentVariable("STRAVA_VERIFY_TOKEN");
        if (string.IsNullOrEmpty(expectedToken))
        {
            logger.LogError("Missing STRAVA_VERIFY_TOKEN environment variable");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            return errorResponse;
        }

        // Verify the request
        if (mode == "subscribe" && verifyToken == expectedToken)
        {
            logger.LogInformation("Webhook verification successful, returning challenge");
            var response = req.CreateResponse(HttpStatusCode.OK);
            var verifyResponse = new StravaWebhookVerifyResponse { Challenge = challenge };
            await response.WriteAsJsonAsync(verifyResponse);
            return response;
        }

        logger.LogWarning("Webhook verification failed: mode={Mode}, token_match={TokenMatch}",
            mode, verifyToken == expectedToken);
        return await CreateBadRequestResponse(req, "Verification failed");
    }

    private async Task<HttpResponseData> HandleWebhookEvent(HttpRequestData req)
    {
        logger.LogInformation("Processing webhook event");

        // Read the request body
        var requestBody = await req.ReadAsStringAsync();

        if (string.IsNullOrEmpty(requestBody))
        {
            logger.LogWarning("Empty webhook payload");
            return await CreateBadRequestResponse(req, "Empty payload");
        }

        try
        {
            var webhookEvent = JsonSerializer.Deserialize<StravaWebhookEvent>(requestBody);
            if (webhookEvent == null)
            {
                logger.LogWarning("Failed to parse webhook payload");
                return await CreateBadRequestResponse(req, "Invalid payload");
            }

            logger.LogInformation("Received webhook: {AspectType} {ObjectType} for owner {OwnerId}",
                webhookEvent.AspectType, webhookEvent.ObjectType, webhookEvent.OwnerId);

            // Process activity creation events (fire-and-forget to avoid HttpContext disposal)
            if (webhookEvent.AspectType == "create" && webhookEvent.ObjectType == "activity")
            {
                try
                {
                    await ProcessActivityCreated(webhookEvent);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error in background processing of activity {ActivityId}", webhookEvent.ObjectId);
                }
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync("Event processed");
            return response;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse webhook JSON");
            return await CreateBadRequestResponse(req, "Invalid JSON");
        }
    }

    private async Task<HttpResponseData> CreateBadRequestResponse(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }

    private async Task ProcessActivityCreated(StravaWebhookEvent webhookEvent)
    {
        logger.LogInformation("Processing activity creation: Activity {ActivityId} by athlete {AthleteId}",
            webhookEvent.ObjectId, webhookEvent.OwnerId);

        // Look up Discord user by athlete ID and get valid tokens
        var userData = await userDataService.GetValidUserDataByAthleteIdAsync(webhookEvent.OwnerId.ToString());

        if (userData != null)
        {
            // Fetch activity details using the access token
            await FetchAndProcessActivity(userData, webhookEvent.ObjectId);
        }
    }

    private async Task FetchAndProcessActivity(StravaUserData userData, long activityId)
    {
        try
        {
            // Fetch activity details from Strava API
            var request =
                new HttpRequestMessage(HttpMethod.Get, $"https://www.strava.com/api/v3/activities/{activityId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userData.AccessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var activityJson = await response.Content.ReadAsStringAsync();
                logger.LogInformation("Successfully fetched activity {ActivityId} for user {DiscordUserId}",
                    activityId, userData.Id);

                // Process the activity data (send Discord notification, etc.)
                await SendDiscordNotification(userData.Id, activityId, activityJson);
            }
            else
            {
                logger.LogWarning("Failed to fetch activity {ActivityId}: {StatusCode}",
                    activityId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching activity {ActivityId} for user {DiscordUserId}",
                activityId, userData.Id);
        }
    }

    private async Task SendDiscordNotification(string discordUserId, long activityId, string? activityJson)
    {
        var discordWebhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
        if (string.IsNullOrEmpty(discordWebhookUrl))
        {
            logger.LogError("Missing DISCORD_WEBHOOK_URL environment variable");
            return;
        }
        
        if (string.IsNullOrEmpty(activityJson))
        {
            logger.LogWarning("Empty activity json");
            return;
        }
        
        var json = JsonSerializer.Deserialize<JsonNode>(activityJson);

        if (json == null)
        {
            logger.LogWarning("Failed to parse activity json");
            return;
        }
        
        logger.LogInformation("Sending Discord message for activity {ActivityId}", activityId);        // Extract activity details
        var activityName = json["name"]?.GetValue<string>() ?? "Untitled Activity";
        var activityType = json["type"]?.GetValue<string>() ?? "Activity";
        var sportType = json["sport_type"]?.GetValue<string>() ?? activityType;
        var distance = json["distance"]?.GetValue<double>() ?? 0;
        var movingTime = json["moving_time"]?.GetValue<int>() ?? 0;
        var elevationGain = json["total_elevation_gain"]?.GetValue<double>() ?? 0;
        var averageSpeed = json["average_speed"]?.GetValue<double>() ?? 0;
        var calories = json["calories"]?.GetValue<double>() ?? 0;
        var activityUrl = $"https://www.strava.com/activities/{activityId}";

        // Format time (seconds to HH:MM:SS)
        var timeSpan = TimeSpan.FromSeconds(movingTime);
        var formattedTime = $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";

        // Format distance (meters to km with 2 decimal places)
        var distanceKm = distance / 1000;

        // Format speed (m/s to km/h)
        var speedKmh = averageSpeed * 3.6;

        var messagePayload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"🚴 New {sportType}!",
                    url = activityUrl,
                    description = $"<@{discordUserId}> just completed **{activityName}**",
                    color = 16534530, // Strava orange
                    fields = new[]
                    {
                        new { name = "📏 Distance", value = $"{distanceKm:F2} km", inline = true },
                        new { name = "⏱️ Time", value = formattedTime, inline = true },
                        new { name = "⚡ Avg Speed", value = $"{speedKmh:F1} km/h", inline = true },
                        new { name = "⛰️ Elevation", value = $"{elevationGain:F0} m", inline = true },
                        new { name = "🔥 Calories", value = calories > 0 ? $"{calories:F0}" : "N/A", inline = true },
                        new { name = "🎯 Activity Type", value = sportType, inline = true }
                    },
                    footer = new
                    {
                        text = "Powered by Strava",
                        icon_url = "https://d3nn82uaxijpm6.cloudfront.net/assets/settings/badges/48-e988944d47ed60d661eb5075e0875e09021cfecfb2ba5a56fb4e5fd1041fce25.png"
                    },
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }
            }
        };

        try
        {
            // Serialize the payload to JSON
            var jsonPayload = JsonSerializer.Serialize(messagePayload);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            // Send the request to Discord webhook
            var response = await _httpClient.PostAsync(discordWebhookUrl, content);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Successfully sent Discord notification for activity {ActivityId}", activityId);
            }
            else
            {
                logger.LogWarning("Failed to send Discord notification for activity {ActivityId}: {StatusCode}",
                    activityId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending Discord notification for activity {ActivityId}", activityId);
        }
    }
}
