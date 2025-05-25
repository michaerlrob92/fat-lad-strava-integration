using System.Text.Json.Serialization;

namespace FLTL.StravaIntegration.Models;

public class StravaWebhookVerifyResponse
{
    [JsonPropertyName("hub.challenge")]
    public string? Challenge { get; set; }
}

public class StravaTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }

    [JsonPropertyName("athlete")] public StravaAthlete Athlete { get; set; } = new();
}

public class StravaAthlete
{
    [JsonPropertyName("id")] public long Id { get; set; }

    [JsonPropertyName("firstname")] public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastname")] public string LastName { get; set; } = string.Empty;
}

public class StravaUserData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty; // Discord user ID (partition key)
    
    [JsonPropertyName("athleteId")]
    public string AthleteId { get; set; } = string.Empty;
    
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
    
    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }
    
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class StravaWebhookEvent
{
    [JsonPropertyName("aspect_type")] public string AspectType { get; set; } = string.Empty;

    [JsonPropertyName("event_time")] public long EventTime { get; set; }

    [JsonPropertyName("object_id")] public long ObjectId { get; set; }

    [JsonPropertyName("object_type")] public string ObjectType { get; set; } = string.Empty;

    [JsonPropertyName("owner_id")] public long OwnerId { get; set; }

    [JsonPropertyName("subscription_id")] public long SubscriptionId { get; set; }
}