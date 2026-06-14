using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoFlow.App.Services;

public sealed class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/dss886/AutoFlow/releases/latest";
    private static readonly Regex SemVerRegex = new(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?$",
        RegexOptions.Compiled);

    private readonly AppLoggerService _logger;

    public UpdateCheckService(AppLoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string CurrentVersion => GetCurrentVersion();

    public async Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetUpdateCheckResultAsync(cancellationToken);
        if (result.Status == UpdateCheckStatus.UpdateAvailable)
        {
            _logger.I($"发现新版本 v{result.LatestVersion}，当前版本 v{result.CurrentVersion}，可前往 GitHub Release 下载更新。");
        }
    }

    public async Task CheckForUpdatesFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        _logger.I($"开始检查更新");

        var result = await GetUpdateCheckResultAsync(cancellationToken);
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                _logger.I($"发现新版本 v{result.LatestVersion}，当前版本 v{result.CurrentVersion}，可前往 GitHub Release 下载更新。");
                break;
            case UpdateCheckStatus.UpToDate:
                _logger.I($"当前已是最新版本 v{result.CurrentVersion}。");
                break;
            case UpdateCheckStatus.NewerThanRelease:
                _logger.I($"当前版本 v{result.CurrentVersion} 比 Github Release 公开发布版本 v{result.LatestVersion} 更高。");
                break;
            case UpdateCheckStatus.Failed:
                _logger.W($"检查更新失败：{result.ErrorMessage}");
                break;
        }
    }

    private async Task<UpdateCheckResult> GetUpdateCheckResultAsync(CancellationToken cancellationToken)
    {
        var currentVersion = GetCurrentVersion();

        try
        {
            var systemProxy = WebRequest.DefaultWebProxy;
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseProxy = true,
                Proxy = systemProxy,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            };

            if (systemProxy is not null)
            {
                systemProxy.Credentials = CredentialCache.DefaultCredentials;
            }

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoFlow-UpdateCheck");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(
                    currentVersion,
                    $"GitHub API 返回错误状态码: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var latestVersion = TryExtractVersionFromApiResponse(json.RootElement);

            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return UpdateCheckResult.Failed(currentVersion, "未能从 GitHub API 返回结果中解析最新版本号。");
            }

            var versionComparison = CompareVersions(currentVersion, latestVersion);
            if (versionComparison < 0)
            {
                return UpdateCheckResult.UpdateAvailable(currentVersion, latestVersion);
            }

            if (versionComparison > 0)
            {
                return UpdateCheckResult.NewerThanRelease(currentVersion, latestVersion);
            }

            return UpdateCheckResult.UpToDate(currentVersion, latestVersion);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed(currentVersion, "请求已取消或超时。");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(currentVersion, ex.Message);
        }
    }

    private static string GetCurrentVersion()
    {
        var infoVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.1";

        return NormalizeVersion(infoVersion);
    }

    private static string? TryExtractVersionFromApiResponse(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out var tagNameElement))
        {
            return null;
        }

        var tagName = tagNameElement.GetString();
        return string.IsNullOrWhiteSpace(tagName) ? null : NormalizeVersion(tagName);
    }

    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.1";
        }

        var normalized = version.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var plusIndex = normalized.IndexOf('+');
        return plusIndex >= 0 ? normalized[..plusIndex] : normalized;
    }

    private static int CompareVersions(string currentVersion, string latestVersion)
    {
        if (TryParseSemVer(currentVersion, out var current) && TryParseSemVer(latestVersion, out var latest))
        {
            return current.CompareTo(latest);
        }

        return string.Equals(currentVersion, latestVersion, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
    }

    private static bool TryParseSemVer(string version, out SemanticVersion semanticVersion)
    {
        var match = SemVerRegex.Match(version);
        if (!match.Success)
        {
            semanticVersion = default;
            return false;
        }

        semanticVersion = new SemanticVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    private enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        NewerThanRelease,
        Failed,
    }

    private readonly record struct UpdateCheckResult(
        UpdateCheckStatus Status,
        string CurrentVersion,
        string? LatestVersion,
        string? ErrorMessage)
    {
        public static UpdateCheckResult UpToDate(string currentVersion, string latestVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, currentVersion, latestVersion, null);
        }

        public static UpdateCheckResult UpdateAvailable(string currentVersion, string latestVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, currentVersion, latestVersion, null);
        }

        public static UpdateCheckResult NewerThanRelease(string currentVersion, string latestVersion)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NewerThanRelease, currentVersion, latestVersion, null);
        }

        public static UpdateCheckResult Failed(string currentVersion, string errorMessage)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, currentVersion, null, errorMessage);
        }
    }

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
    {
        public int CompareTo(SemanticVersion other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
            {
                return minor;
            }

            var patch = Patch.CompareTo(other.Patch);
            if (patch != 0)
            {
                return patch;
            }

            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(left))
            {
                return 1;
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return -1;
            }

            var leftParts = left.Split('.');
            var rightParts = right.Split('.');
            var count = Math.Max(leftParts.Length, rightParts.Length);

            for (var index = 0; index < count; index++)
            {
                if (index >= leftParts.Length)
                {
                    return -1;
                }

                if (index >= rightParts.Length)
                {
                    return 1;
                }

                var comparison = CompareIdentifier(leftParts[index], rightParts[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private static int CompareIdentifier(string left, string right)
        {
            var leftIsNumber = int.TryParse(left, out var leftNumber);
            var rightIsNumber = int.TryParse(right, out var rightNumber);

            if (leftIsNumber && rightIsNumber)
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
