using ETS2LA.Networking.Settings;
using ETS2LA.Networking.Plugins;
using ETS2LA.Networking.Updates;
using ETS2LA.Networking.News;

namespace ETS2LA.Networking;

public class NetworkingClient
{
    private static readonly Lazy<NetworkingClient> _instance = new(() => new NetworkingClient());
    public static NetworkingClient Current => _instance.Value;

    private List<ApiServer> apiServers = new()
    {
        new ApiServer { Name = "Global", BaseUrl = "https://api.ets2la.com/api/v1" },
        new ApiServer { Name = "China", BaseUrl = "https://api.ets2la.cn/api/v1" }
    };

    public PluginApiClient Plugins { get; } = new();
    public NewsApiClient News { get; } = new();
    public UserApiClient UserClient { get; } = new();

    public NetworkingClient()
    {
        if (NetworkingSettings.Current.CurrentApiServer == null)
        {
            // Updater sets it's source based on the download location. We can assume that
            // if the download was from CNB, then we should use the China server.
            if (Updater.Current.GetSelectedSource().sourceName == "CNB")
                NetworkingSettings.Current.CurrentApiServer = apiServers.FirstOrDefault(s => s.Name == "China", apiServers[0]);
            else
                NetworkingSettings.Current.CurrentApiServer = apiServers.FirstOrDefault(s => s.Name == "Global", apiServers[0]);

            NetworkingSettings.Current.Save();
        }

        Plugins.FetchAvailablePluginsAsync();
        News.FetchAvailableNewsAsync();
    }
}