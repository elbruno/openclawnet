using System.Text.Json;
using OpenClawNet.Storage;

namespace OpenClawNet.Gateway.Endpoints;

public static class StorageEndpoints
{
    public static void MapStorageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/storage").WithTags("Storage");

        // GET /api/storage/location
        group.MapGet("/location", (StorageOptions storage) =>
        {
            return Results.Ok(new StorageLocationResponse(
                RootPath: storage.RootPath,
                EffectivePath: storage.RootPath,
                AgentStoragePath: storage.AgentsPath
            ));
        })
        .WithName("GetStorageLocation")
        .WithDescription("Returns the current storage directory configuration");

        // PUT /api/storage/location
        group.MapPut("/location", async (StorageUpdateRequest request,
                                         IHostEnvironment hostEnvironment,
                                         ILogger<StorageOptions> logger) =>
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.RootPath))
                    return StorageUpdateError(StatusCodes.Status400BadRequest, "Root path cannot be empty");

                // Check if path is absolute
                if (!Path.IsPathRooted(request.RootPath))
                    return StorageUpdateError(StatusCodes.Status400BadRequest, "Path must be absolute");

                // Normalize path
                var normalizedPath = Path.GetFullPath(request.RootPath);

                // Validate not in system roots
                if (IsSystemPath(normalizedPath))
                    return StorageUpdateError(StatusCodes.Status400BadRequest, "Cannot use system directories (Windows, Program Files, System32, etc.)");

                // Check if we can create the directory
                try
                {
                    Directory.CreateDirectory(normalizedPath);
                }
                catch (UnauthorizedAccessException)
                {
                    return StorageUpdateError(StatusCodes.Status403Forbidden, "Permission denied: Cannot create or write to the specified path");
                }
                catch (IOException ex)
                {
                    return StorageUpdateError(StatusCodes.Status500InternalServerError, $"IO error: {ex.Message}");
                }

                // Test write permissions
                var testFilePath = Path.Combine(normalizedPath, $".openclawnet-test-{Guid.NewGuid()}.tmp");
                try
                {
                    await File.WriteAllTextAsync(testFilePath, "test");
                    File.Delete(testFilePath);
                }
                catch (UnauthorizedAccessException)
                {
                    return StorageUpdateError(StatusCodes.Status403Forbidden, "Permission denied: Path is not writable");
                }
                catch (IOException ex)
                {
                    return StorageUpdateError(StatusCodes.Status500InternalServerError, $"IO error while testing write permissions: {ex.Message}");
                }

                // Persist to appsettings.json
                var settingsPath = Path.Combine(hostEnvironment.ContentRootPath, "appsettings.json");
                var fallbackPath = Path.Combine(hostEnvironment.ContentRootPath, "storage-settings.json");
                
                // Try to update existing appsettings.json, or create storage-settings.json
                string configFileUsed;
                try
                {
                    configFileUsed = await UpdateConfigurationFile(settingsPath, normalizedPath, logger);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to update appsettings.json, using storage-settings.json");
                    configFileUsed = await CreateStorageSettingsFile(fallbackPath, normalizedPath);
                }

                return Results.Ok(new StorageUpdateResponse(
                    Success: true,
                    Message: $"Storage location updated to {normalizedPath}. Restart the service to apply changes. Configuration saved to {configFileUsed}",
                    NewPath: normalizedPath
                ));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating storage location");
                return StorageUpdateError(StatusCodes.Status500InternalServerError, $"Unexpected error: {ex.Message}");
            }
        })
        .WithName("UpdateStorageLocation")
        .WithDescription("Updates the storage root path (requires restart to take effect)");
    }

    private static IResult StorageUpdateError(int statusCode, string message) =>
        Results.Json(
            new StorageUpdateResponse(
                Success: false,
                Message: message,
                NewPath: null),
            statusCode: statusCode);

    /// <summary>
    /// Validates that a path is not in protected system directories
    /// </summary>
    private static bool IsSystemPath(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        var systemPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant(),
            "c:\\windows",
            "c:\\program files",
            "c:\\program files (x86)"
        };

        return systemPaths.Any(sysPath => !string.IsNullOrEmpty(sysPath) && 
                                         (lowerPath.Equals(sysPath, StringComparison.OrdinalIgnoreCase) ||
                                          lowerPath.StartsWith(sysPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Updates the existing appsettings.json file with new storage path
    /// </summary>
    private static async Task<string> UpdateConfigurationFile(string settingsPath, string newRootPath, ILogger logger)
    {
        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException("appsettings.json not found");
        }

        var json = await File.ReadAllTextAsync(settingsPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            // Copy all existing properties
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "Storage")
                {
                    // Update Storage section
                    writer.WriteStartObject("Storage");
                    
                    if (property.Value.TryGetProperty("BinaryFolderName", out var binaryFolder))
                        writer.WriteString("BinaryFolderName", binaryFolder.GetString());
                    else
                        writer.WriteString("BinaryFolderName", "binary");

                    if (property.Value.TryGetProperty("ModelsFolderName", out var modelsFolder))
                        writer.WriteString("ModelsFolderName", modelsFolder.GetString());
                    else
                        writer.WriteString("ModelsFolderName", "models");

                    if (property.Value.TryGetProperty("AgentsFolderName", out var agentsFolder))
                        writer.WriteString("AgentsFolderName", agentsFolder.GetString());
                    else
                        writer.WriteString("AgentsFolderName", "agents");

                    writer.WriteString("RootPath", newRootPath);
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            // If Storage section didn't exist, add it
            if (!root.TryGetProperty("Storage", out _))
            {
                writer.WriteStartObject("Storage");
                writer.WriteString("RootPath", newRootPath);
                writer.WriteString("BinaryFolderName", "binary");
                writer.WriteString("ModelsFolderName", "models");
                writer.WriteString("AgentsFolderName", "agents");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        var updatedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        await File.WriteAllTextAsync(settingsPath, updatedJson);
        
        logger.LogInformation("Updated storage configuration in {SettingsPath}", settingsPath);
        return settingsPath;
    }

    /// <summary>
    /// Creates a separate storage-settings.json file if appsettings.json cannot be updated
    /// </summary>
    private static async Task<string> CreateStorageSettingsFile(string fallbackPath, string newRootPath)
    {
        var settings = new
        {
            Storage = new
            {
                RootPath = newRootPath,
                BinaryFolderName = "binary",
                ModelsFolderName = "models",
                AgentsFolderName = "agents"
            }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fallbackPath, json);
        
        return fallbackPath;
    }
}

public sealed record StorageLocationResponse(
    string RootPath,
    string EffectivePath,
    string AgentStoragePath);

public sealed record StorageUpdateRequest(
    string RootPath);

public sealed record StorageUpdateResponse(
    bool Success,
    string Message,
    string? NewPath);
