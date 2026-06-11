using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenClawNet.Models.Abstractions;

namespace OpenClawNet.Gateway.Services;

internal static class HostedAgentExportBundleBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static HostedAgentExportBundle Create(HostedAgentExportRequest request, IReadOnlyList<AgentProfile> profiles)
    {
        var normalizedPrefix = Slugify(request.NamePrefix);
        var manifest = new HostedAgentExportManifest(
            request.NamePrefix,
            request.Location,
            request.ContainerImage,
            request.ContainerPort,
            DateTime.UtcNow,
            profiles.Select(ToManifestProfile).ToArray());

        using var content = new MemoryStream();
        using (var zip = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(zip, "main.bicep", BuildMainBicep());
            AddText(zip, "main.parameters.json", BuildParametersJson(request, profiles));
            AddText(zip, "profiles.json", JsonSerializer.Serialize(manifest, JsonOptions));
            AddText(zip, "README.md", BuildReadme(normalizedPrefix));
        }

        return new HostedAgentExportBundle(
            $"{normalizedPrefix}-hosted-agent-export.zip",
            content.ToArray());
    }

    private static string BuildMainBicep() =>
        $$"""
        targetScope = 'resourceGroup'

        param namePrefix string
        param location string = resourceGroup().location
        param containerImage string
        param containerPort int = 8080
        param profiles array

        var normalizedPrefix = toLower(replace(replace(namePrefix, ' ', '-'), '_', '-'))
        var logAnalyticsName = '${normalizedPrefix}-law'
        var environmentName = '${normalizedPrefix}-env'

        resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
          name: logAnalyticsName
          location: location
          properties: {
            sku: {
              name: 'PerGB2018'
            }
            retentionInDays: 30
          }
        }

        resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
          name: environmentName
          location: location
          identity: {
            type: 'SystemAssigned'
          }
          properties: {
            appLogsConfiguration: {
              destination: 'log-analytics'
              logAnalyticsConfiguration: {
                customerId: logAnalytics.properties.customerId
                sharedKey: logAnalytics.listKeys().primarySharedKey
              }
            }
          }
          dependsOn: [
            logAnalytics
          ]
        }

        resource agentApps 'Microsoft.App/containerApps@2024-03-01' = [for profile in profiles: {
          name: '${normalizedPrefix}-${toLower(replace(profile.name, ' ', '-'))}'
          location: location
          identity: {
            type: 'SystemAssigned'
          }
          properties: {
            managedEnvironmentId: environment.id
            configuration: {
              ingress: {
                external: true
                targetPort: containerPort
                transport: 'auto'
              }
            }
            template: {
              containers: [
                {
                  name: 'agent'
                  image: containerImage
                  env: [
                    {
                      name: 'OPENCLAWNET_PROFILE_NAME'
                      value: profile.name
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_DISPLAY_NAME'
                      value: profile.displayName
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_PROVIDER'
                      value: profile.provider
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_MODEL'
                      value: profile.model
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_KIND'
                      value: profile.kind
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_RETRIEVAL_LEVEL'
                      value: profile.retrievalLevel
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_REQUIRE_TOOL_APPROVAL'
                      value: string(profile.requireToolApproval)
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_ENABLED_TOOLS'
                      value: join(profile.enabledTools, ',')
                    }
                    {
                      name: 'OPENCLAWNET_PROFILE_INSTRUCTIONS'
                      value: profile.instructions
                    }
                  ]
                }
              ]
            }
          }
          dependsOn: [
            environment
          ]
        }]

        output hostedAgentNames array = [for profile in profiles: profile.name]
        output hostedAgentEnvironment string = environment.name
        output hostedAgentWorkspace string = logAnalytics.name
        """;

    private static string BuildParametersJson(HostedAgentExportRequest request, IReadOnlyList<AgentProfile> profiles)
    {
        var payload = new HostedAgentDeploymentParametersFile(
            "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
            "1.0.0.0",
            new HostedAgentDeploymentParameters(
                new HostedAgentParameterValue(request.NamePrefix),
                new HostedAgentParameterValue(request.Location),
                new HostedAgentParameterValue(request.ContainerImage),
                new HostedAgentParameterValue(request.ContainerPort),
                new HostedAgentParameterValue(profiles.Select(ToParameterProfile).ToArray())));

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildReadme(string normalizedPrefix) =>
        $$"""
        # Hosted Agent export

        This bundle was generated for the OpenClaw .NET hosted-agent flow.

        ## What is included

        - `main.bicep` — Azure Container Apps deployment for the selected agents
        - `main.parameters.json` — parameter values for the current export
        - `profiles.json` — human-readable manifest of the exported profiles

        ## Deploy

        ```powershell
        az group create --name <resource-group> --location <azure-region>
        az deployment group create \
          --resource-group <resource-group> \
          --template-file main.bicep \
          --parameters @main.parameters.json
        ```

        ## Notes

        - Replace the container image parameter with a published image for the hosted agent runtime.
        - The prefix `{{normalizedPrefix}}` is used to keep the Azure resource names consistent.
        """;

    private static HostedAgentManifestProfile ToManifestProfile(AgentProfile profile) =>
        new(
            profile.Name,
            profile.DisplayName,
            profile.Provider,
            profile.Model,
            profile.Kind.ToString(),
            profile.Instructions,
            SplitTools(profile.EnabledTools),
            profile.Temperature,
            profile.MaxTokens,
            profile.RequireToolApproval,
            profile.RetrievalLevel.ToString());

    private static object ToParameterProfile(AgentProfile profile) => new
    {
        name = profile.Name,
        displayName = profile.DisplayName ?? string.Empty,
        provider = profile.Provider ?? string.Empty,
        model = profile.Model ?? string.Empty,
        kind = profile.Kind.ToString(),
        retrievalLevel = profile.RetrievalLevel.ToString(),
        requireToolApproval = profile.RequireToolApproval,
        instructions = profile.Instructions ?? string.Empty,
        enabledTools = SplitTools(profile.EnabledTools),
        temperature = profile.Temperature,
        maxTokens = profile.MaxTokens
    };

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: false);
        writer.Write(content);
    }

    private static IReadOnlyList<string> SplitTools(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Slugify(string value)
    {
        var slug = new string(value.Trim().ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) ? ch : '-').ToArray());

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "openclaw-hosted" : slug;
    }
}

