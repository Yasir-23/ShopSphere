using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace ShopSphere.Common.Resilience;

public static class ResilienceExtensions
{
    public const string DefaultHttpPipelineName = "ShopSphereHttpResiliencePipeline";

    /// <summary>
    /// Configures the standardized ShopSphere HTTP resilience pipeline on an HttpClient:
    /// - Max 50 concurrent requests (Rate Limiter / Bulkhead)
    /// - 5 seconds attempt timeout
    /// - Circuit breaker: 5 consecutive failures, 30s break duration, 3 trial attempts in half-open state
    /// - Exponential backoff retry: 3 retries (100ms base delay with jitter) handling 5xx, 408, HttpRequestException
    /// </summary>
    public static IHttpClientBuilder AddShopSphereResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler(DefaultHttpPipelineName, pipelineBuilder =>
        {
            // 1. Rate Limiter (Bulkhead) - Max 50 concurrent executions
            pipelineBuilder.AddConcurrencyLimiter(
                permitLimit: 50,
                queueLimit: 100);

            // 2. Request Timeout - 5 seconds
            pipelineBuilder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(5)
            });

            // 3. Retry Strategy - 3 retries, exponential backoff (100ms base delay with jitter)
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(100),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(response => (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
            });

            // 4. Circuit Breaker Strategy - 5 failures, 30s break duration, 3 trial attempts in half-open state
            pipelineBuilder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(response => (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
            });
        });

        return builder;
    }
}
