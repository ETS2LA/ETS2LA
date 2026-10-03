using ETS2LA.Networking.Users;
using ETS2LA.Networking.Settings;
using ETS2LA.Backend;
using ETS2LA.Backend.Events;
using ETS2LA.Backend.Plugins;
using ETS2LA.Notifications;
using static ETS2LA.Translations.T;
using ETS2LA.Logging;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Reflection;

namespace ETS2LA.Networking.News;

public class NewsApiClient
{
    public List<Article> Articles { get; private set; } = new();

    JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public async Task FetchAvailableNewsAsync()
    {
        try
        {
            var apiServer = NetworkingSettings.Current.CurrentApiServer;
            if (apiServer == null)
            {
                throw new InvalidOperationException("CurrentApiServer is not set.");
            }

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"{apiServer.Value.BaseUrl}/news");
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            Articles = JsonSerializer.Deserialize<List<Article>>(jsonResponse, jsonOptions) ?? new List<Article>();
        }
        catch { }
    }
}