using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using SteamKit2.Internal;

namespace DepotDownloader.Lib;

internal class Steam3Session
{
    public delegate bool WaitCondition();

    // Cancellation
    private readonly CancellationTokenSource _abortedToken = new();

    // Authentication
    private readonly bool _authenticatedUser;

    // SteamKit2 Components
    private readonly CallbackManager _callbacks;
    private readonly SteamUser.LogOnDetails _logonDetails;
    private readonly SteamApps _steamApps;
    private readonly SteamCloud _steamCloud;
    private readonly Lock _steamLock = new();
    private readonly PublishedFile _steamPublishedFile;
    private readonly IUserInterface _userInterface;
    private AuthSession _authSession;

    // Connection State
    private bool _bAborted;
    private bool _bConnecting;
    private bool _bDidDisconnect;
    private bool _bExpectingDisconnectRemote;
    private bool _bIsConnectionRecovery;
    private int _connectionBackoff;
    private int _seq;

    // Public SteamKit2 Handlers
    public SteamClient SteamClient;
    public SteamContent SteamContent;
    public SteamUser SteamUser;

    public Steam3Session(SteamUser.LogOnDetails details, IUserInterface userInterface)
    {
        _userInterface = userInterface ?? throw new ArgumentNullException(nameof(userInterface));
        _logonDetails = details;
        _authenticatedUser = details.Username != null || ContentDownloader.Config.UseQrCode;

        var clientConfiguration = SteamConfiguration.Create(config =>
            config
                .WithHttpClientFactory(static _ => HttpClientFactory.CreateHttpClient())
        );

        SteamClient = new SteamClient(clientConfiguration);

        SteamUser = SteamClient.GetHandler<SteamUser>();
        _steamApps = SteamClient.GetHandler<SteamApps>();
        _steamCloud = SteamClient.GetHandler<SteamCloud>();
        var steamUnifiedMessages = SteamClient.GetHandler<SteamUnifiedMessages>();

        if (steamUnifiedMessages != null)
            _steamPublishedFile = steamUnifiedMessages.CreateService<PublishedFile>();
        else
            throw new InvalidOperationException("Failed to get SteamUnifiedMessages handler from SteamClient");

        SteamContent = SteamClient.GetHandler<SteamContent>();

        _callbacks = new CallbackManager(SteamClient);

        _callbacks.Subscribe<SteamClient.ConnectedCallback>(ConnectedCallback);
        _callbacks.Subscribe<SteamClient.DisconnectedCallback>(DisconnectedCallback);
        _callbacks.Subscribe<SteamUser.LoggedOnCallback>(LogOnCallback);
        _callbacks.Subscribe<SteamApps.LicenseListCallback>(LicenseListCallback);

        _userInterface.Write("Connecting to Steam3...");
        Connect();
    }

    public bool IsLoggedOn { get; private set; }

    public ReadOnlyCollection<SteamApps.LicenseListCallback.License> Licenses { get; private set; }

    public Dictionary<uint, ulong> AppTokens { get; } = [];
    public Dictionary<uint, ulong> PackageTokens { get; } = [];
    public Dictionary<uint, byte[]> DepotKeys { get; } = [];

    public ConcurrentDictionary<(uint, string), TaskCompletionSource<SteamContent.CDNAuthToken>>
        CdnAuthTokens { get; } = [];

    public Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> AppInfo { get; } = [];
    public Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo> PackageInfo { get; } = [];
    public Dictionary<string, byte[]> AppBetaPasswords { get; } = [];

    public bool WaitUntilCallback(Action submitter, WaitCondition waiter)
    {
        while (!_bAborted && !waiter())
        {
            lock (_steamLock)
            {
                submitter();
            }

            var seq = _seq;
            do
            {
                lock (_steamLock)
                {
                    _callbacks.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }
            } while (!_bAborted && _seq == seq && !waiter());
        }

        return _bAborted;
    }

    public bool WaitForCredentials()
    {
        if (IsLoggedOn || _bAborted)
            return IsLoggedOn;

        WaitUntilCallback(() => { }, () => IsLoggedOn);

        return IsLoggedOn;
    }

    public async Task TickCallbacks()
    {
        var token = _abortedToken.Token;

        try
        {
            while (!token.IsCancellationRequested) await _callbacks.RunWaitCallbackAsync(token);
        }
        catch (OperationCanceledException)
        {
            //
        }
    }

