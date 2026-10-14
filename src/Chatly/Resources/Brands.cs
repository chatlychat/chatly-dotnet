using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Chatly.Resources;

/// <summary>
/// Brands — optional sub-tenants inside one workspace, for a customer running
/// several products off one account. Reached as <c>chatly.Brands</c>.
/// </summary>
/// <remarks>
/// A brand carries its own name, logo, colour, support address and mail
/// domain. Two domain-ish fields are easy to conflate and are not the same
/// thing: <c>domain</c> is the brand's help-centre hostname, while
/// <c>replyDomain</c> is where notification replies are addressed — pointing
/// an MX record at a help-centre hostname is the mistake the split exists to
/// prevent.
/// </remarks>
public sealed class BrandsResource
{
    private const string Collection = "/v1/brands";

    private readonly Transport _transport;

    internal BrandsResource(Transport transport) => _transport = transport;

    /// <summary>Every brand in the workspace. <c>GET /v1/brands</c>.</summary>
    /// <remarks>
    /// <b>A bare JSON array</b>, pagination-exempt, and each row carries
    /// usage counts — so this is also the cheapest way to find out whether a
    /// brand is safe to delete.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A JSON array of brands with usage counts.</returns>
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default)
        => _transport.GetJsonAsync(Collection, null, cancellationToken);

    /// <summary>Create a brand. <c>POST /v1/brands</c>.</summary>
    /// <remarks>
    /// <c>slug</c> is required and constrained to lowercase alphanumerics and
    /// hyphens — it appears in URLs — and <c>primaryColor</c> must be
    /// <c>#RRGGBB</c>. Both are 400s rather than coercions.
    /// </remarks>
    /// <param name="brand">The brand to create, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created brand, at 201.</returns>
    public Task<JsonElement> CreateAsync(object brand, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(HttpMethod.Post, Collection, brand, null, cancellationToken);

    /// <summary>Change part of a brand. <c>PATCH /v1/brands/{id}</c>.</summary>
    /// <param name="brandId">The brand to change.</param>
    /// <param name="changes">The fields to change, serialized as the request body.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated brand.</returns>
    public Task<JsonElement> UpdateAsync(
        string brandId,
        object changes,
        CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            new HttpMethod("PATCH"),
            $"{Collection}/{Route.Segment(brandId, nameof(brandId))}",
            changes,
            null,
            cancellationToken);

    /// <summary>Soft-delete a brand. <c>DELETE /v1/brands/{id}</c>.</summary>
    /// <remarks>
    /// A <b>soft</b> delete that answers <b>200 with the id</b>, not a 204.
    /// The conversations, tickets and help-centre content that referenced the
    /// brand keep referencing it; nothing is reassigned for you.
    /// </remarks>
    /// <param name="brandId">The brand to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The id of the soft-deleted brand.</returns>
    public Task<JsonElement> DeleteAsync(string brandId, CancellationToken cancellationToken = default)
        => _transport.WriteJsonAsync(
            HttpMethod.Delete,
            $"{Collection}/{Route.Segment(brandId, nameof(brandId))}",
            null,
            null,
            cancellationToken);
}
