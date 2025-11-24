using System;

namespace DepotDownloader.Lib;

/// <summary>
///     Abstraction for user interaction, allowing the library to be decoupled from Console.
/// </summary>
public interface IUserInterface
{
    /// <summary>
    ///     Gets whether input is redirected.
    /// </summary>
    bool IsInputRedirected { get; }

    /// <summary>
    ///     Gets whether output is redirected.
    /// </summary>
    bool IsOutputRedirected { get; }

    /// <summary>
    ///     Writes a message to the output.
    /// </summary>
    void Write(string message);

    /// <summary>
    ///     Writes a debug/diagnostic message (goes to stderr in console, can be filtered).
    /// </summary>
    void WriteDebug(string category, string message);

    /// <summary>
    ///     Writes a formatted message to the output.
    /// </summary>
    void Write(string format, params object[] args);

    /// <summary>
    ///     Writes a newline to the output.
    /// </summary>
    void WriteLine();

    /// <summary>
    ///     Writes a message to the output with a newline.
    /// </summary>
    void WriteLine(string message);

    /// <summary>
    ///     Writes a formatted message to the output with a newline.
    /// </summary>
    void WriteLine(string format, params object[] args);

    /// <summary>
    ///     Writes an error message to the error output.
    /// </summary>
    void WriteError(string message);

    /// <summary>
    ///     Writes a formatted error message to the error output.
    /// </summary>
    void WriteError(string format, params object[] args);

    /// <summary>
    ///     Reads a line of text from the input.
    /// </summary>
    string ReadLine();

    /// <summary>
    ///     Reads a password from the input without echoing characters.
    /// </summary>
    string ReadPassword();

    /// <summary>
    ///     Reads a single key press from the input.
    /// </summary>
    ConsoleKeyInfo ReadKey(bool intercept);

    /// <summary>
    ///     Updates a progress indicator.
    /// </summary>
    void UpdateProgress(ulong downloaded, ulong total);

    /// <summary>
    ///     Updates a progress indicator with a specific state.
    /// </summary>
    void UpdateProgress(Ansi.ProgressState state, byte progress = 0);

    /// <summary>
    ///     Displays a QR code for authentication.
    /// </summary>
    void DisplayQrCode(string challengeUrl);
}