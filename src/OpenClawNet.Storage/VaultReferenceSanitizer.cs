namespace OpenClawNet.Storage;

public static class VaultReferenceSanitizer
{
    public const string RedactedReferenceDisplay = "[vault-backed]";
    private const string SecureConfigurationFailureMessage =
        "Secure configuration could not be resolved. Ensure the required secret exists and is accessible.";

    public static bool IsVaultReference(string? value) =>
        VaultConfigurationResolver.TryParseVaultReference(value, out _);

    public static string? SanitizeReferenceForDisplay(string? value) =>
        IsVaultReference(value) ? RedactedReferenceDisplay : value;

    public static string? PreserveExistingReference(string? requestedValue, string? existingValue)
    {
        if (!string.IsNullOrWhiteSpace(requestedValue))
            return requestedValue;

        return IsVaultReference(existingValue) ? existingValue : requestedValue;
    }

    public static string BuildMissingSecretMessage(string fieldName) =>
        $"Failed to resolve secure configuration for {fieldName}. Ensure the required secret exists and is accessible.";

    public static string? SanitizeFailureMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        return LooksLikeVaultFailure(message)
            ? SecureConfigurationFailureMessage
            : message;
    }

    private static bool LooksLikeVaultFailure(string message) =>
        message.Contains("vault://", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Vault secret resolution failed", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Failed to resolve vault reference", StringComparison.OrdinalIgnoreCase) ||
        (message.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
         message.Contains("not found", StringComparison.OrdinalIgnoreCase));
}
