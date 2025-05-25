using System.Text.Json;
using FLTL.StravaIntegration.Models;
using Microsoft.Extensions.Logging;

namespace FLTL.StravaIntegration.Services;

public class StravaUserDataService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StravaUserDataService> _logger;
    private readonly IUserDataStorage _userDataStorage;

    public StravaUserDataService(
        IUserDataStorage userDataStorage,
        HttpClient httpClient,
        ILogger<StravaUserDataService> logger)
    {
        _userDataStorage = userDataStorage;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<StravaUserData?> GetUserDataAsync(string discordUserId)
    {
        return await _userDataStorage.GetUserDataAsync(discordUserId);
    }

    public async Task<StravaUserData?> GetUserDataByAthleteIdAsync(string athleteId)
    {
        // Optimized: Use single database call instead of two
        return await _userDataStorage.GetUserDataByAthleteIdAsync(athleteId);
    }

    public async Task StoreUserDataAsync(string discordUserId, StravaUserData userData)
    {
        await _userDataStorage.StoreUserDataAsync(discordUserId, userData);
    }

    public async Task<StravaUserData?> GetValidUserDataAsync(string discordUserId)
    {
        var userData = await _userDataStorage.GetUserDataAsync(discordUserId);
        if (userData == null) return null;

        // Check if token is expired (with 5-minute buffer)
        var expirationTime = DateTimeOffset.FromUnixTimeSeconds(userData.ExpiresAt);
        var bufferTime = DateTime.UtcNow.AddMinutes(5);

        if (expirationTime <= bufferTime)
        {
            _logger.LogInformation("Access token for user {DiscordUserId} is expired or expiring soon, refreshing...",
                discordUserId);

            var refreshedUserData = await RefreshTokenAsync(userData);
            if (refreshedUserData != null)
            {
                await _userDataStorage.StoreUserDataAsync(discordUserId, refreshedUserData);
                return refreshedUserData;
            }

            _logger.LogWarning("Failed to refresh token for user {DiscordUserId}", discordUserId);
            return userData; // Return original data even if refresh failed
        }

        return userData;
    }

    public async Task<StravaUserData?> GetValidUserDataByAthleteIdAsync(string athleteId)
    {
        // Optimized: Get user data directly instead of two separate calls
        var userData = await _userDataStorage.GetUserDataByAthleteIdAsync(athleteId);
        if (userData == null) return null;

        // Check if token is expired (with 5-minute buffer)
        var expirationTime = DateTimeOffset.FromUnixTimeSeconds(userData.ExpiresAt);
        var bufferTime = DateTime.UtcNow.AddMinutes(5);

        if (expirationTime <= bufferTime)
        {
            _logger.LogInformation("Access token for user {DiscordUserId} is expired or expiring soon, refreshing...",
                userData.Id);

            var refreshedUserData = await RefreshTokenAsync(userData);
            if (refreshedUserData != null)
            {
                await _userDataStorage.StoreUserDataAsync(userData.Id, refreshedUserData);
                return refreshedUserData;
            }

            _logger.LogWarning("Failed to refresh token for user {DiscordUserId}", userData.Id);
            return userData; // Return original data even if refresh failed
        }

        return userData;
    }

    private async Task<StravaUserData?> RefreshTokenAsync(StravaUserData userData)
    {
        try
        {
            var clientId = Environment.GetEnvironmentVariable("STRAVA_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("STRAVA_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Missing Strava client credentials for token refresh");
                return null;
            }

            var refreshRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("refresh_token", userData.RefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            var response = await _httpClient.PostAsync("https://www.strava.com/oauth/token", refreshRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Token refresh failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<StravaTokenResponse>(responseContent);

            if (tokenResponse == null)
            {
                _logger.LogError("Failed to parse token refresh response");
                return null;
            }

            // Update the user data with new tokens
            userData.AccessToken = tokenResponse.AccessToken;
            userData.RefreshToken = tokenResponse.RefreshToken;
            userData.ExpiresAt = tokenResponse.ExpiresAt;
            userData.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("Successfully refreshed token for user {DiscordUserId}", userData.Id);

            return userData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refreshing token for user {DiscordUserId}", userData.Id);
            return null;
        }
    }
}