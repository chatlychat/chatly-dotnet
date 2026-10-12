using System;

namespace Chatly.Resources;

/// <summary>
/// Path-segment handling shared by every resource.
/// </summary>
internal static class Route
{
    /// <summary>
    /// Percent-encode one value for use as a path segment, and refuse an
    /// empty one.
    /// </summary>
    /// <remarks>
    /// Encoding matters beyond tidiness: a tag is a path segment on
    /// <c>DELETE /v1/contacts/{id}/tags/{tag}</c>, the API decodes that
    /// segment before matching, and a tag containing a space or a slash would
    /// otherwise never match the one stored — or would address a different
    /// route entirely.
    /// </remarks>
    /// <param name="value">The raw value.</param>
    /// <param name="parameterName">For the exception message.</param>
    /// <returns>The encoded segment.</returns>
    /// <exception cref="ArgumentException">The value is null, empty or whitespace.</exception>
    internal static string Segment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{parameterName} is required and cannot be empty.",
                parameterName);
        }

        return Uri.EscapeDataString(value);
    }
}
