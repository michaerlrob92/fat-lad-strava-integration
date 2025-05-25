using System.Collections.Concurrent;
using FLTL.StravaIntegration.Models;

namespace FLTL.StravaIntegration.Services;

/// <summary>
///     In-memory implementation of user data storage for fallback and testing scenarios.
///     Uses concurrent dictionaries to safely handle concurrent access.
/// </summary>
public class InMemoryUserDataStorage : IUserDataStorage
{
    private readonly ConcurrentDictionary<string, string> _discordIdByAthleteId = new();
    private readonly ConcurrentDictionary<string, StravaUserData> _userDataByDiscordId = new();

    public Task StoreUserDataAsync(string discordUserId, StravaUserData userData)
    {
        userData.Id = discordUserId;
        userData.UpdatedAt = DateTime.UtcNow;

        _userDataByDiscordId[discordUserId] = userData;
        _discordIdByAthleteId[userData.AthleteId] = discordUserId;

        return Task.CompletedTask;
    }

    public Task<StravaUserData?> GetUserDataAsync(string discordUserId)
    {
        var userData = _userDataByDiscordId.TryGetValue(discordUserId, out var data) ? data : null;
        return Task.FromResult(userData);
    }

    public Task<StravaUserData?> GetUserDataByAthleteIdAsync(string athleteId)
    {
        var discordId = _discordIdByAthleteId.TryGetValue(athleteId, out var id) ? id : null;
        if (string.IsNullOrEmpty(discordId)) return Task.FromResult<StravaUserData?>(null);

        var userData = _userDataByDiscordId.TryGetValue(discordId, out var data) ? data : null;
        return Task.FromResult(userData);
    }
}