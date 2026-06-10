using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ClockifyClient;
using Microsoft.Kiota.Abstractions;

namespace Clockify;

// Read-only Lookups für den Property Inspector. Baut pro Aufruf einen
// kurzlebigen Client und teilt KEINEN Zustand mit ClockifyService, damit
// PI-Abfragen das laufende Timer-Matching in OnTick nicht beeinflussen.
public class ClockifyLookupService(Logger logger)
{
    private const int MaxPageSize = 5000;

    public async Task<List<string>> GetWorkspacesAsync(string apiKey, string serverUrl)
    {
        var client = TryCreateClient(apiKey, serverUrl);
        if (client is null)
        {
            return [];
        }

        try
        {
            var workspaces = await client.V1.Workspaces.GetAsync();
            return LookupMapping.ToSortedNames(workspaces, w => w.Name);
        }
        catch (Exception e) when (e is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Lookup workspaces failed: {e.Message}");
            return [];
        }
    }

    public async Task<List<string>> GetClientsAsync(string apiKey, string serverUrl, string workspaceName)
    {
        var client = TryCreateClient(apiKey, serverUrl);
        if (client is null)
        {
            return [];
        }

        try
        {
            var workspaceId = await ResolveWorkspaceIdAsync(client, workspaceName);
            if (workspaceId is null)
            {
                return [];
            }

            var clients = await client.V1.Workspaces[workspaceId].Clients
                .GetAsync(q => q.QueryParameters.PageSize = MaxPageSize);
            return LookupMapping.ToSortedNames(clients, c => c.Name);
        }
        catch (Exception e) when (e is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Lookup clients failed: {e.Message}");
            return [];
        }
    }

    public async Task<List<string>> GetProjectsAsync(string apiKey, string serverUrl, string workspaceName, string clientName)
    {
        var client = TryCreateClient(apiKey, serverUrl);
        if (client is null)
        {
            return [];
        }

        try
        {
            var workspaceId = await ResolveWorkspaceIdAsync(client, workspaceName);
            if (workspaceId is null)
            {
                return [];
            }

            string clientId = null;
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                var clients = await client.V1.Workspaces[workspaceId].Clients
                    .GetAsync(q =>
                    {
                        q.QueryParameters.Name = clientName;
                        q.QueryParameters.PageSize = MaxPageSize;
                    });
                clientId = clients?.FirstOrDefault(c => c.Name == clientName)?.Id;
            }

            var projects = await client.V1.Workspaces[workspaceId].Projects
                .GetAsync(q =>
                {
                    q.QueryParameters.PageSize = MaxPageSize;
                    if (clientId is not null)
                    {
                        q.QueryParameters.Clients = [clientId];
                    }
                });
            return LookupMapping.ToSortedNames(projects, p => p.Name);
        }
        catch (Exception e) when (e is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Lookup projects failed: {e.Message}");
            return [];
        }
    }

    public async Task<List<string>> GetTasksAsync(string apiKey, string serverUrl, string workspaceName, string projectName)
    {
        var client = TryCreateClient(apiKey, serverUrl);
        if (client is null || string.IsNullOrWhiteSpace(projectName))
        {
            return [];
        }

        try
        {
            var workspaceId = await ResolveWorkspaceIdAsync(client, workspaceName);
            if (workspaceId is null)
            {
                return [];
            }

            var projects = await client.V1.Workspaces[workspaceId].Projects
                .GetAsync(q =>
                {
                    q.QueryParameters.Name = projectName;
                    q.QueryParameters.StrictNameSearch = true;
                    q.QueryParameters.PageSize = MaxPageSize;
                });
            var projectId = projects?.FirstOrDefault(p => p.Name == projectName)?.Id;
            if (projectId is null)
            {
                return [];
            }

            var tasks = await client.V1.Workspaces[workspaceId].Projects[projectId].Tasks
                .GetAsync(q => q.QueryParameters.PageSize = MaxPageSize);
            return LookupMapping.ToSortedNames(tasks, t => t.Name);
        }
        catch (Exception e) when (e is ApiException or HttpRequestException)
        {
            logger.LogWarn($"Lookup tasks failed: {e.Message}");
            return [];
        }
    }

    private static async Task<string> ResolveWorkspaceIdAsync(ClockifyApiClient client, string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            return null;
        }

        var workspaces = await client.V1.Workspaces.GetAsync();
        return workspaces?.FirstOrDefault(w => w.Name == workspaceName)?.Id;
    }

    private ClockifyApiClient TryCreateClient(string apiKey, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length != 48)
        {
            return null;
        }

        var url = string.IsNullOrWhiteSpace(serverUrl)
            ? "https://api.clockify.me/api"
            : serverUrl.Replace("/api/v1", "/api");

        if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            return null;
        }

        try
        {
            return ClockifyApiClientFactory.Create(apiKey, url);
        }
        catch (Exception e)
        {
            logger.LogWarn($"Lookup client creation failed: {e.Message}");
            return null;
        }
    }
}
