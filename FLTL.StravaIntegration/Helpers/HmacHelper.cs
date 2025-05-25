using System.Security.Cryptography;
using System.Text;

namespace FLTL.StravaIntegration.Helpers;

public static class HmacHelper
{
    public static string GenerateSignedState(string discordUserId, string secretKey)
    {
        var message = discordUserId;
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        var signature = Convert.ToBase64String(hashBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='); // Base64URL encoding

        return $"{discordUserId}:{signature}";
    }

    public static bool VerifySignedState(string signedState, string secretKey, out string discordUserId)
    {
        discordUserId = string.Empty;

        try
        {
            var parts = signedState.Split(':');
            if (parts.Length != 2)
                return false;

            var userId = parts[0];
            var providedSignature = parts[1];

            // Generate expected signature
            var expectedSignedState = GenerateSignedState(userId, secretKey);
            var expectedParts = expectedSignedState.Split(':');

            if (expectedParts.Length != 2)
                return false;

            var expectedSignature = expectedParts[1];

            // Compare signatures using constant-time comparison
            if (providedSignature == expectedSignature)
            {
                discordUserId = userId;
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}