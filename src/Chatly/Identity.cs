using System;
using System.Security.Cryptography;
using System.Text;

namespace Chatly;

/// <summary>
/// The backend half of the widget's identity verification.
/// </summary>
/// <remarks>
/// <para>
/// When a workspace turns identity verification on, the widget must prove
/// that the <c>externalId</c> it claims was issued by your backend rather than
/// typed into a browser console — otherwise anyone can open a chat as anyone
/// else's account. Your server computes the digest with the workspace's
/// identity-verification secret and renders it into the page; the widget sends
/// it along with the id.
/// </para>
/// <para>
/// The secret stays on your server. Only the digest reaches the browser.
/// </para>
/// </remarks>
public static class ChatlyIdentity
{
    /// <summary>
    /// Compute the <c>userHash</c> for one external id.
    /// </summary>
    /// <example>
    /// <code>
    /// var userHash = ChatlyIdentity.Hash(
    ///     Environment.GetEnvironmentVariable("CHATLY_IDV_SECRET")!,
    ///     user.Id);
    ///
    /// // then render into the page:
    /// // Chatly('identify', { externalId: '…', userHash: '…', email: '…' })
    /// </code>
    /// </example>
    /// <param name="secret">
    /// The workspace's identity-verification secret (<c>sek_…</c>), from
    /// <b>Workspace → Settings → Widget → Identity verification</b>. Never
    /// send this to a browser.
    /// </param>
    /// <param name="externalId">
    /// The id your system uses for this person — the same value you pass as
    /// <c>externalId</c> to the widget.
    /// </param>
    /// <returns>Lowercase hex HMAC-SHA256 of <paramref name="externalId"/> under <paramref name="secret"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Either argument is empty. An empty secret would produce a digest that
    /// verifies nothing, and an empty id would authenticate the anonymous
    /// visitor as a real account; both are programming errors, so they throw
    /// rather than returning a useless string.
    /// </exception>
    public static string Hash(string secret, string externalId)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException(
                "ChatlyIdentity.Hash: the identity-verification secret is empty.",
                nameof(secret));
        }

        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException(
                "ChatlyIdentity.Hash: externalId is empty.",
                nameof(externalId));
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(externalId));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
