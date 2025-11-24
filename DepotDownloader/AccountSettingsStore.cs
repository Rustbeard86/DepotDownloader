using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.IsolatedStorage;
using System.Threading;
using ProtoBuf;

namespace DepotDownloader.Lib;

[ProtoContract]
public class AccountSettingsStore
{
    private static readonly Lock LockObject = new();
    private static AccountSettingsStore _instance;
    private static readonly IsolatedStorageFile IsolatedStorage = IsolatedStorageFile.GetUserStoreForAssembly();
    private static IUserInterface _userInterface;

    private string _fileName;

    private AccountSettingsStore()
    {
        ContentServerPenalty = new ConcurrentDictionary<string, int>();
        LoginTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        GuardData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
    // Member 1 was a Dictionary<string, byte[]> for SentryData.

    [ProtoMember(2, IsRequired = false)]
    public ConcurrentDictionary<string, int> ContentServerPenalty { get; private set; }

    // Member 3 was a Dictionary<string, string> for LoginKeys.

    [ProtoMember(4, IsRequired = false)] public Dictionary<string, string> LoginTokens { get; private set; }

    [ProtoMember(5, IsRequired = false)] public Dictionary<string, string> GuardData { get; private set; }

    /// <summary>
    ///     Gets the singleton instance of AccountSettingsStore.
    ///     Thread-safe access to the loaded configuration.
    /// </summary>
    public static AccountSettingsStore Instance
    {
        get
        {
            lock (LockObject)
            {
                if (_instance == null)
                    throw new InvalidOperationException(
                        "AccountSettingsStore has not been loaded. Call LoadFromFile first.");
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
                return _instance != null;
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
            if (_instance != null)
                throw new InvalidOperationException("Config already loaded");

            if (IsolatedStorage.FileExists(filename))
                try
                {
                    using var fs = IsolatedStorage.OpenFile(filename, FileMode.Open, FileAccess.Read);
                    using var ds = new DeflateStream(fs, CompressionMode.Decompress);
                    _instance = Serializer.Deserialize<AccountSettingsStore>(ds);
                }
                catch (IOException ex)
                {
                    _userInterface?.WriteLine("Failed to load account settings: {0}", ex.Message);
                    _instance = new AccountSettingsStore();
                }
            else
                _instance = new AccountSettingsStore();

            _instance._fileName = filename;
        }
    }

    public static void Save()
    {
        AccountSettingsStore instance;

        lock (LockObject)
        {
            if (_instance == null)
                throw new InvalidOperationException("Cannot save config before loading");

            instance = _instance;
        }

        try
        {
            using var fs = IsolatedStorage.OpenFile(instance._fileName, FileMode.Create, FileAccess.Write);
            using var ds = new DeflateStream(fs, CompressionMode.Compress);
            Serializer.Serialize(ds, instance);
        }
        catch (IOException ex)
        {
            _userInterface?.WriteLine("Failed to save account settings: {0}", ex.Message);
        }
    }
}