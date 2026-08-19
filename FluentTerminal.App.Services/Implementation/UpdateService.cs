using System;
using Windows.ApplicationModel;
using Newtonsoft.Json;
using RestSharp;
using System.Threading.Tasks;

namespace FluentTerminal.App.Services.Implementation
{
    public class UpdateService : IUpdateService
    {
        private const string ApiEndpoint = "https://api.github.com";
        private const string ReleasesUrl = "https://github.com/Yizuka17/FluentTerminalPlus/releases";

        private readonly INotificationService _notificationService;

        public UpdateService(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        public async Task CheckForUpdateAsync(bool notifyNoUpdate = false)
        {
            var latest = await GetLatestVersionAsync().ConfigureAwait(false);
            if (latest > GetCurrentVersion())
            {
                _notificationService.ShowNotification("Update available",
                    "Click to open the releases page.", ReleasesUrl);
            }
            else if (notifyNoUpdate)
            {
                _notificationService.ShowNotification("No update available", "You're up to date!");
            }
        }

        public Version GetCurrentVersion()
        {
            var currentVersion = Package.Current.Id.Version;
            return new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build, currentVersion.Revision);
        }

        public async Task<Version> GetLatestVersionAsync()
        {
            var restClient = new RestClient(ApiEndpoint);
            var restRequest = new RestRequest("/repos/Yizuka17/FluentTerminalPlus/releases", Method.Get);

            var restResponse = await restClient.ExecuteAsync(restRequest).ConfigureAwait(false);
            if (restResponse.IsSuccessful)
            {
                dynamic restResponseData = JsonConvert.DeserializeObject(restResponse.Content);
                if (restResponseData != null && restResponseData.Count > 0)
                {
                    string tag = restResponseData[0].tag_name;
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        tag = tag.TrimStart('v', 'V');
                        if (Version.TryParse(tag, out var latestVersion))
                        {
                            return new Version(
                                latestVersion.Major,
                                latestVersion.Minor,
                                Math.Max(0, latestVersion.Build),
                                Math.Max(0, latestVersion.Revision));
                        }
                    }
                }
            }
            return new Version(0, 0, 0, 0);
        }
    }
}