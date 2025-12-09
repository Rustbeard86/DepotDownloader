using DepotDownloader.Client;
using DepotDownloader.Client.Commands;
using DepotDownloader.Lib;
using NSubstitute;

namespace DepotDownloader.Tests.Helpers;

/// <summary>
/// Builder for creating test command contexts.
/// </summary>
public class CommandContextBuilder
{
    private DepotDownloaderClient? _client;
    private IUserInterface _ui = new TestUserInterface();
    private ArgumentParser? _args;
    private ConfigFile? _config;
    private bool _jsonOutput;

    public CommandContextBuilder WithClient(DepotDownloaderClient client)
    {
        _client = client;
        return this;
    }

    public CommandContextBuilder WithMockClient()
    {
        _client = Substitute.For<DepotDownloaderClient>(_ui);
        return this;
    }

    public CommandContextBuilder WithUserInterface(IUserInterface ui)
    {
        _ui = ui;
        return this;
    }

    public CommandContextBuilder WithArgs(params string[] args)
    {
        _args = new ArgumentParser(args);
        return this;
    }

    public CommandContextBuilder WithConfig(ConfigFile config)
    {
        _config = config;
        return this;
    }

    public CommandContextBuilder WithJsonOutput(bool enabled = true)
    {
        _jsonOutput = enabled;
        return this;
    }

    public CommandContext Build()
    {
        _client ??= Substitute.For<DepotDownloaderClient>(_ui);
        _args ??= new ArgumentParser([]);
        _config ??= new ConfigFile();

        return new CommandContext(_client, _ui, _args, _config, _jsonOutput);
    }

    public (CommandContext Context, TestUserInterface Ui) BuildWithTestUi()
    {
        var testUi = new TestUserInterface();
        _ui = testUi;
        return (Build(), testUi);
    }
}
