using System;
using System.Threading.Tasks;
using SteamKit2.Authentication;

namespace DepotDownloader;

// This is practically copied from https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Authentication/UserConsoleAuthenticator.cs
internal class ConsoleAuthenticator(IUserInterface userInterface) : IAuthenticator
{
    private readonly IUserInterface _userInterface =
        userInterface ?? throw new ArgumentNullException(nameof(userInterface));

    /// <inheritdoc />
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        if (previousCodeWasIncorrect)
            _userInterface.WriteError("The previous 2-factor auth code you have provided is incorrect.");

        string code;

        do
        {
            _userInterface.WriteError(
                "STEAM GUARD! Please enter your 2-factor auth code from your authenticator app: ");
            code = _userInterface.ReadLine()?.Trim();

            if (code == null) break;
        } while (string.IsNullOrEmpty(code));

        return Task.FromResult(code!);
    }

    /// <inheritdoc />
    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        if (previousCodeWasIncorrect)
            _userInterface.WriteError("The previous 2-factor auth code you have provided is incorrect.");

        string code;

        do
        {
            _userInterface.WriteError($"STEAM GUARD! Please enter the auth code sent to the email at {email}: ");
            code = _userInterface.ReadLine()?.Trim();

            if (code == null) break;
        } while (string.IsNullOrEmpty(code));

        return Task.FromResult(code!);
    }

    /// <inheritdoc />
    public Task<bool> AcceptDeviceConfirmationAsync()
    {
        if (ContentDownloader.Config.SkipAppConfirmation) return Task.FromResult(false);

        _userInterface.WriteError("STEAM GUARD! Use the Steam Mobile App to confirm your sign in...");

        return Task.FromResult(true);
    }
}