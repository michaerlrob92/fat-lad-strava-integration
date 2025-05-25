using FLTL.StravaIntegration.Models;

namespace FLTL.StravaIntegration.Services;

/// <summary>
///     Simplified interface for user data storage focused on core operations.
///     Stores and retrieves Strava user data associated with Discord user IDs.
/// </summary>
public interface IUserDataStorage
{
    /// <summary>
    ///     Stores or updates user data for a Discord user.
    /// </summary>
    /// <param name="discordUserId">The Discord user ID (used as partition key)</param>
    /// <param name="userData">The Strava user data to store</param>
    Task StoreUserDataAsync(string discordUserId, StravaUserData userData);

    /// <summary>
    ///     Retrieves user data by Discord user ID.
    /// </summary>
    /// <param name="discordUserId">The Discord user ID</param>
    /// <returns>The user data or null if not found</returns>
    Task<StravaUserData?> GetUserDataAsync(string discordUserId);

    /// <summary>
    ///     Retrieves user data by Strava athlete ID.
    ///     This is the optimized method that performs a single database query.
    /// </summary>
    /// <param name="athleteId">The Strava athlete ID</param>
    /// <returns>The user data or null if not found</returns>
    Task<StravaUserData?> GetUserDataByAthleteIdAsync(string athleteId);
}