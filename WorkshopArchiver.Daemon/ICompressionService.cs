namespace WorkshopArchiver.Daemon;

public interface ICompressionService
{
    Task<string> CompressDirectoryAsync(string sourceDir, string outputPath, CancellationToken ct = default);
}