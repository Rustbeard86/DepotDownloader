using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ProtoBuf;

namespace DepotDownloader;

[ProtoContract]
internal class DepotConfigStore
{
    public static DepotConfigStore Instance;
    private static IUserInterface _userInterface;

    private string _fileName;

    private DepotConfigStore()
    {
        InstalledManifestIDs = [];
    }

    [ProtoMember(1)] public Dictionary<uint, ulong> InstalledManifestIDs { get; private set; }

    private static bool Loaded => Instance != null;

    public static void Initialize(IUserInterface userInterface)
    {
        _userInterface = userInterface ?? throw new ArgumentNullException(nameof(userInterface));
    }

    public static void LoadFromFile(string filename)
    {
        if (Loaded)
            throw new Exception("Config already loaded");

        if (File.Exists(filename))
            try
            {
                using var fs = File.Open(filename, FileMode.Open, FileAccess.Read);
                using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                Instance = Serializer.Deserialize<DepotConfigStore>(ds);

                if (Instance == null)
                {
                    _userInterface?.WriteLine("Failed to load depot config: deserialization returned null");
                    Instance = new DepotConfigStore();
                }
            }
            catch (Exception ex)
            {
                _userInterface?.WriteLine("Failed to load depot config: {0}", ex.Message);
                Instance = new DepotConfigStore();
            }
        else
            Instance = new DepotConfigStore();

        Instance._fileName = filename;
    }

    public static void Save()
    {
        if (!Loaded)
            throw new Exception("Saved config before loading");

        try
        {
            using var fs = File.Open(Instance._fileName, FileMode.Create, FileAccess.Write);
            using var ds = new DeflateStream(fs, CompressionMode.Compress);
            Serializer.Serialize(ds, Instance);
        }
        catch (Exception ex)
        {
            _userInterface?.WriteLine("Failed to save depot config: {0}", ex.Message);
        }
    }
}