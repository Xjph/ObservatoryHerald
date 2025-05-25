using Observatory.Framework;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace Observatory.Herald
{
    class ApiRequestManager
    {
        private HttpClient httpClient;
        private DirectoryInfo cacheLocation;
        private int cacheSize;
        private Action<Exception, string> ErrorLogger;
        private ConcurrentDictionary<string, CacheData> cacheIndex;
        private Api Api {
            get
            {
                if (settings.API.TryGetValue(settings.SelectedAPI ?? "Patreon", out var selectedApi))
                {
                    return (Api)selectedApi;
                }
                else
                {
                    return Api.Patreon;
                }
            }
        }
        private HeraldSettings settings;

        internal ApiRequestManager(
            HeraldSettings settings, HttpClient httpClient, string cacheFolder, Action<Exception, String> errorLogger)
        {
            this.settings = settings;
            // Api = settings.API.TryGetValue(settings.SelectedAPI ?? "Patreon", out var selectedApi) ? (Api)selectedApi : Api.Patreon;
            this.httpClient = httpClient;
            cacheSize = Math.Max(settings.CacheSize, 1);
            cacheLocation = new DirectoryInfo(cacheFolder);
            ReadCache();
            ErrorLogger = errorLogger;

            if (!Directory.Exists(cacheLocation.FullName))
            {
                Directory.CreateDirectory(cacheLocation.FullName);
            }

            settings.Voices = PopulateVoiceSettingOptions();
        }

        internal async Task<string> GetNewUserId()
        {
            var requestTask = httpClient.GetAsync(settings.PatreonApiEndpoint + "NewUserId");

            requestTask.Wait(5000);

            if (requestTask.IsFaulted)
                throw new PluginException("Herald", "Error retrieving new user ID.", requestTask.Exception);

            using var response = await requestTask;
            if (response.IsSuccessStatusCode)
            {
                var userId = await response.Content.ReadAsStringAsync();
                settings.UserID = userId;
                return userId;
            }
            else
            {
                throw new PluginException("Herald", "Unable to retrieve new user ID.", new Exception(response.StatusCode.ToString() + ": " + response.ReasonPhrase));
            }
        }

        internal async Task<string> GetAudioFile(string ssml, string rawText, string voice, string style, string rate)
        {

            ssml = AddVoiceToSsml(ssml, voice, style, rate);

            using var sha = SHA256.Create();

            var ssmlHash = BitConverter.ToString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(ssml))
                ).Replace("-", string.Empty);

            var audioFilename = cacheLocation + ssmlHash + ".mp3";

            FileInfo audioFileInfo = null;
            if (File.Exists(audioFilename))
            {
                audioFileInfo = new FileInfo(audioFilename);
                if (audioFileInfo.Length == 0)
                {
                    File.Delete(audioFilename);
                    audioFileInfo = null;
                }
            }

            audioFileInfo ??= Api switch
                {
                    Api.Patreon => await GetAudioFromPatreon(ssml, audioFilename),
                    Api.Azure => await GetAudioFromAzure(ssml, audioFilename),
                    Api.OpenAI => await GetAudioFromOpenAI(rawText, audioFilename),
                    _ => throw new NotImplementedException("No valid API selected."),
                };

            UpdateAndPruneCache(audioFileInfo);
                        
            return audioFilename;
        }

        private async Task<FileInfo> GetAudioFromPatreon(string ssml, string audioFilename)
        {
            using StringContent request = new(ssml)
            {
                Headers = {
                    { "User-Id", settings.UserID }
                }
            };

            var requestTask = httpClient.PostAsync(settings.PatreonApiEndpoint + "AzureVoice/Speak", request);

            requestTask.Wait(5000);

            if (requestTask.IsFaulted)
                throw new PluginException("Herald", "Error retrieving voice audio from Observatory API.", requestTask.Exception);

            using var response = await requestTask;

            if (response.IsSuccessStatusCode)
            {
                using FileStream fileStream = new FileStream(audioFilename, FileMode.CreateNew);
                response.Content.ReadAsStream().CopyTo(fileStream);
                fileStream.Close();
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new PluginException("Herald", "Not Authorised to use Observatory API, authenticate with Patreon first.", new());
            }
            else
            {
                throw new PluginException("Herald", "Unable to retrieve audio data from Observatory API.", new Exception(response.StatusCode.ToString() + ": " + response.ReasonPhrase));
            }
            return new FileInfo(audioFilename);
        }

        private async Task<FileInfo> GetAudioFromAzure(string ssml, string audioFilename)
        {
            using StringContent request = new(ssml, Encoding.UTF8, "application/ssml+xml");
            
           

            var url = $"https://{settings.AzureRegion}.tts.speech.microsoft.com/cognitiveservices/v1";
            using HttpRequestMessage httpRequest = new(HttpMethod.Post, url);
            httpRequest.Headers.Add("User-Agent", $"ObservatoryHerald{settings.SettingsVersion} (+https://github.com/Xjph/ObservatoryCore)");
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", settings.SubscriptionKey);
            httpRequest.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-128kbitrate-mono-mp3");
            httpRequest.Headers.Add("X-Microsoft-ProjectName", "ObservatoryHerald");
            httpRequest.Content = request;

            var requestTask = httpClient.SendAsync(httpRequest);

            requestTask.Wait(5000);

            if (requestTask.IsFaulted)
                throw new PluginException("Herald", "Error retrieving voice audio from Azure.", requestTask.Exception);

            using var response = await requestTask;

            if (response.IsSuccessStatusCode)
            {
                using FileStream fileStream = new FileStream(audioFilename, FileMode.CreateNew);
                response.Content.ReadAsStream().CopyTo(fileStream);
                fileStream.Close();
            }
            else
            {
                throw new PluginException("Herald", "Unable to retrieve audio data from Azure.", new Exception(response.StatusCode.ToString() + ": " + response.ReasonPhrase));
            }
            return new FileInfo(audioFilename);
        }

        private async Task<FileInfo> GetAudioFromOpenAI(string text, string audioFilename)
        {
            Dictionary<string, string> openAiPayload = new()
            {
                { "model", settings.OpenAIModel },
                { "input", text },
                { "voice", settings.OpenAIVoice },
                { "response_format", "mp3" }
            };

            using StringContent request = new(JsonSerializer.Serialize(openAiPayload))
            {
                Headers = {
                    { "Authorization", "Bearer " + settings.OpenAIKey },
                    { "Content-Type", "application/json" }
                }
            };

            var requestTask = httpClient.PostAsync(settings.OpenAIEndpoint, request);

            requestTask.Wait(5000);

            if (requestTask.IsFaulted)
                throw new PluginException("Herald", "Error retrieving voice audio from OpenAI.", requestTask.Exception);

            using var response = await requestTask;

            if (response.IsSuccessStatusCode)
            {
                using FileStream fileStream = new FileStream(audioFilename, FileMode.CreateNew);
                response.Content.ReadAsStream().CopyTo(fileStream);
                fileStream.Close();
            }
            else
            {
                throw new PluginException("Herald", "Unable to retrieve audio data from OpenAI.", new Exception(response.StatusCode.ToString() + ": " + response.ReasonPhrase));
            }
            return new FileInfo(audioFilename);
        }

        private static string AddVoiceToSsml(string ssml, string voiceName, string styleName, string rate)
        {
            XmlDocument ssmlDoc = new();
            ssmlDoc.LoadXml(ssml);

            var ssmlNamespace = ssmlDoc.DocumentElement.NamespaceURI;
            XmlNamespaceManager ssmlNs = new(ssmlDoc.NameTable);
            ssmlNs.AddNamespace("ssml", ssmlNamespace);
            ssmlNs.AddNamespace("mstts", "http://www.w3.org/2001/mstts");
            ssmlNs.AddNamespace("emo", "http://www.w3.org/2009/10/emotionml");

            var voiceNode = ssmlDoc.SelectSingleNode("/ssml:speak/ssml:voice", ssmlNs);
            voiceNode.Attributes.GetNamedItem("name").Value = voiceName;

            if (!string.IsNullOrWhiteSpace(rate))
            {
                var prosodyNode = ssmlDoc.CreateElement("ssml", "prosody", ssmlNamespace);
                prosodyNode.SetAttribute("rate", rate);
                prosodyNode.InnerXml = voiceNode.InnerXml;
                voiceNode.InnerXml = prosodyNode.OuterXml;
            }

            if (!string.IsNullOrWhiteSpace(styleName))
            {
                var expressAsNode = ssmlDoc.CreateElement("mstts", "express-as", "http://www.w3.org/2001/mstts");
                expressAsNode.SetAttribute("style", styleName);
                expressAsNode.InnerXml = voiceNode.InnerXml;
                voiceNode.InnerXml = expressAsNode.OuterXml;
            }

            return ssmlDoc.OuterXml;
        }

        private Dictionary<string, object> PopulateVoiceSettingOptions()
        {
            Dictionary<string, object> voices = new();

            using var request = new HttpRequestMessage(HttpMethod.Get, settings.PatreonApiEndpoint + "AzureVoice/List");

            var response = httpClient.Send(request);

            if (response.IsSuccessStatusCode)
            {
                var voiceJson = response.Content.ReadAsStringAsync().Result;
                var voiceDoc = JsonDocument.Parse(voiceJson);

                var englishSpeakingVoices = from v in voiceDoc.RootElement.EnumerateArray()
                                            where v.GetProperty("Locale").GetString().StartsWith("en-")
                                            || (v.TryGetProperty("SecondaryLocaleList", out var l) && l.EnumerateArray().Any(s => s.GetString().StartsWith("en-")))
                                            select v;

                foreach(var voice in englishSpeakingVoices)
                {
                    string demonym = GetDemonymFromLocale(voice.GetProperty("Locale").GetString());

                    voices.TryAdd(
                        demonym + " - " + voice.GetProperty("LocalName").GetString(),
                        voice);

                    if (voice.TryGetProperty("StyleList", out var styles))
                    foreach (var style in styles.EnumerateArray())
                    {
                        voices.TryAdd(
                            demonym + " - " + voice.GetProperty("LocalName").GetString() + " - " + style.GetString(),
                            voice);
                    }
                }
            }
            else
            {
                throw new PluginException("Herald", "Unable to retrieve available voices.", new Exception(response.StatusCode.ToString() + ": " + response.ReasonPhrase));
            }
            
            return voices;
        }

        private static readonly Dictionary<string, string> LocaleDemonymMap = new()
        {
            { "en-AU", "Australian" },
            { "en-CA", "Canadian" },
            { "en-GB", "British" },
            { "en-HK", "Hong Konger" },
            { "en-IE", "Irish" },
            { "en-IN", "Indian" },
            { "en-KE", "Kenyan" },
            { "en-NG", "Nigerian" },
            { "en-NZ", "Kiwi" },
            { "en-PH", "Filipino" },
            { "en-SG", "Singaporean" },
            { "en-TZ", "Tanzanian" },
            { "en-US", "American" },
            { "en-ZA", "South African" },
            { "de-DE", "German" },
            { "fr-FR", "French" },
            { "it-IT", "Italian" },
            { "ja-JP", "Japanese" },
            { "ko-KR", "Korean" },
            { "es-ES", "Spanish" },
            { "es-MX", "Mexican" },
            { "pt-BR", "Brazilian" },
            { "pt-PT", "Portuguese" },
            { "zh-CN", "Chinese" },
            { "zh-HK", "Hong Konger" },
            { "zh-TW", "Taiwanese" }
        };

        private static string GetDemonymFromLocale(string locale)
        {
            return LocaleDemonymMap.TryGetValue(locale, out var demonym) ? demonym : locale;
        }

        private void ReadCache()
        {
            string cacheIndexFile = cacheLocation + "CacheIndex.json";

            if (File.Exists(cacheIndexFile))
            {
                var indexFileContent = File.ReadAllText(cacheIndexFile);
                try
                {
                    cacheIndex = JsonSerializer.Deserialize<ConcurrentDictionary<string, CacheData>>(indexFileContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    cacheIndex = new();
                    ErrorLogger(ex, "deserializing CacheIndex.json");
                }
            }
            else
            {
                cacheIndex = new();
            }

            // Re-index orphaned files in event of corrupted or lost index.
            var cacheFiles = cacheLocation.GetFiles("*.mp3");
            foreach (var file in cacheFiles.Where(file => !cacheIndex.ContainsKey(file.Name)))
            {
                cacheIndex.TryAdd(file.Name, new(file.CreationTime, 0));
            };
        }

        private void UpdateAndPruneCache(FileInfo currentFile)
        {
            var cacheFiles = cacheLocation.GetFiles("*.mp3");
            if (cacheIndex.ContainsKey(currentFile.Name))
            {
                cacheIndex[currentFile.Name] = new(
                    cacheIndex[currentFile.Name].Created,
                    cacheIndex[currentFile.Name].HitCount + 1
                    );
            }
            else
            {
                cacheIndex.TryAdd(currentFile.Name, new(DateTime.UtcNow, 1));
            }

            var indexedCacheSize = cacheFiles
                .Where(f => cacheIndex.ContainsKey(f.Name))
                .Sum(f => f.Length);

            while (indexedCacheSize > cacheSize * 1024 * 1024)
            {
                var staleFile = (from file in cacheIndex
                                orderby file.Value.HitCount, file.Value.Created
                                select file.Key).First();

                if (staleFile == currentFile.Name)
                    break;

                cacheIndex.TryRemove(staleFile, out _);
            }
        }

        internal async void CommitCache()
        {
            string cacheIndexFile = cacheLocation + "CacheIndex.json";

            System.Diagnostics.Stopwatch stopwatch = new();
            stopwatch.Start();

            // Race condition isn't a concern anymore, but should check this anyway to be safe.
            // (Maybe someone is poking at the file with notepad?)
            while (!IsFileWritable(cacheIndexFile) && stopwatch.ElapsedMilliseconds < 1000)
                await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(100));

            // 1000ms should be more than enough for a conflicting title or detail to complete,
            // if we're still waiting something else is locking the file, just give up.
            if (stopwatch.ElapsedMilliseconds < 1000)
            {
                File.WriteAllText(cacheIndexFile, JsonSerializer.Serialize(cacheIndex));

                // Purge cache from earlier versions, if still present.
                var legacyCache = cacheLocation.GetFiles("*.wav");
                Array.ForEach(legacyCache, file => File.Delete(file.FullName));
            }

            stopwatch.Stop();
        }

        private static bool IsFileWritable(string path)
        {
            try
            {
                using FileStream fs = File.OpenWrite(path);
                fs.Close();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private class CacheData
        {
            public CacheData(DateTime Created, int HitCount)
            {
                this.Created = Created;
                this.HitCount = HitCount;
            }
            public DateTime Created { get; set; }
            public int HitCount { get; set; }
        }
    }
}
