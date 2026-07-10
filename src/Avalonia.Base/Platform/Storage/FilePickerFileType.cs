using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Platform.Storage;

/// <summary>
/// Represents a name mapped to the associated file types (extensions).
/// </summary>
public sealed class FilePickerFileType(string? name)
{
    /// <summary>
    /// File type name.
    /// </summary>
    public string Name { get; } = name ?? string.Empty;

    /// <summary>
    /// List of extensions in GLOB format. I.e. "*.png" or "*.*".
    /// </summary>
    /// <remarks>
    /// Used on Windows, Linux and Browser platforms.
    /// </remarks>
    public IReadOnlyList<string>? Patterns { get; set; }

    /// <summary>
    /// List of extensions in MIME format.
    /// </summary>
    /// <remarks>
    /// Used on Android, Linux and Browser platforms.
    /// </remarks>
    public IReadOnlyList<string>? MimeTypes { get; set; }

    /// <summary>
    /// List of extensions in Apple uniform format.
    /// </summary>
    /// <remarks>
    /// Used only on Apple devices.
    /// See https://developer.apple.com/documentation/uniformtypeidentifiers/system_declared_uniform_type_identifiers.
    /// </remarks>
    public IReadOnlyList<string>? AppleUniformTypeIdentifiers { get; set; }

    internal IReadOnlyList<string>? TryGetExtensions()
    {
        return Patterns?
            .Select(TryGetExtension)
            .Where(e => !string.IsNullOrEmpty(e))!
            .ToArray<string>();
    }

    /// <summary>
    /// Converts a glob pattern to a simple extension name.
    /// Tries to return as many extensions as possible that don't contain a pattern (e.g. "*.*abc*.def.ghi" returns "def.ghi").
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>An extension, if available, without a starting dot, or null if no valid extension was found.</returns>
    internal static string? TryGetExtension(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return null;

        var previousDotIndex = -1;
        var dotIndex = pattern.LastIndexOf('.', pattern.Length - 1);

        while (dotIndex >= 0)
        {
            var extension = pattern.AsSpan(dotIndex + 1);

            if (extension.IsEmpty || extension.Contains('*'))
                break;

            previousDotIndex = dotIndex;
            if (previousDotIndex == 0)
                break;

            dotIndex = pattern.LastIndexOf('.', previousDotIndex - 1);
        }

        return previousDotIndex >= 0 ? pattern.Substring(previousDotIndex + 1) : null;
    }

    /// <inheritdoc />
    public override string ToString() => Name;
}