    public async Task RequestAppInfo(uint appId, bool bForce = false)
    {
        if ((AppInfo.ContainsKey(appId) && !bForce) || _bAborted)
            return;

        var appTokens = await _steamApps.PICSGetAccessTokens([appId], []);

        if (appTokens.AppTokensDenied.Contains(appId))
            _userInterface.WriteLine("Insufficient privileges to get access token for app {0}", appId);

        foreach (var tokenDict in appTokens.AppTokens) AppTokens[tokenDict.Key] = tokenDict.Value;

        var request = new SteamApps.PICSRequest(appId);

        if (AppTokens.TryGetValue(appId, out var token)) request.AccessToken = token;

        var appInfoMultiple = await _steamApps.PICSGetProductInfo([request], []);

        if (appInfoMultiple.Results != null)
            foreach (var appInfo in appInfoMultiple.Results)
            {
                foreach (var appValue in appInfo.Apps)
                {
                    var app = appValue.Value;

                    _userInterface.WriteLine("Got AppInfo for {0}", app.ID);
                    AppInfo[app.ID] = app;
                }

                foreach (var app in appInfo.UnknownApps) AppInfo[app] = null;
            }
    }

    public async Task RequestPackageInfo(IEnumerable<uint> packageIds)
    {
        var packages = packageIds.ToList();
        packages.RemoveAll(PackageInfo.ContainsKey);

        if (packages.Count == 0 || _bAborted)
            return;

        var packageRequests = new List<SteamApps.PICSRequest>();

        foreach (var package in packages)
        {
            var request = new SteamApps.PICSRequest(package);

            if (PackageTokens.TryGetValue(package, out var token)) request.AccessToken = token;

            packageRequests.Add(request);
        }

        var packageInfoMultiple = await _steamApps.PICSGetProductInfo([], packageRequests);

        if (packageInfoMultiple.Results != null)
            foreach (var packageInfo in packageInfoMultiple.Results)
            {
                foreach (var packageValue in packageInfo.Packages)
                {
                    var package = packageValue.Value;
                    PackageInfo[package.ID] = package;
                }

                foreach (var package in packageInfo.UnknownPackages) PackageInfo[package] = null;
            }
    }

    public async Task<bool> RequestFreeAppLicense(uint appId)
    {
        try
        {
            var resultInfo = await _steamApps.RequestFreeLicense(appId);

            return resultInfo.GrantedApps.Contains(appId);
        }
        catch (Exception ex)
        {
            _userInterface.WriteLine($"Failed to request FreeOnDemand license for app {appId}: {ex.Message}");
            return false;
        }
    }

    public async Task RequestDepotKey(uint depotId, uint appid = 0)
    {
        if (DepotKeys.ContainsKey(depotId) || _bAborted)
            return;

        var depotKey = await _steamApps.GetDepotDecryptionKey(depotId, appid);

        _userInterface.WriteLine("Got depot key for {0} result: {1}", depotKey.DepotID, depotKey.Result);

        if (depotKey.Result != EResult.OK) return;

        DepotKeys[depotKey.DepotID] = depotKey.DepotKey;
    }


    public async Task<ulong> GetDepotManifestRequestCodeAsync(uint depotId, uint appId, ulong manifestId, string branch)
    {
        if (_bAborted)
            return 0;

        var requestCode = await SteamContent.GetManifestRequestCode(depotId, appId, manifestId, branch);

        if (requestCode == 0)
        {
            _userInterface.WriteLine(
                $"No manifest request code was returned for depot {depotId} from app {appId}, manifest {manifestId}");

            if (!_authenticatedUser)
                _userInterface.WriteLine(
                    "Suggestion: Try logging in with -username as old manifests may not be available for anonymous accounts.");
        }
        else
        {
            _userInterface.WriteLine(
                $"Got manifest request code for depot {depotId} from app {appId}, manifest {manifestId}, result: {requestCode}");
        }

        return requestCode;
    }

    public async Task RequestCdnAuthToken(uint appid, uint depotid, Server server)
    {
        var cdnKey = (depotid, server.Host);
        var completion = new TaskCompletionSource<SteamContent.CDNAuthToken>();

        if (_bAborted || !CdnAuthTokens.TryAdd(cdnKey, completion)) return;

        DebugLog.WriteLine(nameof(Steam3Session), $"Requesting CDN auth token for {server.Host}");

        if (server.Host != null)
        {
            var cdnAuth = await SteamContent.GetCDNAuthToken(appid, depotid, server.Host);

            _userInterface.WriteLine(
                $"Got CDN auth token for {server.Host} result: {cdnAuth.Result} (expires {cdnAuth.Expiration})");

            if (cdnAuth.Result != EResult.OK) return;

            completion.TrySetResult(cdnAuth);
        }
    }

