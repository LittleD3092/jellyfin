using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Server.Migrations.Routines;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Tests.Migrations;

public class EnableMp4KeyframeExtractionTests
{
    [Fact]
    public async Task PerformAsync_AddsMp4ToLegacyDefault()
    {
        var options = new EncodingOptions
        {
            AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = ["mkv"]
        };
        var configurationManager = CreateConfigurationManager(options);

        await new EnableMp4KeyframeExtraction(NullLogger<EnableMp4KeyframeExtraction>.Instance, configurationManager.Object)
            .PerformAsync(CancellationToken.None);

        Assert.Equal(["mkv", "mp4"], options.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions);
        configurationManager.Verify(i => i.SaveConfiguration("encoding", options), Times.Once);
    }

    [Theory]
    [MemberData(nameof(NonLegacyConfigurations))]
    public async Task PerformAsync_PreservesNonLegacyConfiguration(string[] allowedExtensions)
    {
        var options = new EncodingOptions
        {
            AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = allowedExtensions
        };
        var configurationManager = CreateConfigurationManager(options);

        await new EnableMp4KeyframeExtraction(NullLogger<EnableMp4KeyframeExtraction>.Instance, configurationManager.Object)
            .PerformAsync(CancellationToken.None);

        Assert.Equal(allowedExtensions, options.AllowOnDemandMetadataBasedKeyframeExtractionForExtensions);
        configurationManager.Verify(i => i.SaveConfiguration(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    public static IEnumerable<object[]> NonLegacyConfigurations()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "webm" }];
        yield return [new[] { "mkv", "webm" }];
        yield return [new[] { "mkv", "mp4" }];
    }

    private static Mock<IConfigurationManager> CreateConfigurationManager(EncodingOptions options)
    {
        var configurationManager = new Mock<IConfigurationManager>();
        configurationManager
            .Setup(i => i.GetConfiguration("encoding"))
            .Returns(options);
        return configurationManager;
    }
}
