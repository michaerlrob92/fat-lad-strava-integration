using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FLTL.StravaIntegration.Models;
using FLTL.StravaIntegration.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FLTL.StravaIntegration.Functions;

public class StravaWebhookFunction(
    ILogger<StravaWebhookFunction> logger,
    StravaUserDataService userDataService,
    IHttpClientFactory factory)
{
    private readonly HttpClient _httpClient = factory.CreateClient();
    
    [Function("StravaWebhookFunction")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "strava-webhook")]
        HttpRequest req)
    {
        try
        {
            return req.Method switch
            {
                "GET" => HandleVerification(req),
                "POST" => await HandleWebhookEvent(req),
                _ => new BadRequestObjectResult("Unsupported method")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing webhook request");
            return new StatusCodeResult(500);
        }
    }

    private IActionResult HandleVerification(HttpRequest req)
    {
        logger.LogInformation("Processing webhook verification");

        // Get verification parameters
        if (!req.Query.TryGetValue("hub.mode", out var modeValues) ||
            !req.Query.TryGetValue("hub.verify_token", out var tokenValues) ||
            !req.Query.TryGetValue("hub.challenge", out var challengeValues))
        {
            logger.LogWarning("Missing verification parameters");
            return new BadRequestObjectResult("Missing verification parameters");
        }

        var mode = modeValues.FirstOrDefault();
        var verifyToken = tokenValues.FirstOrDefault();
        var challenge = challengeValues.FirstOrDefault();

        // Get expected verify token from environment
        var expectedToken = Environment.GetEnvironmentVariable("STRAVA_VERIFY_TOKEN");
        if (string.IsNullOrEmpty(expectedToken))
        {
            logger.LogError("Missing STRAVA_VERIFY_TOKEN environment variable");
            return new StatusCodeResult(500);
        } // Verify the request

        if (mode == "subscribe" && verifyToken == expectedToken)
        {
            logger.LogInformation("Webhook verification successful, returning challenge");
            return new JsonResult(new StravaWebhookVerifyResponse { Challenge = challenge });
        }

        logger.LogWarning("Webhook verification failed: mode={Mode}, token_match={TokenMatch}",
            mode, verifyToken == expectedToken);
        return new BadRequestObjectResult("Verification failed");
    }

    private async Task<IActionResult> HandleWebhookEvent(HttpRequest req)
    {
        logger.LogInformation("Processing webhook event");

        // Read the request body
        using var reader = new StreamReader(req.Body);
        var requestBody = await reader.ReadToEndAsync();

        if (string.IsNullOrEmpty(requestBody))
        {
            logger.LogWarning("Empty webhook payload");
            return new BadRequestObjectResult("Empty payload");
        }

        try
        {
            var webhookEvent = JsonSerializer.Deserialize<StravaWebhookEvent>(requestBody);
            if (webhookEvent == null)
            {
                logger.LogWarning("Failed to parse webhook payload");
                return new BadRequestObjectResult("Invalid payload");
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

            return new OkObjectResult("Event processed");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse webhook JSON");
            return new BadRequestObjectResult("Invalid JSON");
        }
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
            _ = Task.Run(async () =>
            {
                await FetchAndProcessActivity(userData, webhookEvent.ObjectId);
            });
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
                        icon_url = "https://camo.githubusercontent.com/cf95bc20ee9b22b2fb50a827a70ab0390f64b582975531abf7588ac190ef1869/68747470733a2f2f6564656e742e6769746875622e696f2f537570657254696e7949636f6e732f696d616765732f7376672f7374726176612e737667"
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
