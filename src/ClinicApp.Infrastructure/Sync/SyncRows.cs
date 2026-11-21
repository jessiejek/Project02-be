//JJQR - AI (Nov 21, 2026) cloud sync: entity <-> snake_case row JSON using EF metadata (column names)
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicApp.Infrastructure.Sync;

public static class SyncRows
{
    /// <summary>Columns the server owns; never taken from a pushed row.</summary>
    public static readonly IReadOnlySet<string> ServerColumns = new HashSet<string> { "row_version", "deleted_at", "origin_node_id" };

    /// <summary>Wire name of a column: the REST API's JSON name (snake_case of the property,
    /// e.g. <c>icd10_code</c>). The physical column name (e.g. <c>icd10code</c>) is accepted on input too.</summary>
    public static string Name(IProperty p) => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);

    private static IEntityType Type(DbContext db, Type clr) =>
        db.Model.FindEntityType(clr) ?? throw new InvalidOperationException($"{clr.Name} is not mapped.");

    private static IEnumerable<IProperty> Columns(IEntityType t) =>
        t.GetProperties().Where(p => p.PropertyInfo is not null);

    /// <summary>Scalar columns of an entity as { column_name: value }.</summary>
    public static Dictionary<string, object?> ToRow(DbContext db, object entity)
    {
        var t = Type(db, entity.GetType());
        var row = new Dictionary<string, object?>();
        foreach (var p in Columns(t))
            row[Name(p)] = p.PropertyInfo!.GetValue(entity);
        return row;
    }

    /// <summary>Values present in <paramref name="data"/> for columns of <paramref name="clr"/>.</summary>
    public static Dictionary<IProperty, object?> Read(DbContext db, Type clr, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw new SyncPayloadException("Row must be a JSON object.");
        var t = Type(db, clr);
        var byColumn = new Dictionary<string, IProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Columns(t)) { byColumn.TryAdd(Name(c), c); byColumn.TryAdd(c.GetColumnName(), c); }
        var values = new Dictionary<IProperty, object?>();
        foreach (var prop in data.EnumerateObject())
        {
            if (!byColumn.TryGetValue(prop.Name, out var p)) continue; // unknown / navigation keys are ignored
            try
            {
                values[p] = prop.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : prop.Value.Deserialize(p.ClrType, SyncJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                throw new SyncPayloadException($"Invalid value for '{prop.Name}': {ex.Message}");
            }
            if (values[p] is null && !p.IsNullable)
                throw new SyncPayloadException($"'{prop.Name}' cannot be null.");
        }
        return values;
    }

    /// <summary>Copy values onto an entity, skipping server columns and anything in <paramref name="skip"/>.</summary>
    public static void Set(object entity, Dictionary<IProperty, object?> values, Func<string, bool>? include = null)
    {
        foreach (var (p, v) in values)
        {
            var col = Name(p);
            if (ServerColumns.Contains(col)) continue;
            if (include is not null && !include(col)) continue;
            p.PropertyInfo!.SetValue(entity, v);
        }
    }

    public static object? Get(object entity, IProperty p) => p.PropertyInfo!.GetValue(entity);

    public static bool SameValue(object? a, object? b) => Equals(a, b);

    /// <summary>Build a fresh entity from a row.</summary>
    public static T New<T>(DbContext db, JsonElement data) where T : class, new()
    {
        var e = new T();
        Set(e, Read(db, typeof(T), data));
        return e;
    }
}

public sealed class SyncPayloadException(string message) : Exception(message);
//JJQR - END