    public async Task CheckAppBetaPassword(uint appid, string password)
    {
        var appPassword = await _steamApps.CheckAppBetaPassword(appid, password);

        _userInterface.WriteLine("Retrieved {0} beta keys with result: {1}", appPassword.BetaPasswords.Count,
            appPassword.Result);

        foreach (var entry in appPassword.BetaPasswords) AppBetaPasswords[entry.Key] = entry.Value;
    }

    public async Task<KeyValue> GetPrivateBetaDepotSection(uint appid, string branch)
    {
        if (!AppBetaPasswords.TryGetValue(branch, out var branchPassword)) // Should be filled by CheckAppBetaPassword
            return new KeyValue();

        AppTokens.TryGetValue(appid, out var accessToken); // Should be filled by RequestAppInfo

        var privateBeta = await _steamApps.PICSGetPrivateBeta(appid, accessToken, branch, branchPassword);

        _userInterface.WriteLine($"Retrieved private beta depot section for {appid} with result: {privateBeta.Result}");

        return privateBeta.DepotSection;
    }

    public async Task<PublishedFileDetails> GetPublishedFileDetails(uint appId, PublishedFileID pubFile)
    {
        var pubFileRequest = new CPublishedFile_GetDetails_Request
        {
            appid = appId,
            includechildren = true
        };
        pubFileRequest.publishedfileids.Add(pubFile);

        var details = await _steamPublishedFile.GetDetails(pubFileRequest);

        if (details.Result == EResult.OK) return details.Body.publishedfiledetails.FirstOrDefault();

        throw new Exception(
            $"EResult {(int)details.Result} ({details.Result}) while retrieving file details for pubfile {pubFile}.");
    }


    public async Task<SteamCloud.UGCDetailsCallback> GetUgcDetails(UGCHandle ugcHandle)
    {
        var callback = await _steamCloud.RequestUGCDetails(ugcHandle);

        if (callback.Result == EResult.OK) return callback;

        if (callback.Result == EResult.FileNotFound) return null;

        throw new Exception(
            $"EResult {(int)callback.Result} ({callback.Result}) while retrieving UGC details for {ugcHandle}.");
    }

    private void ResetConnectionFlags()
    {
        _bExpectingDisconnectRemote = false;
        _bDidDisconnect = false;
        _bIsConnectionRecovery = false;
    }

    private void Connect()
    {
        _bAborted = false;
        _bConnecting = true;
        _connectionBackoff = 0;
        _authSession = null;

        ResetConnectionFlags();
        SteamClient.Connect();
    }

    private void Abort(bool sendLogOff = true)
    {
        Disconnect(sendLogOff);
    }

    public void Disconnect(bool sendLogOff = true)
    {
        if (sendLogOff) SteamUser.LogOff();

        _bAborted = true;
        _bConnecting = false;
        _bIsConnectionRecovery = false;
        _abortedToken.Cancel();
        SteamClient.Disconnect();

        _userInterface.UpdateProgress(Ansi.ProgressState.Hidden);

        // flush callbacks until our disconnected event
        while (!_bDidDisconnect) _callbacks.RunWaitAllCallbacks(TimeSpan.FromMilliseconds(100));
    }

    private void Reconnect()
    {
        _bIsConnectionRecovery = true;
        SteamClient.Disconnect();
    }

    private void ConnectedCallback(SteamClient.ConnectedCallback connected)
    {
        // Fire and forget - exceptions are handled within HandleConnectedAsync
        _ = HandleConnectedAsync();
    }

