using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Jellyfin.MediaEncoding.Hls.Extractors;
using Jellyfin.MediaEncoding.Hls.Playlist;
using Jellyfin.MediaEncoding.Keyframes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.MediaEncoding.Hls.Tests.Playlist
{
    public class DynamicHlsPlaylistGeneratorTests
    {
        [Theory]
        [MemberData(nameof(ComputeSegments_Valid_Success_Data))]
        public void ComputeSegments_Valid_Success(KeyframeData keyframeData, int desiredSegmentLengthMs, double[] segments)
        {
            Assert.Equal(segments, DynamicHlsPlaylistGenerator.ComputeSegments(keyframeData, desiredSegmentLengthMs));
        }

        [Fact]
        public void ComputeSegments_ZeroDurationOvershoot_ClampsToDuration()
        {
            var keyframeData = new KeyframeData(0, new[] { MsToTicks(10000) });
            Assert.Equal(new[] { 10.0 }, DynamicHlsPlaylistGenerator.ComputeSegments(keyframeData, 6000));
        }

        [Fact]
        public void ComputeSegments_MinorDurationOvershoot_ClampsToDuration()
        {
            var keyframeData = new KeyframeData(MsToTicks(9900), new[] { 0L, MsToTicks(5000), MsToTicks(10000) });
            Assert.Equal(new[] { 10.0 }, DynamicHlsPlaylistGenerator.ComputeSegments(keyframeData, 6000));
        }

        [Fact]
        public void CreateMainPlaylist_Mp4Remux_UsesIrregularKeyframeSchedule()
        {
            // Real E09 validation observed copied-video segment 36 starting at
            // 223.514958 even though the equal-length fallback advertised 216.0.
            var keyframes = Enumerable.Range(0, 36)
                .Select(i => TimeSpan.FromSeconds(i * 6).Ticks)
                .Append(2_235_149_580L)
                .ToArray();
            var keyframeData = new KeyframeData(TimeSpan.FromSeconds(240).Ticks, keyframes);
            var extractor = new TestKeyframeExtractor(keyframeData, isMetadataBased: false);
            var configurationManager = new Mock<IServerConfigurationManager>();
            configurationManager
                .Setup(i => i.GetConfiguration("encoding"))
                .Returns(new EncodingOptions
                {
                    AllowOnDemandMetadataBasedKeyframeExtractionForExtensions = ["mp4"]
                });
            var generator = new DynamicHlsPlaylistGenerator(configurationManager.Object, [extractor]);
            var request = new CreateMainPlaylistRequest(
                Guid.NewGuid(),
                "/media/e09.mp4",
                6000,
                TimeSpan.FromSeconds(240).Ticks,
                "mp4",
                "hls1/main/",
                "?test=1",
                true);

            var playlist = generator.CreateMainPlaylist(request);

            Assert.Equal(1, extractor.ExtractionCount);
            Assert.Contains("hls1/main/36.mp4?test=1&runtimeTicks=2235149580", playlist, StringComparison.Ordinal);
            Assert.DoesNotContain("hls1/main/36.mp4?test=1&runtimeTicks=2160000000", playlist, StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(ComputeEqualLengthSegments_Valid_Success_Data))]
        public void ComputeEqualLengthSegments_Valid_Success(int desiredSegmentLengthMs, long totalRuntimeTicks, double[] segments)
        {
            Assert.Equal(segments, DynamicHlsPlaylistGenerator.ComputeEqualLengthSegments(desiredSegmentLengthMs, totalRuntimeTicks));
        }

        [Theory]
        [InlineData(0, 1000000)]
        [InlineData(1000, 0)]
        public void ComputeEqualLengthSegments_Invalid_ThrowsInvalidOperationException(int desiredSegmentLengthMs, long totalRuntimeTicks)
        {
            Assert.Throws<InvalidOperationException>(() => DynamicHlsPlaylistGenerator.ComputeEqualLengthSegments(desiredSegmentLengthMs, totalRuntimeTicks));
        }

        [Theory]
        [InlineData("testfile.mkv", new string[0], false)]
        [InlineData("testfile.flv", new[] { ".mp4", ".mkv", ".ts" }, false)]
        [InlineData("testfile.flv", new[] { ".mp4", ".mkv", ".ts", ".flv" }, true)]
        [InlineData("/some/arbitrarily/long/path/testfile.mkv", new[] { "mkv" }, true)]
        public void IsExtractionAllowedForFile_Valid_Success(string filePath, string[] allowedExtensions, bool isAllowed)
        {
            Assert.Equal(isAllowed, DynamicHlsPlaylistGenerator.IsExtractionAllowedForFile(filePath, allowedExtensions));
        }

        [Theory]
        [InlineData("testfile", new[] { ".mp4" })]
        public void IsExtractionAllowedForFile_Invalid_ReturnsFalse(string filePath, string[] allowedExtensions)
        {
            Assert.False(DynamicHlsPlaylistGenerator.IsExtractionAllowedForFile(filePath, allowedExtensions));
        }

        public static TheoryData<int, long, double[]> ComputeEqualLengthSegments_Valid_Success_Data()
        {
            var data = new TheoryData<int, long, double[]>
            {
                { 6000, MsToTicks(13000), new[] { 6.0, 6.0, 1.0 } },
                { 3000, MsToTicks(15000), new[] { 3.0, 3.0, 3.0, 3.0, 3.0 } },
                { 6000, MsToTicks(25000), new[] { 6.0, 6.0, 6.0, 6.0, 1.0 } },
                { 6000, MsToTicks(20123), new[] { 6.0, 6.0, 6.0, 2.123 } },
                { 6000, MsToTicks(1234), new[] { 1.234 } }
            };

            return data;
        }

        public static TheoryData<KeyframeData, int, double[]> ComputeSegments_Valid_Success_Data()
        {
            var data = new TheoryData<KeyframeData, int, double[]>
            {
                {
                    new KeyframeData(MsToTicks(35000), new[] { 0, MsToTicks(10427), MsToTicks(20854), MsToTicks(31240) }),
                    6000,
                    new[] { 10.427, 10.427, 10.386, 3.760 }
                },
                {
                    new KeyframeData(MsToTicks(10000), new[] { 0, MsToTicks(1000), MsToTicks(2000), MsToTicks(3000), MsToTicks(4000), MsToTicks(5000) }),
                    2000,
                    new[] { 2.0, 2.0, 6.0 }
                },
                {
                    new KeyframeData(MsToTicks(10000), new[] { 0L }),
                    6000,
                    new[] { 10.0 }
                },
                {
                    new KeyframeData(MsToTicks(10000), Array.Empty<long>()),
                    6000,
                    new[] { 10.0 }
                }
            };

            return data;
        }

        private static long MsToTicks(int value) => TimeSpan.FromMilliseconds(value).Ticks;

        private sealed class TestKeyframeExtractor : IKeyframeExtractor
        {
            private readonly KeyframeData _keyframeData;

            public TestKeyframeExtractor(KeyframeData keyframeData, bool isMetadataBased)
            {
                _keyframeData = keyframeData;
                IsMetadataBased = isMetadataBased;
            }

            public bool IsMetadataBased { get; }

            public int ExtractionCount { get; private set; }

            public bool TryExtractKeyframes(Guid itemId, string filePath, [NotNullWhen(true)] out KeyframeData? keyframeData)
            {
                ExtractionCount++;
                keyframeData = _keyframeData;
                return true;
            }
        }
    }
}
