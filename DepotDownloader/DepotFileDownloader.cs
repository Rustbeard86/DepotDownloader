using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.CDN;

namespace DepotDownloader.Lib;

/// <summary>
///     Handles file and chunk download operations for Steam depot content.
/// </summary>
internal static class DepotFileDownloader
{
    /// <summary>
    ///     Downloads all files for a depot, including chunk validation and delta updates.
    /// </summary>
    internal static async Task DownloadDepotFilesAsync(
        CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter,
        DepotFilesData depotFilesData,
        HashSet<string> allFileNamesAllDepots,
        CdnClientPool cdnPool,
        Steam3Session steam3,
        DownloadConfig config,
        IUserInterface userInterface,
        DownloadProgressContext progressContext = null)
    {
        var depot = depotFilesData.DepotDownloadInfo;
        var depotCounter = depotFilesData.DepotCounter;

        userInterface?.WriteLine("Downloading depot {0}", depot.DepotId);

        var files = depotFilesData.FilteredFiles.Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory)).ToArray();
        var networkChunkQueue =
            new ConcurrentQueue<(FileStreamData fileStreamData, DepotManifest.FileData fileData, DepotManifest.ChunkData
                chunk)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = config.MaxDownloads,
            CancellationToken = cts.Token
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, _) =>
        {
            await Task.Yield();
            ProcessDepotFile(cts, downloadCounter, depotFilesData, file, networkChunkQueue, config, userInterface);
        });

        await Parallel.ForEachAsync(networkChunkQueue, parallelOptions, async (q, _) =>
        {
            await DownloadChunkAsync(
                cts, downloadCounter, depotFilesData,
                q.fileData, q.fileStreamData, q.chunk,
                cdnPool, steam3, userInterface, progressContext
            );
        });

        // Check for deleted files if updating the depot.
        if (depotFilesData.PreviousManifest is { Files: not null })
        {
            var previousFilteredFiles = depotFilesData.PreviousManifest.Files.AsParallel()
                .Where(f => FileFilter.TestIsFileIncluded(f.FileName, config)).Select(f => f.FileName).ToHashSet();

            // Check if we are writing to a single output directory. If not, each depot folder is managed independently
            previousFilteredFiles.ExceptWith(string.IsNullOrWhiteSpace(config.InstallDirectory)
                ? depotFilesData.AllFileNames
                : allFileNamesAllDepots);

            foreach (var existingFileName in previousFilteredFiles)
            {
                var fileFinalPath = Path.Combine(depot.InstallDir, existingFileName);

                if (!File.Exists(fileFinalPath))
                    continue;

                File.Delete(fileFinalPath);
                userInterface?.WriteLine("Deleted {0}", fileFinalPath);
            }
        }

        DepotConfigStore.Instance.InstalledManifestIDs[depot.DepotId] = depot.ManifestId;
        DepotConfigStore.Save();

        userInterface?.WriteLine("Depot {0} - Downloaded {1} bytes ({2} bytes uncompressed)", depot.DepotId,
            depotCounter.DepotBytesCompressed, depotCounter.DepotBytesUncompressed);
    }

    /// <summary>
    ///     Processes a single file, determining which chunks need downloading.
    /// </summary>
    private static void ProcessDepotFile(
        CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter,
        DepotFilesData depotFilesData,
        DepotManifest.FileData file,
        ConcurrentQueue<(FileStreamData, DepotManifest.FileData, DepotManifest.ChunkData)> networkChunkQueue,
        DownloadConfig config,
        IUserInterface userInterface)
    {
        cts.Token.ThrowIfCancellationRequested();

        var depot = depotFilesData.DepotDownloadInfo;
        var stagingDirPath = depotFilesData.StagingDirectoryPath;
        var depotDownloadCounter = depotFilesData.DepotCounter;
        var oldProtoManifest = depotFilesData.PreviousManifest;
        DepotManifest.FileData oldManifestFile = null;
        if (oldProtoManifest?.Files is not null)
            oldManifestFile = oldProtoManifest.Files.SingleOrDefault(f => f.FileName == file.FileName);

        var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
        var fileStagingPath = Path.Combine(stagingDirPath, file.FileName);

        // This may still exist if the previous run exited before cleanup
        if (File.Exists(fileStagingPath)) File.Delete(fileStagingPath);

        List<DepotManifest.ChunkData> neededChunks;
        var fi = new FileInfo(fileFinalPath);
        var fileDidExist = fi.Exists;
        if (!fileDidExist)
        {
            userInterface?.WriteLine("Pre-allocating {0}", fileFinalPath);

            // create new file. need all chunks
            using var fs = File.Create(fileFinalPath);
            try
            {
                fs.SetLength((long)file.TotalSize);
            }
            catch (IOException ex)
            {
                throw new ContentDownloaderException($"Failed to allocate file {fileFinalPath}: {ex.Message}");
            }

            neededChunks = [.. file.Chunks];
        }
        else
        {
            // open existing
            if (oldManifestFile is not null)
            {
                neededChunks = [];

                var hashMatches = oldManifestFile.FileHash.SequenceEqual(file.FileHash);
                if (config.VerifyAll || !hashMatches)
                {
                    // we have a version of this file, but it doesn't fully match what we want
                    if (config.VerifyAll) userInterface?.WriteLine("Validating {0}", fileFinalPath);

                    var matchingChunks = new List<ChunkMatch>();

                    foreach (var chunk in file.Chunks)
                    {
                        var oldChunk =
                            oldManifestFile.Chunks.FirstOrDefault(c =>
                                chunk.ChunkID is not null && c.ChunkID is not null &&
                                c.ChunkID.SequenceEqual(chunk.ChunkID));
                        if (oldChunk is not null)
                            matchingChunks.Add(new ChunkMatch(oldChunk, chunk));
                        else
                            neededChunks.Add(chunk);
                    }

                    var orderedChunks = matchingChunks.OrderBy(x => x.OldChunk.Offset);

                    var copyChunks = new List<ChunkMatch>();

                    using (var fsOld = File.Open(fileFinalPath, FileMode.Open))
                    {
                        foreach (var match in orderedChunks)
                        {
                            fsOld.Seek((long)match.OldChunk.Offset, SeekOrigin.Begin);

                            var adler = Util.AdlerHash(fsOld, (int)match.OldChunk.UncompressedLength);
                            if (!adler.SequenceEqual(BitConverter.GetBytes(match.OldChunk.Checksum)))
                                neededChunks.Add(match.NewChunk);
                            else
                                copyChunks.Add(match);
                        }
                    }

                    if (!hashMatches || neededChunks.Count > 0)
                    {
                        File.Move(fileFinalPath, fileStagingPath);

                        using (var fsOld = File.Open(fileStagingPath, FileMode.Open))
                        {
                            using var fs = File.Open(fileFinalPath, FileMode.Create);
                            try
                            {
                                fs.SetLength((long)file.TotalSize);
                            }
                            catch (IOException ex)
                            {
                                throw new ContentDownloaderException(
                                    $"Failed to resize file to expected size {fileFinalPath}: {ex.Message}");
                            }

                            foreach (var match in copyChunks)
                            {
                                fsOld.Seek((long)match.OldChunk.Offset, SeekOrigin.Begin);

                                var tmp = new byte[match.OldChunk.UncompressedLength];
                                fsOld.ReadExactly(tmp);

                                fs.Seek((long)match.NewChunk.Offset, SeekOrigin.Begin);
                                fs.Write(tmp, 0, tmp.Length);
                            }
                        }

                        File.Delete(fileStagingPath);
                    }
                }
            }
            else
            {
                // No old manifest or file not in old manifest. We must validate.

                using var fs = File.Open(fileFinalPath, FileMode.Open);
                if ((ulong)fi.Length != file.TotalSize)
                    try
                    {
                        fs.SetLength((long)file.TotalSize);
                    }
                    catch (IOException ex)
                    {
                        throw new ContentDownloaderException($"Failed to allocate file {fileFinalPath}: {ex.Message}");
                    }

                userInterface?.WriteLine("Validating {0}", fileFinalPath);
                neededChunks = Util.ValidateSteam3FileChecksums(fs, [.. file.Chunks.OrderBy(x => x.Offset)]);
            }

            if (neededChunks.Count == 0)
            {
                lock (depotDownloadCounter)
                {
                    depotDownloadCounter.SizeDownloaded += file.TotalSize;
                    userInterface?.WriteLine("{0,6:#00.00}% {1}",
                        depotDownloadCounter.SizeDownloaded / (float)depotDownloadCounter.CompleteDownloadSize * 100.0f,
                        fileFinalPath);
                }

                lock (downloadCounter)
                {
                    downloadCounter.CompleteDownloadSize -= file.TotalSize;
                }

                return;
            }

            var sizeOnDisk = file.TotalSize - (ulong)neededChunks.Select(x => (long)x.UncompressedLength).Sum();
            lock (depotDownloadCounter)
            {
                depotDownloadCounter.SizeDownloaded += sizeOnDisk;
            }

            lock (downloadCounter)
            {
                downloadCounter.CompleteDownloadSize -= sizeOnDisk;
            }
        }

        var fileIsExecutable = file.Flags.HasFlag(EDepotFileFlag.Executable);
        switch (fileIsExecutable)
        {
            case true when !fileDidExist || oldManifestFile is null ||
                           !oldManifestFile.Flags.HasFlag(EDepotFileFlag.Executable):
                PlatformUtilities.SetExecutable(fileFinalPath, true);
                break;
            case false when oldManifestFile is not null &&
                            oldManifestFile.Flags.HasFlag(EDepotFileFlag.Executable):
                PlatformUtilities.SetExecutable(fileFinalPath, false);
                break;
        }

        var fileStreamData = new FileStreamData
        {
            ChunksToDownload = neededChunks.Count
        };

        foreach (var chunk in neededChunks) networkChunkQueue.Enqueue((fileStreamData, file, chunk));
    }

    /// <summary>
    ///     Downloads a single chunk from the CDN.
    /// </summary>
    private static async Task DownloadChunkAsync(
        CancellationTokenSource cts,
        GlobalDownloadCounter downloadCounter,
        DepotFilesData depotFilesData,
        DepotManifest.FileData file,
        FileStreamData fileStreamData,
        DepotManifest.ChunkData chunk,
        CdnClientPool cdnPool,
        Steam3Session steam3,
        IUserInterface userInterface,
        DownloadProgressContext progressContext = null)
    {
        cts.Token.ThrowIfCancellationRequested();

        var depot = depotFilesData.DepotDownloadInfo;
        var depotDownloadCounter = depotFilesData.DepotCounter;

        if (chunk.ChunkID is null) return;

        var chunkId = Convert.ToHexString(chunk.ChunkID).ToLowerInvariant();

        var written = 0;
        var chunkBuffer = ArrayPool<byte>.Shared.Rent((int)chunk.UncompressedLength);

        try
        {
            do
            {
                cts.Token.ThrowIfCancellationRequested();

                Server connection = null;

                try
                {
                    connection = cdnPool.GetConnection();

                    string cdnToken = null;
                    if (steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                            out var authTokenCallbackPromise))
                    {
                        var result = await authTokenCallbackPromise.Task;
                        cdnToken = result.Token;
                    }

                    DebugLog.WriteLine("DepotFileDownloader", "Downloading chunk {0} from {1} with {2}", chunkId,
                        connection, cdnPool.ProxyServer is not null ? cdnPool.ProxyServer : "no proxy");
                    written = await cdnPool.CdnClient.DownloadDepotChunkAsync(
                        depot.DepotId,
                        chunk,
                        connection,
                        chunkBuffer,
                        depot.DepotKey,
                        cdnPool.ProxyServer,
                        cdnToken).ConfigureAwait(false);

                    cdnPool.ReturnConnection(connection);

                    break;
                }
                catch (TaskCanceledException)
                {
                    userInterface?.WriteLine("Connection timeout downloading chunk {0}", chunkId);
                    cdnPool.ReturnBrokenConnection(connection);
                }
                catch (SteamKitWebRequestException e)
                {
                    // If the CDN returned 403, attempt to get a cdn auth if we didn't yet
                    if (e.StatusCode == HttpStatusCode.Forbidden &&
                        connection is not null &&
                        (!steam3.CdnAuthTokens.TryGetValue((depot.DepotId, connection.Host),
                            out var authTokenCallbackPromise) || !authTokenCallbackPromise.Task.IsCompleted))
                    {
                        await steam3.RequestCdnAuthToken(depot.AppId, depot.DepotId, connection);

                        cdnPool.ReturnConnection(connection);

                        continue;
                    }

                    cdnPool.ReturnBrokenConnection(connection);

                    if (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        userInterface?.WriteLine("Encountered {1} for chunk {0}. Aborting.", chunkId,
                            (int)e.StatusCode);
                        break;
                    }

                    userInterface?.WriteLine("Encountered error downloading chunk {0}: {1}", chunkId,
                        e.StatusCode);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    cdnPool.ReturnBrokenConnection(connection);
                    userInterface?.WriteLine("Encountered unexpected error downloading chunk {0}: {1}", chunkId,
                        e.Message);
                }
            } while (written == 0);

            if (written == 0)
            {
                userInterface?.WriteLine("Failed to find any server with chunk {0} for depot {1}. Aborting.",
                    chunkId,
                    depot.DepotId);
                // ReSharper disable once MethodHasAsyncOverload
                cts.Cancel();
            }

            // Throw the cancellation exception if requested so that this task is marked failed
            cts.Token.ThrowIfCancellationRequested();

            try
            {
                await fileStreamData.FileLock.WaitAsync().ConfigureAwait(false);

                if (fileStreamData.FileStream is null)
                {
                    var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
                    fileStreamData.FileStream = File.Open(fileFinalPath, FileMode.Open);
                }

                fileStreamData.FileStream.Seek((long)chunk.Offset, SeekOrigin.Begin);
                await fileStreamData.FileStream.WriteAsync(chunkBuffer.AsMemory(0, written), cts.Token);
            }
            finally
            {
                fileStreamData.FileLock.Release();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }

        var remainingChunks = Interlocked.Decrement(ref fileStreamData.ChunksToDownload);
        if (remainingChunks == 0)
        {
            // ReSharper disable once MethodHasAsyncOverload
            fileStreamData.FileStream?.Dispose();
            fileStreamData.FileLock.Dispose();
        }

        ulong sizeDownloaded;
        ulong totalBytesDownloaded;
        lock (depotDownloadCounter)
        {
            sizeDownloaded = depotDownloadCounter.SizeDownloaded + (ulong)written;
            depotDownloadCounter.SizeDownloaded = sizeDownloaded;
            depotDownloadCounter.DepotBytesCompressed += chunk.CompressedLength;
            depotDownloadCounter.DepotBytesUncompressed += chunk.UncompressedLength;
        }

        lock (downloadCounter)
        {
            downloadCounter.TotalBytesCompressed += chunk.CompressedLength;
            downloadCounter.TotalBytesUncompressed += chunk.UncompressedLength;
            totalBytesDownloaded = downloadCounter.TotalBytesDownloaded += chunk.UncompressedLength;
        }

        // Update global progress
        userInterface?.UpdateProgress(totalBytesDownloaded, downloadCounter.TotalDownloadSize);

        // Fire progress event if context is provided
        var fileCompleted = remainingChunks == 0;
        progressContext?.ReportProgress(
            (ulong)written,
            file.FileName,
            depot.DepotId,
            fileCompleted);

        if (fileCompleted)
        {
            var fileFinalPath = Path.Combine(depot.InstallDir, file.FileName);
            userInterface?.WriteLine("{0,6:#00.00}% {1}",
                sizeDownloaded / (float)depotDownloadCounter.CompleteDownloadSize * 100.0f, fileFinalPath);
        }
    }
}

/// <summary>
///     Provides file filtering functionality for download operations.
/// </summary>
internal static class FileFilter
{
    /// <summary>
    ///     Tests if a file should be included based on download configuration.
    /// </summary>
    internal static bool TestIsFileIncluded(string filename, DownloadConfig config)
    {
        if (!config.UsingFileList)
            return true;

        filename = filename.Replace('\\', '/');

        if (config.FilesToDownload.Contains(filename)) return true;

        foreach (var rgx in config.FilesToDownloadRegex)
        {
            var m = rgx.Match(filename);

            if (m.Success)
                return true;
        }

        return false;
    }
}