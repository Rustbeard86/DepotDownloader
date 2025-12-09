using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using ProtoBuf;

namespace DepotDownloader.Lib;

[ProtoContract]
public class DepotConfigStore
{
    private static readonly Lock LockObject = new();
    private static DepotConfigStore _instance;
    private static IUserInterface _userInterface;

    private string _fileName;

    private DepotConfigStore()
    {
        InstalledManifestIDs = [];
    }

    [ProtoMember(1)] public Dictionary<uint, ulong> InstalledManifestIDs { get; private set; }

    /// <summary>
    ///     Gets the singleton instance of DepotConfigStore.
    ///     Thread-safe access to the loaded configuration.
    /// </summary>
    public static DepotConfigStore Instance
    {
        get
        {
            lock (LockObject)
            {
                if (_instance is null)
                    throw new InvalidOperationException(
                        "DepotConfigStore has not been loaded. Call LoadFromFile first.");
                return _instance;
            }
        }
    }

    private static bool Loaded
    {
        get
        {
            lock (LockObject)
            {
                return _instance is not null;
            }
        }
    }

    public static void Initialize(IUserInterface userInterface)
    {
        _userInterface = userInterface ?? throw new ArgumentNullException(nameof(userInterface));
    }

    public static void LoadFromFile(string filename)
    {
        lock (LockObject)
        {
            if (_instance is not null)
                throw new InvalidOperationException("Config already loaded");

            if (File.Exists(filename))
                try
                {
                    using var fs = File.Open(filename, FileMode.Open, FileAccess.Read);
                    using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                    _instance = Serializer.Deserialize<DepotConfigStore>(ds);

                    if (_instance is null)
                    {
                        _userInterface?.WriteLine("Failed to load depot config: deserialization returned null");
                        _instance = new DepotConfigStore();
                    }
                }
                catch (Exception ex)
                {
                    _userInterface?.WriteLine("Failed to load depot config: {0}", ex.Message);
                    _instance = new DepotConfigStore();
                }
            else
                _instance = new DepotConfigStore();

            _instance._fileName = filename;
        }
    }

    public static void Save()
    {
        DepotConfigStore instance;

        lock (LockObject)
        {
            instance = _instance ?? throw new InvalidOperationException("Cannot save config before loading");
        }

        try
        {
            using var fs = File.Open(instance._fileName, FileMode.Create, FileAccess.Write);
            using var ds = new DeflateStream(fs, CompressionMode.Compress);
            Serializer.Serialize(ds, instance);
        }
        catch (Exception ex)
        {
            _userInterface?.WriteLine("Failed to save depot config: {0}", ex.Message);
        }
    }
}