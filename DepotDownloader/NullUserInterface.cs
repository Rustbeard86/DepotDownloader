using System;

namespace DepotDownloader.Lib;

/// <summary>
///     No-op implementation of IUserInterface for library consumers who don't need output.
/// </summary>
public sealed class NullUserInterface : IUserInterface
{
    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;

    public void Write(string message)
    {
    }

    public void Write(string format, params object[] args)
    {
    }

    public void WriteLine()
    {
    }

    public void WriteLine(string message)
    {
    }

    public void WriteLine(string format, params object[] args)
    {
    }

    public void WriteError(string message)
    {
    }

    public void WriteError(string format, params object[] args)
    {
    }

    public void WriteDebug(string category, string message)
    {
    }

    public string ReadLine()
    {
        return string.Empty;
    }

    public string ReadPassword()
    {
        return string.Empty;
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return default;
    }

    public void UpdateProgress(ulong downloaded, ulong total)
    {
    }

    public void UpdateProgress(Ansi.ProgressState state, byte progress = 0)
    {
    }

    public void DisplayQrCode(string challengeUrl)
    {
    }
}