internal sealed record HostedAgentExportBundle(string FileName, byte[] Content);

internal sealed record HostedAgentExportRequest(
    IReadOnlyList<string> ProfileNames,
    string NamePrefix,
    string Location,
    string ContainerImage,
    int ContainerPort);

internal sealed record HostedAgentExportManifest(
    string NamePrefix,
    string Location,
    string ContainerImage,
    int ContainerPort,
    DateTime ExportedAtUtc,
    HostedAgentManifestProfile[] Profiles);

internal sealed record HostedAgentManifestProfile(
    string Name,
    string? DisplayName,
    string? Provider,
    string? Model,
    string Kind,
    string? Instructions,
    IReadOnlyList<string> EnabledTools,
    double? Temperature,
    int? MaxTokens,
    bool RequireToolApproval,
    string RetrievalLevel);

internal sealed record HostedAgentDeploymentParametersFile(
    [property: JsonPropertyName("$schema")] string Schema,
    [property: JsonPropertyName("contentVersion")] string ContentVersion,
    [property: JsonPropertyName("parameters")] HostedAgentDeploymentParameters Parameters);

internal sealed record HostedAgentDeploymentParameters(
    [property: JsonPropertyName("namePrefix")] HostedAgentParameterValue NamePrefix,
    [property: JsonPropertyName("location")] HostedAgentParameterValue Location,
    [property: JsonPropertyName("containerImage")] HostedAgentParameterValue ContainerImage,
    [property: JsonPropertyName("containerPort")] HostedAgentParameterValue ContainerPort,
    [property: JsonPropertyName("profiles")] HostedAgentParameterValue Profiles);

internal sealed record HostedAgentParameterValue([property: JsonPropertyName("value")] object Value);
