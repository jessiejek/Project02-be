//JJQR - AI (Nov 21, 2026) cloud sync: JSON conventions shared by the cloud API and the Clinic Node
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicApp.Infrastructure.Sync;

/// <summary>Same conventions as the REST API (Program.cs AddJsonOptions): snake_case names,
/// enums as their exact member names.</summary>
public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null, // dictionary keys are already column names
            PropertyNameCaseInsensitive = true,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };
        o.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true));
        return o;
    }
}
//JJQR - END