    private async Task HandleConnectedAsync()
    {
        try
        {
            _userInterface.WriteLine(" Done!");
            _bConnecting = false;

            // Update our tracking so that we don't time out, even if we need to reconnect multiple times,
            // e.g. if the authentication phase takes a while and therefore multiple connections.
            _connectionBackoff = 0;

            if (!_authenticatedUser)
            {
                _userInterface.Write("Logging anonymously into Steam3...");
                SteamUser.LogOnAnonymous();
            }
            else
            {
                if (_logonDetails.Username != null)
                    _userInterface.WriteLine("Logging '{0}' into Steam3...", _logonDetails.Username);

                if (_authSession is null)
                {
                    if (_logonDetails.Username != null && _logonDetails.Password != null &&
                        _logonDetails.AccessToken is null)
                    {
                        try
                        {
                            _ = AccountSettingsStore.Instance.GuardData.TryGetValue(_logonDetails.Username,
                                out var guarddata);
                            _authSession = await SteamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
                                new AuthSessionDetails
                                {
                                    DeviceFriendlyName = nameof(DepotDownloader),
                                    Username = _logonDetails.Username,
                                    Password = _logonDetails.Password,
                                    IsPersistentSession = ContentDownloader.Config.RememberPassword,
                                    GuardData = guarddata,
                                    Authenticator = new ConsoleAuthenticator(_userInterface)
                                });
                        }
                        catch (TaskCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _userInterface.WriteError("Failed to authenticate with Steam: " + ex.Message);
                            Abort(false);
                            return;
                        }
                    }
                    else if (_logonDetails.AccessToken is null && ContentDownloader.Config.UseQrCode)
                    {
                        _userInterface.WriteLine("Logging in with QR code...");

                        try
                        {
                            var session = await SteamClient.Authentication.BeginAuthSessionViaQRAsync(
                                new AuthSessionDetails
                                {
                                    DeviceFriendlyName = nameof(DepotDownloader),
                                    IsPersistentSession = ContentDownloader.Config.RememberPassword
                                });

                            _authSession = session;

                            // Steam will periodically refresh the challenge url, so we need a new QR code.
                            session.ChallengeURLChanged = () =>
                            {
                                _userInterface.WriteLine();
                                _userInterface.WriteLine("The QR code has changed:");

                                _userInterface.DisplayQrCode(session.ChallengeURL);
                            };

                            // Draw initial QR code immediately
                            _userInterface.DisplayQrCode(session.ChallengeURL);
                        }
                        catch (TaskCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _userInterface.WriteError("Failed to authenticate with Steam: " + ex.Message);
                            Abort(false);
                            return;
                        }
                    }
                }

                if (_authSession != null)
                {
                    try
                    {
                        var result = await _authSession.PollingWaitForResultAsync();
                        _logonDetails.Username = result.AccountName;
                        _logonDetails.Password = null;
                        _logonDetails.AccessToken = result.RefreshToken;

                        if (result.NewGuardData != null)
                        {
                            AccountSettingsStore.Instance.GuardData[result.AccountName] = result.NewGuardData;

                            if (ContentDownloader.Config.UseQrCode)
                                _userInterface.WriteLine(
                                    $"Success! Next time you can login with -username {result.AccountName} -remember-password instead of -qr.");
                        }
                        else
                        {
                            AccountSettingsStore.Instance.GuardData.Remove(result.AccountName);
                        }

                        AccountSettingsStore.Instance.LoginTokens[result.AccountName] = result.RefreshToken;
                        AccountSettingsStore.Save();
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _userInterface.WriteError("Failed to authenticate with Steam: " + ex.Message);
                        Abort(false);
                        return;
                    }

                    _authSession = null;
                }

                SteamUser.LogOn(_logonDetails);
            }
        }
        catch (Exception ex)
        {
            // Catch any unhandled exceptions to prevent application crash
            _userInterface.WriteError($"Unhandled exception in connection callback: {ex.Message}");
            Abort(false);
        }
    }

    private void DisconnectedCallback(SteamClient.DisconnectedCallback disconnected)
    {
        _bDidDisconnect = true;

        DebugLog.WriteLine(nameof(Steam3Session),
            $"Disconnected: bIsConnectionRecovery = {_bIsConnectionRecovery}, UserInitiated = {disconnected.UserInitiated}, bExpectingDisconnectRemote = {_bExpectingDisconnectRemote}");

        // When recovering the connection, we want to reconnect even if the remote disconnects us
        if (!_bIsConnectionRecovery && (disconnected.UserInitiated || _bExpectingDisconnectRemote))
        {
            _userInterface.WriteLine("Disconnected from Steam");

            // Any operations outstanding need to be aborted
            _bAborted = true;
        }
        else if (_connectionBackoff >= 10)
        {
            _userInterface.WriteLine("Could not connect to Steam after 10 tries");
            Abort(false);
        }
        else if (!_bAborted)
        {
            _connectionBackoff += 1;

            _userInterface.WriteLine(_bConnecting
                ? $"Connection to Steam failed. Trying again (#{_connectionBackoff})..."
                : "Lost connection to Steam. Reconnecting");

            Thread.Sleep(1000 * _connectionBackoff);

            // Any connection related flags need to be reset here to match the state after Connect
            ResetConnectionFlags();
            SteamClient.Connect();
        }
    }

    private void LogOnCallback(SteamUser.LoggedOnCallback loggedOn)
    {
        var isSteamGuard = loggedOn.Result == EResult.AccountLogonDenied;
        var is2Fa = loggedOn.Result == EResult.AccountLoginDeniedNeedTwoFactor;
        var isAccessToken = ContentDownloader.Config.RememberPassword && _logonDetails.AccessToken != null &&
                            loggedOn.Result is EResult.InvalidPassword
                                or EResult.InvalidSignature
                                or EResult.AccessDenied
                                or EResult.Expired
                                or EResult.Revoked;

        if (isSteamGuard || is2Fa || isAccessToken)
        {
            _bExpectingDisconnectRemote = true;
            Abort(false);

            if (!isAccessToken) _userInterface.WriteLine("This account is protected by Steam Guard.");

            if (is2Fa)
            {
                do
                {
                    _userInterface.Write("Please enter your 2 factor auth code from your authenticator app: ");
                    _logonDetails.TwoFactorCode = _userInterface.ReadLine();
                } while (string.Empty == _logonDetails.TwoFactorCode);
            }
            else if (isAccessToken)
            {
                if (_logonDetails.Username != null)
                {
                    // Clear the invalid token from stored settings
                    AccountSettingsStore.Instance.LoginTokens.Remove(_logonDetails.Username);
                    AccountSettingsStore.Save();

                    _userInterface.WriteLine($"Access token was rejected ({loggedOn.Result}).");
                    _userInterface.WriteLine("Your saved credentials have expired. Please re-enter your password.");
                    _userInterface.WriteLine();

                    // Clear the access token to force password authentication
                    _logonDetails.AccessToken = null;

                    // Prompt for password
                    string password;
                    do
                    {
                        _userInterface.Write($"Enter account password for \"{_logonDetails.Username}\": ");
                        if (_userInterface.IsInputRedirected)
                        {
                            password = _userInterface.ReadLine();
                        }
                        else
                        {
                            // Avoid console echoing of password
                            password = _userInterface.ReadPassword();
                            _userInterface.WriteLine();
                        }
                    } while (string.IsNullOrEmpty(password));

                    _logonDetails.Password = password;

                    _userInterface.Write("Retrying Steam3 connection...");
                    Connect();
                    return;
                }

                // No username available, can't prompt for password
                _userInterface.WriteLine($"Access token was rejected ({loggedOn.Result}).");
                Abort(false);
                return;
            }
            else
            {
                do
                {
                    _userInterface.Write("Please enter the authentication code sent to your email address: ");
                    _logonDetails.AuthCode = _userInterface.ReadLine();
                } while (string.Empty == _logonDetails.AuthCode);
            }

            _userInterface.Write("Retrying Steam3 connection...");
            Connect();

            return;
        }

        if (loggedOn.Result == EResult.TryAnotherCM)
        {
            _userInterface.Write("Retrying Steam3 connection (TryAnotherCM)...");

            Reconnect();

            return;
        }

        if (loggedOn.Result == EResult.ServiceUnavailable)
        {
            _userInterface.WriteLine("Unable to login to Steam3: {0}", loggedOn.Result);
            Abort(false);

            return;
        }

        if (loggedOn.Result != EResult.OK)
        {
            _userInterface.WriteLine("Unable to login to Steam3: {0}", loggedOn.Result);
            Abort();

            return;
        }

        _userInterface.WriteLine(" Done!");

        _seq++;
        IsLoggedOn = true;

        if (ContentDownloader.Config.CellId == 0)
        {
            _userInterface.WriteLine("Using Steam3 suggested CellID: " + loggedOn.CellID);
            ContentDownloader.Config.CellId = (int)loggedOn.CellID;
        }
    }

    private void LicenseListCallback(SteamApps.LicenseListCallback licenseList)
    {
        if (licenseList.Result != EResult.OK)
        {
            _userInterface.WriteLine("Unable to get license list: {0} ", licenseList.Result);
            Abort();

            return;
        }

        _userInterface.WriteLine("Got {0} licenses for account!", licenseList.LicenseList.Count);
        Licenses = licenseList.LicenseList;

        foreach (var license in licenseList.LicenseList)
            if (license.AccessToken > 0)
                PackageTokens.TryAdd(license.PackageID, license.AccessToken);
    }
}