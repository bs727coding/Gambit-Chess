using Gambit.Server;

// "Gambit.Server <command>" runs an admin command (invite, users, ban, ...) instead of the server.
if (args.Length > 0 && AdminCommands.IsCommand(args[0]))
{
    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    return AdminCommands.Run(args, ServerHost.LoadOptions(configuration), Console.Out);
}

ServerHost.CreateApp(args).Run();
return 0;
