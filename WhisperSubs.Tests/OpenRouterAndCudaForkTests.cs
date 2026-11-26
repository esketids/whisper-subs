using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperSubs.Configuration;
using WhisperSubs.Controller.Workers;
using WhisperSubs.Providers;
using Xunit;

namespace WhisperSubs.Tests;

public class OpenRouterAndCudaForkTests
{
    private sealed class CapturingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(string Path, string Body, string? MediaType, string? Auth, string? Title, string? Referer)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
            Requests.Add((
                request.RequestUri!.AbsolutePath,
                body,
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-OpenRouter-Title", out var t) ? string.Join(",", t) : null,
                request.Headers.TryGetValues("HTTP-Referer", out var r) ? string.Join(",", r) : null));
            return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("https://openrouter.ai", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/v1", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/audio/transcriptions", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/audio/translations", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions", "https://openrouter.ai/api")]
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
    [InlineData("", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("Systran/faster-whisper-large-v3", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("whisper-1", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("whisper-large-v3-turbo", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("openai/whisper-large-v3", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("google/gemini-2.5-flash", "https://openrouter.ai/api", "google/gemini-2.5-flash")]
    [InlineData("google/gemini-2.5-pro", "https://openrouter.ai/api", "google/gemini-2.5-pro")]
    [InlineData("openai/gpt-4o-audio-preview", "https://openrouter.ai/api", "openai/gpt-4o-audio-preview")]
    [InlineData("whisper-1", "https://api.openai.com", "whisper-1")]
    public void NormalizeModelForEndpoint_UsesGeminiDefaultForBlankOrWhisperOnOpenRouter(string model, string url, string expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.NormalizeModelForEndpoint(model, url));
    }

    [Theory]
    [InlineData("wav", "wav")]
    [InlineData("flac", "flac")]
    [InlineData("opus", "ogg")]
    [InlineData("mp3", "mp3")]
    [InlineData(null, "wav")]
    public void InputAudioFormatForCodec_MapsUploadCodecsCleanly(string? codec, string expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.InputAudioFormatForCodec(codec));
    }

    [Fact]
    public void BuildOpenRouterChatPayload_BuildsExpectedInputAudioJson()
    {
        var prompt = RemoteWhisperProvider.BuildOpenRouterTranscriptionPrompt("ja", translate: true, "en");
        Assert.Contains("translate", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("English", prompt, StringComparison.OrdinalIgnoreCase);

        var bytes = new byte[] { 1, 2, 3, 4 };
        var json = RemoteWhisperProvider.BuildOpenRouterChatPayload("google/gemini-2.5-flash", prompt, bytes, "wav");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("google/gemini-2.5-flash", root.GetProperty("model").GetString());
        var msg = root.GetProperty("messages")[0];
        Assert.Equal("user", msg.GetProperty("role").GetString());
        var content = msg.GetProperty("content");
        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal(prompt, content[0].GetProperty("text").GetString());
        Assert.Equal("input_audio", content[1].GetProperty("type").GetString());
        Assert.Equal(Convert.ToBase64String(bytes), content[1].GetProperty("input_audio").GetProperty("data").GetString());
        Assert.Equal("wav", content[1].GetProperty("input_audio").GetProperty("format").GetString());
    }

    [Theory]
    [InlineData("```srt\n1\n00:00:00,000 --> 00:00:01,500\nHello world\n```", "1\n00:00:00,000 --> 00:00:01,500\nHello world")]
    [InlineData("```\n1\n00:00:00,000 --> 00:00:01,500\nHello world\n```", "1\n00:00:00,000 --> 00:00:01,500\nHello world")]
    [InlineData("Here is the SRT:\n```srt\n1\n00:00:00,000 --> 00:00:01,500\nHello world\n```", "1\n00:00:00,000 --> 00:00:01,500\nHello world")]
    [InlineData("1\n00:00:00,000 --> 00:00:01,500\nHello world", "1\n00:00:00,000 --> 00:00:01,500\nHello world")]
    public void StripMarkdownCodeFences_RemovesCodeFences(string raw, string expected)
    {
        Assert.Equal(expected, RemoteWhisperProvider.StripMarkdownCodeFences(raw));
    }

    [Fact]
    public void ParseOpenRouterSrtResponse_ParsesFencedSrtDotTimestampsAndNoSpeech()
    {
        var fenced = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "```srt\n1\n0:00:00.500 --> 0:00:02.250\nHello from OpenRouter\n```"
                    }
                }
            }
        });
        var srt = RemoteWhisperProvider.ParseOpenRouterSrtResponse(fenced, maxDurationSeconds: 10);
        Assert.Contains("00:00:00,500 --> 00:00:02,250", srt);
        Assert.Contains("Hello from OpenRouter", srt);

        var noSpeech = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = "NO_SPEECH" } }
            }
        });
        Assert.Equal(string.Empty, RemoteWhisperProvider.ParseOpenRouterSrtResponse(noSpeech, maxDurationSeconds: 10));
    }

    [Fact]
    public async Task RemoteWhisperProvider_OpenRouterTranscribeAndDetect_UseChatCompletionsInputAudio()
    {
        var srtReply = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "```srt\n1\n00:00:00,000 --> 00:00:01,000\nTranslated hello\n```"
                    }
                }
            }
        });
        var detectReply = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "```json\n{\"language\":\"ja\"}\n```"
                    }
                }
            }
        });

        var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(srtReply, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(detectReply, Encoding.UTF8, "application/json") });
        using var client = new HttpClient(handler);
        var provider = new RemoteWhisperProvider(
            NullLogger<RemoteWhisperProvider>.Instance,
            "https://openrouter.ai/api/v1",
            "",
            "sk-or-test-key",
            httpClient: client);

        var wav = Path.GetTempFileName();
        File.WriteAllBytes(wav, SyntheticAudio.SilentWav16kMono(1000));
        try
        {
            var srt = await provider.TranscribeAsync(wav, "ja", CancellationToken.None, translate: true, targetLanguage: "en");
            Assert.Contains("Translated hello", srt);

            var (lang, prob) = await provider.DetectLanguageAsync(wav, CancellationToken.None);
            Assert.Equal("ja", lang);
            Assert.True(prob > 0f);
        }
        finally
        {
            File.Delete(wav);
        }

        Assert.Equal(2, handler.Requests.Count);
        foreach (var req in handler.Requests)
        {
            Assert.Equal("/api/v1/chat/completions", req.Path);
            Assert.Equal("application/json", req.MediaType);
            Assert.Equal("Bearer sk-or-test-key", req.Auth);
            Assert.Equal("Jellyfin WhisperSubs", req.Title);
            Assert.Equal("https://github.com/esketids/whisper-subs", req.Referer);
            using var doc = JsonDocument.Parse(req.Body);
            Assert.Equal("google/gemini-2.5-flash", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal("input_audio", doc.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("type").GetString());
        }
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

            HostEngineGates.Apply(new PluginConfiguration
            {
                CudaDevices = "GPU-103dbd90-49e1-632c-fae7-523edbf218e3, GPU-dfa0c637-2519-d135-ae80-6ca96f949959, GPU-3f0aff19-6c6a-1877-0a9d-2324bcdcd0ca"
            });
            var u1 = new ProcessStartInfo();
            var u2 = new ProcessStartInfo();
            var u3 = new ProcessStartInfo();
            HostEngineGates.ApplyVulkanDevice(u1);
            HostEngineGates.ApplyVulkanDevice(u2);
            HostEngineGates.ApplyVulkanDevice(u3);
            Assert.Equal("GPU-103dbd90-49e1-632c-fae7-523edbf218e3", u1.Environment["CUDA_VISIBLE_DEVICES"]);
            Assert.Equal("GPU-dfa0c637-2519-d135-ae80-6ca96f949959", u2.Environment["CUDA_VISIBLE_DEVICES"]);
            Assert.Equal("GPU-3f0aff19-6c6a-1877-0a9d-2324bcdcd0ca", u3.Environment["CUDA_VISIBLE_DEVICES"]);
        }
        finally
        {
            HostEngineGates.Reset();
        }
    }

    [Fact]
    public void LocalTranscriptionWorker_ScalesMaxConcurrencyWithMaxFullModelEngines()
    {
        var cfg = new PluginConfiguration { MaxFullModelEngines = 4 };
        var provider = new WhisperProvider(
            NullLogger<WhisperProvider>.Instance,
            "/tmp/nonexistent-model.bin");
        var worker = new LocalTranscriptionWorker(provider, () => cfg, new CanaryInstallCheck(_ => false), _ => null);

        Assert.Equal(4, worker.Capabilities.MaxConcurrency);
    }

    [Fact]
    public void OpenRouterWorkerRow_WorksInWorkerPoolForTranscriptionAndEnglishTranslation()
    {
        var withEn = new WhisperWorker
        {
            ApiUrl = "https://openrouter.ai/api/v1",
            ApiKey = "sk-or-test",
            Model = "google/gemini-2.5-flash",
            CanTranslate = true,
            TranslateTargets = ["en"]
        };

        Assert.True(WorkerTargets.ForRow(withEn).SetEquals(["en"]));
        Assert.True(WorkerConfigValidation.Validate(withEn).Ok);

        var transcribeOnly = new WhisperWorker
        {
            ApiUrl = "https://openrouter.ai/api",
            ApiKey = "sk-or-test",
            Model = "",
            CanTranslate = false,
            TranslateTargets = []
        };

        Assert.Empty(WorkerTargets.ForRow(transcribeOnly));
        Assert.True(WorkerConfigValidation.Validate(transcribeOnly).Ok);
    }
}
