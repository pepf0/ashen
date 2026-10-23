using System.Text.Json.Serialization;

namespace Ashen;

public class TriggerCondition
{
    [JsonPropertyName("hp_below")]
    public double? HPBelow { get; set; }
    public int? EveryNHits { get; set; }
}
