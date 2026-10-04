using ETS2LA.Networking.Users;
using ETS2LA.Networking.Settings;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Reflection;

namespace ETS2LA.Networking.News;

public class UserApiClient
{
    JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    struct LoginResponse
    {
        public string token { get; set; }
        public DateTime expiry { get; set; }
    }

    public async Task<User?> Login(string username, string otpcode)
    {
        try
        {
            var apiServer = NetworkingSettings.Current.CurrentApiServer;
            if (apiServer == null)
            {
                throw new InvalidOperationException("CurrentApiServer is not set.");
            }

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsync($"{apiServer.Value.BaseUrl}/users/login?username={Uri.EscapeDataString(username)}&otpcode={Uri.EscapeDataString(otpcode)}", null);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            LoginResponse? loginResponse = JsonSerializer.Deserialize<LoginResponse>(jsonResponse, jsonOptions);

            if (loginResponse == null)
            {
                throw new InvalidOperationException("Failed to deserialize login response.");
            }
            if (string.IsNullOrEmpty(loginResponse?.token))
            {
                throw new InvalidOperationException("Login failed: token is null or empty.");
            }
            if (loginResponse?.expiry == DateTime.MinValue)
            {
                throw new InvalidOperationException("Login failed: expiry is not set.");
            }

            return new User
            {
                Username = username,
                JwtToken = loginResponse?.token ?? string.Empty,
                Expiry = loginResponse?.expiry ?? DateTime.MinValue
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Login failed for user: {username}. Exception: {ex.Message}");
        }

        return null;
    }
}