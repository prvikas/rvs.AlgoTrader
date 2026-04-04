using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace rvs.AlgoTrader.Infrastructure.Extensions;

/// <summary>
/// Shared Polly resilience policies for broker HTTP clients (AP-010).
///
/// Retry:          3 attempts with exponential back-off (1s, 2s, 4s ± jitter).
///                 Triggers on transient HTTP errors (5xx, timeout, network failure).
///
/// CircuitBreaker: Opens after ≥50% failure rate in a 30-second sampling window
///                 (minimum 10 requests). Stays open for 30 seconds before half-open probe.
///                 Shared singleton — trip once, protect all requests to that broker.
/// </summary>
internal static class BrokerResiliencePolicies
{
    // IAsyncPolicy<HttpResponseMessage> — compatible with AddPolicyHandler from
    // Microsoft.Extensions.Http.Polly. Polly v8 exposes AsAsyncPolicy() on
    // ResiliencePipeline<T> for backward compatibility with HttpClient integration.

    public static readonly IAsyncPolicy<HttpResponseMessage> Retry =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay             = TimeSpan.FromSeconds(1),
                BackoffType       = DelayBackoffType.Exponential,
                UseJitter         = true,
                ShouldHandle      = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500
                                    || r.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
            })
            .Build()
            .AsAsyncPolicy();

    public static readonly IAsyncPolicy<HttpResponseMessage> CircuitBreaker =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio      = 0.5,                       // 50% failure rate …
                SamplingDuration  = TimeSpan.FromSeconds(30),  // … in a 30s window
                MinimumThroughput = 10,                        // … with at least 10 calls
                BreakDuration     = TimeSpan.FromSeconds(30),  // stay open for 30s
                ShouldHandle      = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500)
            })
            .Build()
            .AsAsyncPolicy();
}
