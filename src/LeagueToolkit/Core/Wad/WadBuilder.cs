using CommunityToolkit.Diagnostics;
using LeagueToolkit.Hashing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;

namespace LeagueToolkit.Core.Wad;

/// <summary>
/// Represents an entry to be added to a WAD archive during baking, resolving its data stream lazily.
/// </summary>
public record struct WadBakeEntry(string Path, Func<Stream> OpenStream, WadChunkCompression? Compression = null);

/// <summary>
/// Provides an interface for building a <see cref="WadFile"/>
/// </summary>
public static class WadBuilder
{
    /// <summary>
    /// Creates a new <see cref="WadFile"/> by baking it from the specified stream entries.
    /// </summary>
    /// <param name="entries">The entries containing paths and lazy stream factory callbacks to bake</param>
    /// <param name="outputStream">The target stream where the WAD file will be written</param>
    /// <param name="settings">The settings to use for the baking process</param>
    public static void Bake(
        IEnumerable<WadBakeEntry> entries,
        Stream outputStream,
        WadBakeSettings settings
    )
    {
        Guard.IsNotNull(entries, nameof(entries));
        Guard.IsNotNull(outputStream, nameof(outputStream));
        Guard.CanWrite(outputStream, nameof(outputStream));

        var entryList = entries.ToList();
        XxHash64 hasher = new();
        List<WadChunk> chunks = new();
        Dictionary<ulong, long> checksumOffsetLookup = new();

        long startOffset = outputStream.Position;
        outputStream.Seek(WadFile.HEADER_SIZE_V3 + (WadChunk.TOC_SIZE_V3 * entryList.Count), SeekOrigin.Current);

        foreach (var entry in entryList)
        {
            Guard.IsNotNullOrEmpty(entry.Path, nameof(entry.Path));
            Guard.IsNotNull(entry.OpenStream, nameof(entry.OpenStream));

            WadChunkCompression compression = entry.Compression ?? WadUtils.GetExtensionCompression(Path.GetExtension(entry.Path));

            // Lazily open the stream for the current entry
            using Stream contentStream = entry.OpenStream();
            Guard.IsNotNull(contentStream, nameof(contentStream));

            // Reset input stream to beginning if seekable
            if (contentStream.CanSeek)
                contentStream.Seek(0, SeekOrigin.Begin);

            // Compress the content
            using Stream compressedStream = CreateChunkStream(contentStream, compression);

            // Get the stream checksum and check for duplication
            ulong streamChecksum = CreateChunkChecksum(compressedStream, hasher);
            
            bool isDuplicated = false;
            long dataOffset = outputStream.Position;

            if (settings.DetectDuplicateChunkData)
            {
                if (checksumOffsetLookup.TryGetValue(streamChecksum, out long existingChunkOffset))
                {
                    dataOffset = existingChunkOffset;
                    isDuplicated = true;
                }
            }

            int uncompressedSize = (int)contentStream.Length;
            int compressedSize = (int)compressedStream.Length;

            if (isDuplicated is false)
            {
                compressedStream.CopyTo(outputStream);
            }

            string cleanPath = entry.Path.Replace('\\', '/').ToLowerInvariant();

            WadChunk chunk = new(
                XxHash64Ext.Hash(cleanPath),
                dataOffset - startOffset,
                compressedSize,
                uncompressedSize,
                compression,
                isDuplicated,
                0,
                0,
                streamChecksum
            );

            if (isDuplicated is false && settings.DetectDuplicateChunkData)
                checksumOffsetLookup.Add(streamChecksum, dataOffset);

            chunks.Add(chunk);
        }

        // Seek back to start of WAD and write descriptor
        outputStream.Seek(startOffset, SeekOrigin.Begin);
        using WadFile bakedWad = new(chunks);
        bakedWad.WriteDescriptor(outputStream);
    }

    /// <summary>
    /// Creates a new <see cref="WadFile"/> by baking it from the specified stream entries to a file path.
    /// </summary>
    /// <param name="entries">The entries containing paths and lazy stream factory callbacks to bake</param>
    /// <param name="outputPath">The target path where the WAD file will be created</param>
    /// <param name="settings">The settings to use for the baking process</param>
    public static void Bake(
        IEnumerable<WadBakeEntry> entries,
        string outputPath,
        WadBakeSettings settings
    )
    {
        Guard.IsNotNullOrEmpty(outputPath, nameof(outputPath));
        using FileStream outputFs = File.Create(outputPath);
        Bake(entries, outputFs, settings);
    }

    /// <summary>
    /// Creates a new <see cref="WadFile"/> by "baking" it using the specified parameters
    /// </summary>
    /// <param name="files">The files to add to the <see cref="WadFile"/></param>
    /// <param name="rootDirectory">The root directory of the files to add</param>
    /// <param name="output">The path to the created <see cref="WadFile"/></param>
    /// <param name="settings">The settings to use for the baking process</param>
    public static void BakeFiles(
        IEnumerable<string> files,
        string rootDirectory,
        string output,
        WadBakeSettings settings
    )
    {
        Guard.IsNotNull(files, nameof(files));
        Guard.IsNotNull(rootDirectory, nameof(rootDirectory));
        Guard.IsNotNullOrEmpty(output, nameof(output));

        var entries = files.Select(f =>
        {
            string relativePath = Path.GetRelativePath(rootDirectory, f);
            return new WadBakeEntry(relativePath, () => File.OpenRead(f));
        }).ToList();

        Bake(entries, output, settings);
    }

    private static Stream CreateChunkStream(Stream stream, WadChunkCompression chunkCompression)
    {
        if (chunkCompression is WadChunkCompression.None)
            return stream;

        // Pre-size the memory stream if the input stream supports seeking to avoid early redundant resizing
        int initialCapacity = stream.CanSeek ? (int)stream.Length : 0;
        MemoryStream compressedStream = initialCapacity > 0 ? new MemoryStream(initialCapacity) : new MemoryStream();

        using Stream compressionStream = chunkCompression switch
        {
            WadChunkCompression.GZip => new GZipStream(compressedStream, CompressionMode.Compress),
            WadChunkCompression.Zstd => new ZstdSharp.CompressionStream(compressedStream),
            _ => throw new InvalidOperationException($"Invalid chunk compression: {chunkCompression}")
        };

        stream.CopyTo(compressionStream);
        return compressedStream;
    }

    private static ulong CreateChunkChecksum(Stream compressedStream, XxHash64 hasher)
    {
        compressedStream.Seek(0, SeekOrigin.Begin);
        hasher.Append(compressedStream);
        compressedStream.Seek(0, SeekOrigin.Begin);

        ulong checksum = hasher.GetCurrentHashAsUInt64();
        hasher.Reset();
        return checksum;
    }
}

public struct WadBakeSettings
{
    public bool DetectDuplicateChunkData { get; set; }

    public WadBakeSettings()
    {
        this.DetectDuplicateChunkData = true;
    }
}
