using Observatory.Framework;

namespace Observatory.Herald
{
    public class HeraldSettings
    {
        [SettingIgnore]
        public string SettingsVersion { get; set; }

        [SettingDisplayName("API")]
        [SettingBackingValue("SelectedAPI")]
        public Dictionary<string, object> API
        { get => new()
            {
                { "Observatory API (Patreon)", 0 },
                { "Azure Cognitive Services", 1 },
                { "OpenAI", 2 }
            };
        }

        [SettingIgnore]
        public string SelectedAPI {  get; set; }

        [SettingDisplayName("Voice")]
        [SettingBackingValue("SelectedVoice")]
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, object> Voices { get; internal set; }

        [SettingIgnore]
        public string SelectedVoice { get; set; }

        [SettingBackingValue("SelectedRate")]
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, object> Rate
        {
            get => new()
            {
                {"Slowest", "0.5"},
                {"Slower", "0.75"},
                {"Default", "1.0"},
                {"Faster", "1.25"},
                {"Fastest", "1.5"}
            };
        }

        [SettingIgnore]
        public string SelectedRate { get; set; }

        [SettingDisplayName("Cache Size (MB): ")]
        public int CacheSize { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public Action Test { get; internal set; }

        [SettingNewGroup("Observatory API (Patreon) Settings")]
        [SettingDisplayName("User ID")]
        public string UserID { get; set; }

        [SettingDisplayName("Login With Patreon")]
        [System.Text.Json.Serialization.JsonIgnore]
        public Action Authenticate { get; internal set; }

        [SettingIgnore]
        public string PatreonApiEndpoint { get; set; }

        [SettingNewGroup("Azure Cognitive Services Settings")]
        [SettingDisplayName("Subscription Key")]
        public string SubscriptionKey { get; set; }

        [SettingDisplayName("Azure Region")]
        public string AzureRegion { get; set; }

        [SettingNewGroup("OpenAI Settings")]
        [SettingDisplayName("API Endpoint")]
        public string OpenAIEndpoint { get; set; }

        [SettingDisplayName("API Key")]
        public string OpenAIKey { get; set; }

        [SettingDisplayName("Model")]
        public string OpenAIModel { get; set; }

        [SettingDisplayName("Voice")]
        public string OpenAIVoice { get; set; }
    }
}
