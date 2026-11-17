using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Itineraries;

public sealed class GeminiItineraryGenerator(HttpClient client, IOptions<GeminiOptions> options) : IItineraryGenerator
{
    private const int MaximumResponseBytes = 1024 * 1024;

    public async Task<IReadOnlyList<ItineraryDayDto>> GenerateAsync(TripInput trip, CancellationToken cancellationToken = default)
    {
        ItineraryValidation.ValidateInput(trip);
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Any(char.IsControl))
            throw new ItineraryGenerationException(GenerationFailure.NotConfigured);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(settings.Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = JsonContent.Create(ItineraryPrompt.CreateRequest(trip, settings));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode)
                throw new ItineraryGenerationException(response.StatusCode == HttpStatusCode.TooManyRequests
                    ? GenerationFailure.RateLimited : GenerationFailure.Unavailable);
            var payload = await ReadBoundedAsync(response.Content, deadline.Token);
            using var envelope = JsonDocument.Parse(payload);
            var root = envelope.RootElement;
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
                throw new ItineraryGenerationException(GenerationFailure.Blocked);
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() != 1)
                throw new ItineraryGenerationException(GenerationFailure.InvalidResponse);
            var candidate = candidates[0];
            var finishReason = candidate.GetProperty("finishReason").GetString();
            if (finishReason != "STOP")
                throw new ItineraryGenerationException(finishReason is "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT"
                    ? GenerationFailure.Blocked : GenerationFailure.InvalidResponse);
            var parts = candidate.GetProperty("content").GetProperty("parts");
            var text = string.Concat(parts.EnumerateArray()
                .Where(part => !(part.TryGetProperty("thought", out var thought) && thought.GetBoolean()))
                .Select(part => part.GetProperty("text").GetString()));
            var draft = JsonSerializer.Deserialize<ItineraryDraftDto>(text, ItineraryPrompt.JsonOptions);
            var days = ItineraryValidation.NormalizeTimeSlots(draft?.Days);
            return days;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ItineraryGenerationException(GenerationFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            throw new ItineraryGenerationException(GenerationFailure.Unavailable);
        }
        catch (IOException)
        {
            throw new ItineraryGenerationException(GenerationFailure.Unavailable);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Provider payloads can contain user input. Never expose them in errors or logs.
            throw new ItineraryGenerationException(GenerationFailure.InvalidResponse);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new ItineraryGenerationException(GenerationFailure.InvalidResponse);
        using var buffer = new MemoryStream();
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
                throw new ItineraryGenerationException(GenerationFailure.InvalidResponse);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
