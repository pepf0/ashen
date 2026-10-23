using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ashen;

public static class Protocol
{
    /// <summary>JSON options for everything sent to the browser: camelCase, enums as camelCase strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
}
