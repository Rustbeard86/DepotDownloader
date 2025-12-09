using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace DepotDownloader.Lib;

/// <summary>
///     Manages persistence of download state for resume functionality.
/// </summary>
internal sealed class DownloadStateStore
{
    private const string StateFileName = "download_state.json";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);
    private readonly Lock _lock = new();
    private readonly string _stateFilePath;
    private DateTime _lastSave = DateTime.MinValue;
    private DownloadState _state;

    /// <summary>
    ///     Creates a new download state store.
    /// </summary>
    /// <param name="installDirectory">The directory where state file will be stored.</param>
    public DownloadStateStore(string installDirectory)
    {
        var configDir = Path.Combine(installDirectory, ".DepotDownloader");
        Directory.CreateDirectory(configDir);
        _stateFilePath = Path.Combine(configDir, StateFileName);
    }

    /// <summary>
    ///     Loads existing state from disk, or creates new state.
    /// </summary>
    /// <param name="appId">The app ID being downloaded.</param>
    /// <param name="branch">The branch being downloaded.</param>
    /// <param name="resume">Whether to attempt resuming from existing state.</param>
    /// <returns>True if resuming from existing state, false if starting fresh.</returns>
    public bool LoadOrCreate(uint appId, string branch, bool resume)
    {
        lock (_lock)
        {
            if (resume && File.Exists(_stateFilePath))
                try
                {
                    var json = File.ReadAllText(_stateFilePath);
                    _state = JsonSerializer.Deserialize(json, DownloadStateContext.Default.DownloadState);

                    // Validate the state matches what we're downloading
                    if (_state is not null && _state.AppId == appId && _state.Branch == branch)
                    {
                        _state.LastUpdatedAt = DateTime.UtcNow;
                        return true;
                    }
                }
                catch
                {
                    // Corrupted state file, start fresh
                }

            // Create new state
            _state = new DownloadState
            {
                AppId = appId,
                Branch = branch,
                StartedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            };

            return false;
        }
    }

    /// <summary>
    ///     Initializes state for a depot.
    /// </summary>
    public void InitializeDepot(uint depotId, ulong manifestId, ulong totalBytes)
    {
        lock (_lock)
        {
            if (!_state.Depots.TryGetValue(depotId, out var depotState))
            {
                depotState = new DepotDownloadState
                {
                    DepotId = depotId,
                    ManifestId = manifestId,
                    TotalBytes = totalBytes
                };
                _state.Depots[depotId] = depotState;
            }
            else if (depotState.ManifestId != manifestId)
            {
                // Manifest changed, reset depot state
                depotState.ManifestId = manifestId;
                depotState.CompletedChunks.Clear();
                depotState.CompletedFiles.Clear();
                depotState.BytesDownloaded = 0;
                depotState.TotalBytes = totalBytes;
                depotState.IsComplete = false;
            }
        }
    }

    /// <summary>
    ///     Checks if a chunk has already been downloaded.
    /// </summary>
    public bool IsChunkComplete(uint depotId, string chunkId)
    {
        lock (_lock)
        {
            return _state.Depots.TryGetValue(depotId, out var depotState) &&
                   depotState.CompletedChunks.Contains(chunkId);
        }
    }

    /// <summary>
    ///     Marks a chunk as completed.
    /// </summary>
    public void MarkChunkComplete(uint depotId, string chunkId, ulong chunkSize)
    {
        lock (_lock)
        {
            if (_state.Depots.TryGetValue(depotId, out var depotState))
                if (depotState.CompletedChunks.Add(chunkId))
                {
                    depotState.BytesDownloaded += chunkSize;
                    _state.TotalBytesDownloaded += chunkSize;
                    _state.LastUpdatedAt = DateTime.UtcNow;
                }

            // Throttle saves
            if (DateTime.UtcNow - _lastSave > SaveInterval) SaveInternal();
        }
    }

    /// <summary>
    ///     Marks a file as completely downloaded.
    /// </summary>
    public void MarkFileComplete(uint depotId, string fileName)
    {
        lock (_lock)
        {
            if (_state.Depots.TryGetValue(depotId, out var depotState))
            {
                depotState.CompletedFiles.Add(fileName);
                _state.LastUpdatedAt = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    ///     Marks a depot as completely downloaded.
    /// </summary>
    public void MarkDepotComplete(uint depotId)
    {
        lock (_lock)
        {
            if (_state.Depots.TryGetValue(depotId, out var depotState))
            {
                depotState.IsComplete = true;
                _state.LastUpdatedAt = DateTime.UtcNow;
                SaveInternal();
            }
        }
    }

    /// <summary>
    ///     Sets the total bytes to download.
    /// </summary>
    public void SetTotalBytes(ulong totalBytes)
    {
        lock (_lock)
        {
            _state.TotalBytes = totalBytes;
        }
    }

    /// <summary>
    ///     Saves the current state to disk.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, DownloadStateContext.Default.DownloadState);
            File.WriteAllText(_stateFilePath, json);
            _lastSave = DateTime.UtcNow;
        }
        catch
        {
            // Ignore save errors
        }
    }

    /// <summary>
    ///     Deletes the state file after successful completion.
    /// </summary>
    public void Delete()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_stateFilePath))
                    File.Delete(_stateFilePath);
            }
            catch
            {
                // Ignore delete errors
            }
        }
    }
}