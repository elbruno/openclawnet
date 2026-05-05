using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using OpenClawNet.Web.Models.Audit;

namespace OpenClawNet.Web.Components.Pages;

/// <summary>
/// Code-behind for AuditHistory page.
/// Provides three tabs: Job State Changes, Tool Approvals, and Adapter Deliveries.
/// </summary>
public partial class AuditHistory
{
    // Tab component classes defined here for separation of concerns
}

/// <summary>
/// Tab component for Job State Changes
/// </summary>
public class JobStateChangesTab : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected List<AuditJobStateChangeDto> _changes = [];
    protected bool _loading = true;
    protected string? _error;
    protected bool _dense = true;
    protected DateTime? _sinceFilter;
    protected DateTime? _untilFilter;
    protected string? _jobIdFilter;
    protected int _currentPage = 0;
    protected int _pageSize = 100;
    protected int _totalCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    protected async Task LoadDataAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var client = HttpClientFactory.CreateClient("gateway");
            var queryParams = new List<string>
            {
                $"limit={_pageSize}",
                $"offset={_currentPage * _pageSize}"
            };

            if (_sinceFilter.HasValue)
                queryParams.Add($"since={_sinceFilter.Value:yyyy-MM-dd}");
            
            if (_untilFilter.HasValue)
                queryParams.Add($"until={_untilFilter.Value:yyyy-MM-dd}");

            if (!string.IsNullOrWhiteSpace(_jobIdFilter) && Guid.TryParse(_jobIdFilter, out var jobId))
                queryParams.Add($"jobId={jobId}");

            var queryString = string.Join("&", queryParams);
            var response = await client.GetAsync($"api/audit/job-state-changes?{queryString}");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AuditJobStateChangesResponse>();
            _changes = result?.Changes ?? [];
            _totalCount = result?.Count ?? 0;
        }
        catch (Exception ex)
        {
            _error = $"Failed to load job state changes: {ex.Message}";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    protected async Task OnPageChanged(int page)
    {
        _currentPage = page;
        await LoadDataAsync();
    }

    protected async Task OnFilterChanged()
    {
        _currentPage = 0;
        await LoadDataAsync();
    }

    protected static MudBlazor.Color GetStatusChipColor(string? status) => status?.ToLowerInvariant() switch
    {
        "draft" => MudBlazor.Color.Secondary,
        "active" => MudBlazor.Color.Success,
        "paused" => MudBlazor.Color.Warning,
        "cancelled" => MudBlazor.Color.Error,
        "completed" => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default
    };
}

