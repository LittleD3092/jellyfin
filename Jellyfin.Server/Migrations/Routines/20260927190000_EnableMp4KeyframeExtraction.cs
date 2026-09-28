using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Enables keyframe extraction for MP4 files so direct-stream HLS playlists use real keyframe boundaries.
/// </summary>
[JellyfinMigration("2026-09-27T19:00:00", nameof(EnableMp4KeyframeExtraction))]
internal sealed class EnableMp4KeyframeExtraction : IAsyncMigrationRoutine
{
    private readonly ILogger<EnableMp4KeyframeExtraction> _logger;
    private readonly IConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnableMp4KeyframeExtraction"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="configurationManager">The configuration manager.</param>
    public EnableMp4KeyframeExtraction(
        ILogger<EnableMp4KeyframeExtraction> logger,
        IConfigurationManager configurationManager)
    {
        _logger = logger;
        _configurationManager = configurationManager;
    }

    /// <inheritdoc />
    public Task PerformAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var encodingOptions = _configurationManager.GetEncodingOptions();
        var allowedExtensions = encodingOptions.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions ?? [];
        // An empty or custom list may be an intentional opt-out. Only update the legacy default.
        if (allowedExtensions.Length != 1
            || !allowedExtensions.Contains("mkv", StringComparer.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Enabling on-demand keyframe extraction for MP4 files");
        encodingOptions.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = [.. allowedExtensions, "mp4"];
        _configurationManager.SaveConfiguration("encoding", encodingOptions);
        return Task.CompletedTask;
    }
}
