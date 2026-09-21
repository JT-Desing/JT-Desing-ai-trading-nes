namespace NesTrading.Core.Agents.News
{
    public static class NewsAgentSchema
    {
        public const string JsonSchema =
            "{" +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"schema_version\",\"symbol\",\"state\",\"directional_bias\",\"importance\",\"event_code\",\"generated_at_utc\",\"valid_until_utc\",\"confidence\",\"reason_codes\"]," +
            "\"properties\":{" +
                "\"schema_version\":{\"type\":\"string\",\"enum\":[\"news.v1\"]}," +
                "\"symbol\":{\"type\":\"string\",\"enum\":[\"NES\"]}," +
                "\"state\":{\"type\":\"string\",\"enum\":[\"ALLOW\",\"CAUTION\",\"BLOCK\"]}," +
                "\"directional_bias\":{\"type\":\"string\",\"enum\":[\"BULLISH\",\"BEARISH\",\"NEUTRAL\",\"UNKNOWN\"]}," +
                "\"importance\":{\"type\":\"string\",\"enum\":[\"LOW\",\"MEDIUM\",\"HIGH\",\"CRITICAL\"]}," +
                "\"event_code\":{\"type\":\"string\",\"minLength\":1}," +
                "\"generated_at_utc\":{\"type\":\"string\",\"format\":\"date-time\"}," +
                "\"valid_until_utc\":{\"type\":\"string\",\"format\":\"date-time\"}," +
                "\"confidence\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1}," +
                "\"reason_codes\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"minLength\":1}}" +
            "}" +
            "}";
    }
}
