using ETS2LA.Networking.CloudSettings;
using ETS2LA.Networking.Settings;
using ETS2LA.Networking.Plugins;
using ETS2LA.Networking.Updates;
using ETS2LA.Networking.News;
using ETS2LA.Logging;

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
    public CloudSettingsClient CloudSettingsClient { get; } = new();

    public bool TokenExpired = false;

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

        // Update user info on startup, basically check if the token is up to date.
        Task.Run(async () =>
        {
            try
            {
                var currentUser = NetworkingSettings.Current.CurrentUser;
                if (currentUser != null && !string.IsNullOrEmpty(currentUser.JwtToken))
                {
                    var userInfo = await UserClient.GetUserInfo(currentUser);
                    if (userInfo != null)
                    {
                        NetworkingSettings.Current.CurrentUser = new Users.User
                        {
                            Id = currentUser.Id,
                            Username = userInfo.Value.username,
                            JwtToken = currentUser.JwtToken,
                            Expiry = currentUser.Expiry,
                            Role = userInfo.Value.role,
                            Paddle = userInfo.Value.paddle
                        };
                        NetworkingSettings.Current.Save();
                    } 
                    else
                    {
                        Logger.Warn("Failed to retrieve user info on startup, clearing current user. Please login again.");
                        NetworkingSettings.Current.CurrentUser = new Users.User();
                        NetworkingSettings.Current.Save();
                        TokenExpired = true;
                    }
                }
            } 
            catch (Exception ex)
            {
                Logger.Warn($"Failed to update user info on startup: {ex.Message}");
                NetworkingSettings.Current.CurrentUser = new Users.User();
                NetworkingSettings.Current.Save();
                TokenExpired = true;
            }
        });
    }

    public void Shutdown()
    {
        if (NetworkingSettings.Current.AutomaticallySaveSettingsToCloud)
        {
            CloudSettingsClient.SaveSettingsToCloud(waitForCompletion: true);
        }
    }
}