/// <summary>
/// Tab component for Tool Approvals
/// </summary>
public class ToolApprovalsTab : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected List<AuditToolApprovalLogDto> _logs = [];
    protected bool _loading = true;
    protected string? _error;
    protected bool _dense = true;
    protected DateTime? _sinceFilter;
    protected DateTime? _untilFilter;
    protected string? _toolNameFilter;
    protected string? _sessionIdFilter;
    protected bool? _approvedFilter;
    protected string? _sourceFilter;
    protected int _currentPage = 0;
    protected int _pageSize = 100;
    protected int _totalCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    protected async Task LoadDataAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var client = HttpClientFactory.CreateClient("gateway");
            var queryParams = new List<string>
            {
                $"limit={_pageSize}",
                $"offset={_currentPage * _pageSize}"
            };

            if (_sinceFilter.HasValue)
                queryParams.Add($"since={_sinceFilter.Value:yyyy-MM-dd}");
            
            if (_untilFilter.HasValue)
                queryParams.Add($"until={_untilFilter.Value:yyyy-MM-dd}");

            if (!string.IsNullOrWhiteSpace(_toolNameFilter))
                queryParams.Add($"toolName={Uri.EscapeDataString(_toolNameFilter)}");

            if (!string.IsNullOrWhiteSpace(_sessionIdFilter) && Guid.TryParse(_sessionIdFilter, out var sessionId))
                queryParams.Add($"sessionId={sessionId}");

            var queryString = string.Join("&", queryParams);
            var response = await client.GetAsync($"api/audit/tool-approvals?{queryString}");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AuditToolApprovalLogsResponse>();
            _logs = result?.Logs ?? [];
            
            // Apply client-side filters for approved and source
            if (_approvedFilter.HasValue)
                _logs = _logs.Where(l => l.Approved == _approvedFilter.Value).ToList();
            
            if (!string.IsNullOrWhiteSpace(_sourceFilter))
                _logs = _logs.Where(l => l.Source.Equals(_sourceFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            _totalCount = _logs.Count;
        }
        catch (Exception ex)
        {
            _error = $"Failed to load tool approvals: {ex.Message}";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    protected async Task OnPageChanged(int page)
    {
        _currentPage = page;
        await LoadDataAsync();
    }

    protected async Task OnFilterChanged()
    {
        _currentPage = 0;
        await LoadDataAsync();
    }

    protected static MudBlazor.Color GetApprovalChipColor(bool approved) => 
        approved ? MudBlazor.Color.Success : MudBlazor.Color.Error;
    
    protected static MudBlazor.Color GetSourceChipColor(string? source) => source?.ToLowerInvariant() switch
    {
        "user" => MudBlazor.Color.Primary,
        "timeout" => MudBlazor.Color.Warning,
        "sessionmemory" => MudBlazor.Color.Info,
        _ => MudBlazor.Color.Default
    };
}

/// <summary>
/// Tab component for Adapter Deliveries
/// </summary>
public class AdapterDeliveriesTab : ComponentBase
{
    [Inject] protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected List<AdapterDeliveryLogDto> _logs = [];
    protected bool _loading = true;
    protected string? _error;
    protected bool _dense = true;
    protected DateTime? _sinceFilter;
    protected DateTime? _untilFilter;
    protected string? _jobIdFilter;
    protected string? _statusFilter;
    protected string? _channelTypeFilter;
    protected int _currentPage = 0;
    protected int _pageSize = 100;
    protected int _totalCount = 0;

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    protected async Task LoadDataAsync()
    {
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var client = HttpClientFactory.CreateClient("gateway");
            var queryParams = new List<string>
            {
                $"limit={_pageSize}",
                $"offset={_currentPage * _pageSize}"
            };

            if (_sinceFilter.HasValue)
                queryParams.Add($"since={_sinceFilter.Value:yyyy-MM-dd}");
            
            if (_untilFilter.HasValue)
                queryParams.Add($"until={_untilFilter.Value:yyyy-MM-dd}");

            if (!string.IsNullOrWhiteSpace(_jobIdFilter) && Guid.TryParse(_jobIdFilter, out var jobId))
                queryParams.Add($"jobId={jobId}");

            if (!string.IsNullOrWhiteSpace(_statusFilter))
                queryParams.Add($"status={Uri.EscapeDataString(_statusFilter)}");

            var queryString = string.Join("&", queryParams);
            var response = await client.GetAsync($"api/audit/adapter-deliveries?{queryString}");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AdapterDeliveryLogsResponse>();
            _logs = result?.Logs ?? [];

            // Apply client-side filter for channel type
            if (!string.IsNullOrWhiteSpace(_channelTypeFilter))
                _logs = _logs.Where(l => l.ChannelType.Contains(_channelTypeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            _totalCount = _logs.Count;
        }
        catch (Exception ex)
        {
            _error = $"Failed to load adapter deliveries: {ex.Message}";
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    protected async Task OnPageChanged(int page)
    {
        _currentPage = page;
        await LoadDataAsync();
    }

    protected async Task OnFilterChanged()
    {
        _currentPage = 0;
        await LoadDataAsync();
    }

    protected static MudBlazor.Color GetStatusChipColor(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" => MudBlazor.Color.Warning,
        "success" => MudBlazor.Color.Success,
        "failed" => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default
    };
}
