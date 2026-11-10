using ETS2LA.Logging;
using ETS2LA.Settings;
using System.Text.Json;
using ETS2LA.Networking.Settings;
using static ETS2LA.Translations.T;

namespace ETS2LA.Networking.CloudSettings;

public class CloudSettingsClient
{
    readonly JsonSerializerOptions JsonOption = new JsonSerializerOptions {
        WriteIndented = true,
        IncludeFields = true
    };

    public CloudSettingsClient()
    {
        
    }

    public void SaveSettingsToCloud(bool waitForCompletion = false)
    {
        var user = NetworkingSettings.Current.CurrentUser;
        if (String.IsNullOrEmpty(user.JwtToken))
        {
            Logger.Warn(_("Cannot save settings to cloud: User is not logged in or JWT token is missing."));
            return;
        }

        var apiServer = NetworkingSettings.Current.CurrentApiServer;
        if (apiServer == null)
        {
            throw new InvalidOperationException("CurrentApiServer is not set.");
        }

        try
        {
            // Gather all files from the settings directory
            var handler = new SettingsHandler();
            var settingsFiles = Directory.GetFiles(handler.BaseDirectory, "*.json", SearchOption.TopDirectoryOnly);
            // {
            //   "files": {
            //     "filename.json": {
            //       ...
            //     } 
            //   }
            // }
            var settingsData = new Dictionary<string, object>();
            var settingsList = new Dictionary<string, object>();
            foreach (var file in settingsFiles)
            {
                var fileName = Path.GetFileName(file);
                var jsonContent = File.ReadAllText(file);
                var jsonData = JsonSerializer.Deserialize<object>(jsonContent, JsonOption);
                settingsList[fileName] = jsonData ?? new object();
            }
            settingsData["files"] = settingsList;
            var jsonSettings = JsonSerializer.Serialize(settingsData, JsonOption);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.JwtToken);
            var payload = new StringContent(jsonSettings, System.Text.Encoding.UTF8, "application/json");
            var response = httpClient.PostAsync($"{apiServer.Value.BaseUrl}/user/settings", payload);
            if (waitForCompletion)
            {
                response.Wait();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(_("Failed to save settings to cloud: {0}", ex.Message));
        }
    }

    public void LoadSettingsFromCloud()
    {
        var user = NetworkingSettings.Current.CurrentUser;
        if (String.IsNullOrEmpty(user.JwtToken))
        {
            Logger.Warn(_("Cannot load settings from cloud: User is not logged in or JWT token is missing."));
            return;
        }

        var apiServer = NetworkingSettings.Current.CurrentApiServer;
        if (apiServer == null)
        {
            throw new InvalidOperationException("CurrentApiServer is not set.");
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.JwtToken);
            var response = httpClient.GetAsync($"{apiServer.Value.BaseUrl}/user/settings").Result;

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn(_("Failed to load settings from cloud: {0}", response.ReasonPhrase));
                return;
            }

            var jsonSettings = response.Content.ReadAsStringAsync().Result;
            // Save into SettingsDirectory/cloud_update.json
            // This will be loaded on next startup by SettingsHandler.
            var handler = new SettingsHandler();
            var loadNextPath = Path.Combine(handler.BaseDirectory, "cloud_update.json");
            File.WriteAllText(loadNextPath, jsonSettings);
        }
        catch (Exception ex)
        {
            Logger.Error(_("Failed to load settings from cloud: {0}", ex.Message));
        }
    }
}