using DepotDownloader.Lib;

namespace DepotDownloader.Tests.Helpers;

/// <summary>
/// Mock implementation of IUserInterface for testing.
/// </summary>
public class TestUserInterface : IUserInterface
{
    private readonly List<string> _messages = [];
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Messages => _messages;
    public IReadOnlyList<string> Errors => _errors;

    public bool IsInputRedirected => false;
    public bool IsOutputRedirected => false;

    public void Write(string message) => _messages.Add(message);
    public void Write(string format, params object[] args) => _messages.Add(string.Format(format, args));
    public void WriteLine() => _messages.Add(string.Empty);
    public void WriteLine(string message) => _messages.Add(message);
    public void WriteLine(string format, params object[] args) => _messages.Add(string.Format(format, args));

    public void WriteError(string message) => _errors.Add(message);
    public void WriteError(string format, params object[] args) => _errors.Add(string.Format(format, args));

    public void WriteDebug(string category, string message) { }

    public string ReadLine() => string.Empty;
    public string ReadPassword() => string.Empty;
    public ConsoleKeyInfo ReadKey(bool intercept) => new();

    public void DisplayQrCode(string challengeUrl) { }
    public void UpdateProgress(ulong currentBytes, ulong totalBytes) { }

    public void Clear()
    {
        _messages.Clear();
        _errors.Clear();
    }

    public bool ContainsMessage(string text) => _messages.Any(m => m.Contains(text, StringComparison.OrdinalIgnoreCase));
    public bool ContainsError(string text) => _errors.Any(e => e.Contains(text, StringComparison.OrdinalIgnoreCase));
}
