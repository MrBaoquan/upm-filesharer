using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AndX.Core
{
    /// <summary>统一 JSON 序列化配置：出参 camelCase、忽略 null；入参大小写不敏感。</summary>
    public static class AndXJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return default;
            }
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
