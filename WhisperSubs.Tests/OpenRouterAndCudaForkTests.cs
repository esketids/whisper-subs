using System;
using System.Diagnostics;
using WhisperSubs.Configuration;
using WhisperSubs.Providers;
using Xunit;

namespace WhisperSubs.Tests;

public class OpenRouterAndCudaForkTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/audio/transcriptions", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/audio/translations", "https://openrouter.ai/api")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000")]
    public void NormalizeBaseUrl_StripsTrailingVersionOrAudioPaths(string input, string expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("https://openrouter.ai/api", true)]
    [InlineData("https://api.openrouter.ai/api", true)]
    [InlineData("https://api.openai.com", false)]
    [InlineData("https://api.groq.com/openai", false)]
    [InlineData("http://localhost:8000", false)]
    public void IsOpenRouterHost_DetectsOpenRouterOnly(string url, bool expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.IsOpenRouterHost(url));
    }

    [Theory]
    [InlineData("whisper-1", "https://openrouter.ai/api", "openai/whisper-1")]
    [InlineData("whisper-large-v3-turbo", "https://openrouter.ai/api", "openai/whisper-large-v3-turbo")]
    [InlineData("openai/whisper-large-v3", "https://openrouter.ai/api", "openai/whisper-large-v3")]
    [InlineData("qwen/qwen3-asr-1.7b", "https://openrouter.ai/api", "qwen/qwen3-asr-1.7b")]
    [InlineData("whisper-1", "https://api.openai.com", "whisper-1")]
    public void NormalizeModelForEndpoint_PrefixesBareWhisperSlugsOnOpenRouter(string model, string url, string expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.NormalizeModelForEndpoint(model, url));
    }

    [Fact]
    public void DefaultResponseFormat_UsesVerboseJsonOnOpenRouterAndSrtOnOpenAi()
    {
        Assert.Equal("verbose_json", RemoteWhisperProvider.DefaultResponseFormat("openai", "https://openrouter.ai/api"));
        Assert.Equal("srt", RemoteWhisperProvider.DefaultResponseFormat("openai", "https://api.openai.com"));
    }

    [Fact]
    public void HostEngineGates_RoundRobinsCudaVisibleDevicesWhenConfigured()
    {
        try
        {
            HostEngineGates.Apply(new PluginConfiguration { CudaDevices = "0, 1" });

            var s1 = new ProcessStartInfo();
            var s2 = new ProcessStartInfo();
            var s3 = new ProcessStartInfo();
            HostEngineGates.ApplyVulkanDevice(s1);
            HostEngineGates.ApplyVulkanDevice(s2);
            HostEngineGates.ApplyVulkanDevice(s3);

            Assert.Equal("0", s1.Environment["CUDA_VISIBLE_DEVICES"]);
            Assert.Equal("1", s2.Environment["CUDA_VISIBLE_DEVICES"]);
            Assert.Equal("0", s3.Environment["CUDA_VISIBLE_DEVICES"]);

            HostEngineGates.Apply(new PluginConfiguration { CudaDevices = "" });
            var s4 = new ProcessStartInfo();
            HostEngineGates.ApplyVulkanDevice(s4);
            Assert.False(s4.Environment.ContainsKey("CUDA_VISIBLE_DEVICES"));
        }
        finally
        {
            HostEngineGates.Reset();
        }
    }
}
