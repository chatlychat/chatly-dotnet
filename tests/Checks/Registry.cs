using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chatly.Tests;

/// <summary>One named check.</summary>
public sealed class Check
{
    public Check(string name, Func<Task> run)
    {
        Name = name;
        Run = run;
    }

    /// <summary>The name both runners print.</summary>
    public string Name { get; }

    /// <summary>Runs the check; throws <see cref="CheckFailedException"/> on failure.</summary>
    public Func<Task> Run { get; }
}

/// <summary>
/// The single list of checks, shared by the xunit project and the
/// dependency-free <c>Verify</c> console.
/// </summary>
/// <remarks>
/// Both runners enumerate this, so neither can quietly run fewer checks than
/// the other. Names are prefixed with the CONTRACT.md section they come from.
/// </remarks>
public static class CheckRegistry
{
    /// <summary>Every check, in contract order.</summary>
    public static IReadOnlyList<Check> All { get; } = new List<Check>
    {
        new Check("§10.1 default base URL is the production host", ContractChecks.DefaultBaseUrlIsProduction),
        new Check("§1 a self-hosted base URL keeps its path prefix", ContractChecks.BaseUrlPathPrefixIsKept),
        new Check("§1 an empty API key is rejected at construction", ContractChecks.EmptyApiKeyIsRejectedAtConstruction),

        new Check("§10.2 Authorization carries the key", ContractChecks.AuthorizationCarriesTheKey),
        new Check("§2 a User-Agent suffix is appended, not substituted", ContractChecks.UserAgentSuffixIsAppended),

        new Check("§10.3 a write sends an Idempotency-Key, a read does not", ContractChecks.WriteSendsAnIdempotencyKey),
        new Check("§10.3 two writes send different keys", ContractChecks.TwoWritesSendDifferentIdempotencyKeys),
        new Check("§10.3 a pinned key is sent twice", ContractChecks.PinnedIdempotencyKeyRepeats),
        new Check("§10.3 a derived client rebinds every write namespace", ContractChecks.DerivedClientRebindsEveryWriteNamespace),

        new Check("§10.4 statuses map to typed errors with a request id", ContractChecks.StatusCodesMapToTypedErrors),
        new Check("§10.4 a 429 carries retry_after", ContractChecks.RateLimitCarriesRetryAfter),
        new Check("§4 Retry-After accepts an HTTP-date", ContractChecks.RetryAfterAcceptsAnHttpDate),
        new Check("§3 a transport failure is its own type", ContractChecks.TransportFailureIsItsOwnType),
        new Check("§3 a caller's cancellation is not disguised", ContractChecks.CallerCancellationPropagates),

        new Check("§10.5 a 429 is retried and then succeeds", ContractChecks.RateLimitIsRetriedThenSucceeds),
        new Check("§10.5 a 5xx is retried", ContractChecks.ServerErrorIsRetried),
        new Check("§10.5 a 400 is not retried", ContractChecks.ValidationErrorIsNotRetried),
        new Check("§10.5 a 404 is not retried", ContractChecks.NotFoundIsNotRetried),
        new Check("§4 retries stop at MaxAttempts", ContractChecks.RetriesStopAtMaxAttempts),

        new Check("§10.6 the paginator walks two pages and stops", ContractChecks.PaginatorWalksTwoPagesAndStops),
        new Check("§10.6 the paginator stops on a repeated cursor", ContractChecks.PaginatorStopsOnARepeatedCursor),
        new Check("§5 a single page is returned as sent", ContractChecks.SinglePageIsReturnedAsIs),
        new Check("§5 a non-page response throws instead of reading empty", ContractChecks.NonPageResponseThrows),
        new Check("§2 repeated query keys stay repeated", ContractChecks.RepeatedQueryKeysStayRepeated),

        new Check("§10.7 a genuine signature is accepted", CryptoChecks.GenuineSignatureIsAccepted),
        new Check("§10.7 a stale timestamp is rejected", CryptoChecks.StaleTimestampIsRejected),
        new Check("§10.7 malformed headers return false and never throw", CryptoChecks.MalformedSignatureHeadersReturnFalse),
        new Check("§10.9 the pinned delivery verifies verbatim", CryptoChecks.PinnedWebhookDeliveryVerifies),
        new Check("§10.9 a digest over the body alone is rejected", CryptoChecks.BodyOnlyDigestIsRejected),

        new Check("§10.8 identity_hash matches the pinned vector", CryptoChecks.IdentityHashMatchesThePinnedVector),

        new Check("§8 the escape hatch uses the same pipeline", ContractChecks.EscapeHatchUsesTheSamePipeline),
        new Check("§2 an empty response body decodes as JSON null", ContractChecks.EmptyResponseBodyDecodesAsNull),
        new Check("§2 a bodyless write sends an empty JSON object", ContractChecks.BodylessWriteSendsAnEmptyObject),
        new Check("§2 null fields are omitted from bodies", ContractChecks.NullFieldsAreOmittedFromBodies),
        new Check("§2 path parameters are percent-encoded", ContractChecks.PathParametersAreEncoded),

        new Check("surface.json: the table covers all 55 methods", SurfaceChecks.TableCoversTheWholeSpec),
        new Check("surface.json: every method hits its specified route", SurfaceChecks.EveryMethodHitsTheSpecifiedRoute),
        new Check("surface.json: every paged entry has an auto-paginator", SurfaceChecks.EveryPagedEntryHasAnAutoPaginator),
        new Check("§8 the escape hatch reaches unmodelled routes", SurfaceChecks.EscapeHatchReachesUnmodelledRoutes),
        new Check("surface.lock.json is regenerated and equals the manifest", SurfaceLock.GenerateAndAssertAsync),
    };

    /// <summary>The names, for xunit's test data.</summary>
    public static IEnumerable<object[]> Names => All.Select(check => new object[] { check.Name });

    /// <summary>Look a check up by name.</summary>
    public static Check Get(string name)
        => All.FirstOrDefault(check => check.Name == name)
           ?? throw new CheckFailedException($"no such check: {name}");
}
