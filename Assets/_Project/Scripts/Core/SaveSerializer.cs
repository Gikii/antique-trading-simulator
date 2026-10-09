using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AntiqueTradingSimulator.Core
{

    public static class SaveSerializer
    {

        public const int SaveFormatVersion = 1;

        public static readonly JsonSerializerSettings Settings = CreateSettings();

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,

                ObjectCreationHandling = ObjectCreationHandling.Replace,

                ReferenceLoopHandling = ReferenceLoopHandling.Error,

                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,

                Culture = CultureInfo.InvariantCulture,
                FloatParseHandling = FloatParseHandling.Double,
                DateParseHandling = DateParseHandling.None,
            };

            // Enums are written by name, not by their numeric value.
            settings.Converters.Add(new StringEnumConverter());

            return settings;
        }

        public static string ToJson(object state) => JsonConvert.SerializeObject(state, Settings);

        public static T FromJson<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
