using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2.CDN;

namespace DepotDownloader;

/// <summary>
///     CDNClientPool provides a pool of connections to CDN endpoints, requesting CDN tokens as needed
/// </summary>
internal class CdnClientPool(Steam3Session steamSession, uint appId)
{
    private const int PenaltyIncrement = 100;
    private const int PenaltyDecrement = 10;
    private const int PenaltyDecayAmount = 5;
    private const int MaxPenalty = 1000;
    private readonly Lock _serverLock = new();

    private readonly List<Server> _servers = [];
    private int _nextServer;

    public Client CdnClient { get; } = new(steamSession.SteamClient);
    public Server ProxyServer { get; private set; }

    public async Task UpdateServerList()
    {
        var servers = await steamSession.SteamContent.GetServersForSteamPipe();

        ProxyServer = servers.FirstOrDefault(x => x.UseAsProxy);

        // Apply penalty decay to all servers before sorting
        ApplyPenaltyDecay(servers);

        var weightedCdnServers = servers
            .Where(server =>
            {
                var isEligibleForApp = server.AllowedAppIds.Length == 0 || server.AllowedAppIds.Contains(appId);
                return isEligibleForApp && server.Type is "SteamCache" or "CDN";
            })
            .Select(server =>
            {
                var penalty = 0;
                if (!string.IsNullOrEmpty(server.Host))
                    AccountSettingsStore.Instance.ContentServerPenalty.TryGetValue(server.Host, out penalty);

                return (server, penalty);
            })
            .OrderBy(pair => pair.penalty).ThenBy(pair => pair.server.WeightedLoad);

        lock (_serverLock)
        {
            _servers.Clear();
            foreach (var (server, _) in weightedCdnServers)
                for (var i = 0; i < server.NumEntries; i++)
                    _servers.Add(server);

            if (_servers.Count == 0) throw new Exception("Failed to retrieve any download servers.");
        }
    }

    private static void ApplyPenaltyDecay(IEnumerable<Server> servers)
    {
        // Decay penalties for all known servers to allow recovery from transient issues
        var serverHosts = servers
            .Where(s => !string.IsNullOrEmpty(s.Host))
            .Select(s => s.Host)
            .Distinct()
            .ToList();

        foreach (var host in serverHosts)
            AccountSettingsStore.Instance.ContentServerPenalty.AddOrUpdate(
                host,
                0, // If not in dictionary, no penalty
                (_, oldValue) => Math.Max(0, oldValue - PenaltyDecayAmount)
            );

        // Persist the decayed penalties
        AccountSettingsStore.Save();
    }

    public Server GetConnection()
    {
        lock (_serverLock)
        {
            return _servers[_nextServer % _servers.Count];
        }
    }

    public void ReturnConnection(Server server)
    {
        if (server == null || string.IsNullOrEmpty(server.Host)) return;

        // Successful connection - reduce penalty to reward reliable servers
        AccountSettingsStore.Instance.ContentServerPenalty.AddOrUpdate(
            server.Host,
            0, // If not in dictionary, don't add a penalty
            (_, oldValue) => Math.Max(0, oldValue - PenaltyDecrement)
        );
    }

    public void ReturnBrokenConnection(Server server)
    {
        if (server == null || string.IsNullOrEmpty(server.Host)) return;

        lock (_serverLock)
        {
            // Skip this server in the round-robin if it's the current one
            if (_servers[_nextServer % _servers.Count] == server)
                Interlocked.Increment(ref _nextServer);
        }

        // Add penalty to deprioritize this server in future connections
        AccountSettingsStore.Instance.ContentServerPenalty.AddOrUpdate(
            server.Host,
            PenaltyIncrement, // First failure
            (_, oldValue) => Math.Min(MaxPenalty, oldValue + PenaltyIncrement)
        );

        // Persist penalties to disk so they survive restarts
        AccountSettingsStore.Save();
    }
}