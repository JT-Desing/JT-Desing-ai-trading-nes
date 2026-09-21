namespace NesTrading.Core.Agents.Trade
{
    public static class TradeAgentSchema
    {
        public const string JsonSchema = @"{
  ""type"":""object"",
  ""additionalProperties"":false,
  ""required"":[""schema_version"",""symbol"",""decision"",""confidence"",""generated_at_utc"",""valid_until_utc"",""reason_codes""],
  ""properties"":{
    ""schema_version"":{""type"":""string"",""const"":""trade.v1""},
    ""symbol"":{""type"":""string"",""const"":""NES""},
    ""decision"":{""type"":""string"",""enum"":[""LONG"",""SHORT"",""WAIT"",""EXIT_RECOMMENDATION""]},
    ""confidence"":{""type"":""number"",""minimum"":0,""maximum"":1},
    ""generated_at_utc"":{""type"":""string""},
    ""valid_until_utc"":{""type"":""string""},
    ""reason_codes"":{""type"":""array"",""items"":{""type"":""string""}}
  }
}";
    }
}
