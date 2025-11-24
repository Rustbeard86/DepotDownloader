using System;
using System.Text;
using QRCoder;

namespace DepotDownloader.Lib;

/// <summary>
///     Console-based implementation of IUserInterface.
/// </summary>
public class ConsoleUserInterface : IUserInterface
{
    public void Write(string message)
    {
        Console.Write(message);
    }

    public void Write(string format, params object[] args)
    {
        Console.Write(format, args);
    }

    public void WriteDebug(string category, string message)
    {
        Console.Error.WriteLine($"[DEBUG][{category}] {message}");
    }

    public void WriteLine()
    {
        Console.WriteLine();
    }

    public void WriteLine(string message)
    {
        Console.WriteLine(message);
    }

    public void WriteLine(string format, params object[] args)
    {
        Console.WriteLine(format, args);
    }

    public void WriteError(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void WriteError(string format, params object[] args)
    {
        Console.Error.WriteLine(format, args);
    }

    public string ReadLine()
    {
        return Console.ReadLine();
    }

    public string ReadPassword()
    {
        ConsoleKeyInfo keyInfo;
        var password = new StringBuilder();

        do
        {
            keyInfo = Console.ReadKey(true);

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }

                continue;
            }

            /* Printable ASCII characters only */
            var c = keyInfo.KeyChar;
            if (c is < ' ' or > '~') continue;

            password.Append(c);
            Console.Write('*');
        } while (keyInfo.Key != ConsoleKey.Enter);

        return password.ToString();
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        return Console.ReadKey(intercept);
    }

    public void UpdateProgress(ulong downloaded, ulong total)
    {
        Ansi.Progress(downloaded, total);
    }

    public void UpdateProgress(Ansi.ProgressState state, byte progress = 0)
    {
        Ansi.Progress(state, progress);
    }

    public void DisplayQrCode(string challengeUrl)
    {
        // Generate QR code
        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.L);

        // Default to ASCII art in console
        using var qrCode = new AsciiQRCode(qrCodeData);
        var qrCodeAsAsciiArt = qrCode.GetLineByLineGraphic(1, drawQuietZones: true);

        Console.WriteLine("Use the Steam Mobile App to sign in with this QR code:");
        Console.WriteLine();

        foreach (var line in qrCodeAsAsciiArt)
            Console.WriteLine(line);
    }

    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;
}