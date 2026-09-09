using System.Text.Json.Serialization;

namespace ClinicApp.Api.Controllers;

/// <summary>Matches the { items, totalCount } shape the FE unwraps for bookings/staff/* and
/// bookings/me (booking.service.ts: `data?.items ?? []`, `data?.totalCount ?? items.length`).
/// Custom computed endpoints like this one are camelCase in this app (unlike the snake_case
/// table-passthrough resources) — same pattern as auth/* and bookings/doctor/today-summary.</summary>
public class PagedResult<T>
{
    [JsonPropertyName("items")] public List<T> Items { get; set; } = [];
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("pageSize")] public int PageSize { get; set; }
}
