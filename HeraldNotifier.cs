using Observatory.Framework;
using Observatory.Framework.Interfaces;
using System.Diagnostics;
using System;
using System.Net;
using System.Text.Json;
using System.Web;

namespace Observatory.Herald
{
    public class HeraldNotifier : IObservatoryNotifier
    {
        private IObservatoryCore Core;
        private AboutInfo _aboutInfo = new()
        {
            FullName = "Observatory Herald",
            ShortName = "Herald",
            Description = "Herald is a core plugin for Observatory, designed to provide cloud-based higher-quality speech synthesis.",
            AuthorName = "Vithigar",
            Links = new()
            {
                new AboutLink("github", "https://github.com/Xjph/ObservatoryCore"),
                new AboutLink("Documentation", "https://observatory.xjph.net/usage/plugins/herald"),
            }
        };

        public HeraldNotifier()
        {
            heraldSettings = DefaultSettings;
        }

        private HeraldSettings DefaultSettings
        {
            get => new ()
            {
                SelectedVoice = "American - Christopher",
                SelectedRate = "Default",
                PatreonApiEndpoint = "https://api.observatory.xjph.net/",
                CacheSize = 100,
                SettingsVersion = Version,
                UserID = string.Empty,
                SubscriptionKey = string.Empty,
                AzureRegion = string.Empty,
                OpenAIEndpoint = string.Empty,
                OpenAIKey = string.Empty,
                OpenAIModel = string.Empty,
                OpenAIVoice = string.Empty
            };
        }

        public AboutInfo AboutInfo => _aboutInfo;

        public bool OverrideAudioNotifications => true;

        public string Version => typeof(HeraldNotifier).Assembly.GetName().Version.ToString();

        public PluginUI PluginUI => new (PluginUI.UIType.None, null);

        public object Settings
        {
            get => heraldSettings;
            set
            {
                // Blow away old settings pre-patreon.
                var savedSettings = (HeraldSettings)value;
                var settingsVersion = new Version(savedSettings.SettingsVersion ?? "0.0");

                if (settingsVersion < new Version(2,0))
                {
                    heraldSettings = DefaultSettings;
                }
                else
                {
                    heraldSettings = savedSettings;
                }
            }
        }

        public void Load(IObservatoryCore observatoryCore)
        {
            Core = observatoryCore;
            var apiManager = new ApiRequestManager(
                heraldSettings, observatoryCore.HttpClient, observatoryCore.PluginStorageFolder, observatoryCore.GetPluginErrorLogger(this));
            heraldSpeech = new HeraldQueue(apiManager, observatoryCore.GetPluginErrorLogger(this), observatoryCore);
            if (string.IsNullOrWhiteSpace(heraldSettings.UserID))
                heraldSettings.UserID = apiManager.GetNewUserId().Result;
            heraldSettings.Test = TestVoice;
            heraldSettings.Authenticate = () => { Authenticate(heraldSettings.UserID); };
        }

        private static void Authenticate(string userId)
        {
            var clientId = "KajbPvbqug8DvcCQuUmto1tlA7_Ai8QgmcHzidP8viYqespuSVu1nU9knLzfhfww";
            var redirectUri = HttpUtility.UrlEncode("https://api.observatory.xjph.net/handleOAuth");
            var authUrl = $"https://www.patreon.com/oauth2/authorize?response_type=code&client_id={clientId}&redirect_uri={redirectUri}&state={userId}";
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }

        private void TestVoice()
        {
            heraldSpeech.Enqueue(
                new NotificationArgs() 
                { 
                    Title = "Herald voice testing", 
                    Detail = $"This is {heraldSettings.SelectedVoice.Split(" - ")[1]}." 
                }, 
                GetAzureNameFromSetting(heraldSettings.SelectedVoice),
                GetAzureStyleNameFromSetting(heraldSettings.SelectedVoice),
                heraldSettings.Rate[heraldSettings.SelectedRate].ToString());
        }

        public void OnNotificationEvent(NotificationArgs notificationEventArgs)
        {
            if (Core.IsLogMonitorBatchReading) return;

            if (notificationEventArgs.Rendering.HasFlag(NotificationRendering.NativeVocal))
                heraldSpeech.Enqueue(
                    notificationEventArgs, 
                    GetAzureNameFromSetting(heraldSettings.SelectedVoice),
                    GetAzureStyleNameFromSetting(heraldSettings.SelectedVoice),
                    heraldSettings.Rate[heraldSettings.SelectedRate].ToString());
        }

        private string GetAzureNameFromSetting(string settingName)
        {
            var voiceInfo = (JsonElement)heraldSettings.Voices[settingName];
            return voiceInfo.GetProperty("ShortName").GetString();
        }

        private string GetAzureStyleNameFromSetting(string settingName)
        {
            string[] settingParts = settingName.Split(" - ");
            
            if (settingParts.Length == 3)
                return settingParts[2];
            else
                return string.Empty;
        }

        private HeraldSettings heraldSettings;
        private HeraldQueue heraldSpeech;
    }
}
