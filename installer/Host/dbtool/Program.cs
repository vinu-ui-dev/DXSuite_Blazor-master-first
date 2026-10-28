using DXSuite.Host.Setup;

// Headless installer helper. Invoked by the MSI's deferred custom actions; also runnable by
// hand for diagnostics. Exit codes are the contract with the installer:
//   0 success | 1 error | 2 needs operator confirmation (provision) | 3 usage error
return await DbToolProgram.RunAsync(args);

internal static class DbToolProgram
{
    private const int Ok = 0, Error = 1, NeedsConfirmation = 2, Usage = 3;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return Usage;
        }

        var command = args[0].ToLowerInvariant();
        var opts = ParseOptions(args.Skip(1));
        var log = CreateLogger();

        try
        {
            return command switch
            {
                "test-connection" => await TestConnectionAsync(opts, log),
                "detect" => await DetectAsync(opts, log),
                "apply-settings" => ApplySettings(opts, log),
                "provision" => await ProvisionAsync(opts, log),
                "install" => await InstallAsync(opts, log),
                _ => Fail($"Unknown command '{command}'.", Usage),
            };
        }
        catch (Exception ex)
        {
            log($"ERROR: {ex.Message}");
            log(ex.ToString());
            return Error;
        }
    }

    private static async Task<int> TestConnectionAsync(Dictionary<string, string> opts, Action<string> log)
    {
        var cfg = LoadConfig(opts);
        var which = opts.GetValueOrDefault("which", "app");
        var dbName = cfg.Database.DirectorDatabase;
        var connStr = which == "admin"
            ? ConnectionStringFactory.ForAdmin(cfg.Database, dbName)
            : ConnectionStringFactory.ForDatabase(cfg.Database, "Director", dbName);

        var (ok, message) = await DirectorDbDetector.TestConnectionAsync(connStr);
        log(ok ? $"Connection OK: {message}" : $"Connection FAILED: {message}");
        return ok ? Ok : Error;
    }

    private static async Task<int> DetectAsync(Dictionary<string, string> opts, Action<string> log)
    {
        var cfg = LoadConfig(opts);
        var dbConn = ConnectionStringFactory.ForAdmin(cfg.Database, cfg.Database.DirectorDatabase);

        if (!await DirectorDbDetector.DatabaseExistsAsync(
                ConnectionStringFactory.Retarget(dbConn, "master"), cfg.Database.DirectorDatabase))
        {
            log("State: Fresh (database does not exist yet)");
            Console.WriteLine(nameof(DirectorDbState.Fresh));
            return Ok;
        }

        var state = await DirectorDbDetector.DetectAsync(dbConn);
        log($"State: {state}");
        Console.WriteLine(state.ToString()); // machine-readable for the wizard
        return Ok;
    }

    private static int ApplySettings(Dictionary<string, string> opts, Action<string> log)
    {
        var cfg = LoadConfig(opts);
        var installDir = RequireOption(opts, "installdir");
        var path = AppSettingsWriter.Write(cfg, installDir);
        log($"Wrote {path}");
        return Ok;
    }

    private static async Task<int> ProvisionAsync(Dictionary<string, string> opts, Action<string> log)
    {
        var cfg = LoadConfig(opts);
        var db = cfg.Database;

        var adminConn = ConnectionStringFactory.ForAdmin(db, db.DirectorDatabase);

        var seedText = "";
        if (db.RunSeed)
        {
            var seedPath = RequireOption(opts, "seed");
            seedText = File.ReadAllText(seedPath);
        }

        // The wizard records the confirmation in config; --confirm-baseline can force it.
        var confirmed = db.OperatorConfirmedExistingBaseline || opts.ContainsKey("confirm-baseline");

        var provisioner = new DirectorDatabaseProvisioner();
        var outcome = await provisioner.ProvisionAsync(
            adminConn, db.DirectorDatabase, seedText, confirmed,
            new Progress<string>(log));

        if (outcome.NeedsOperatorConfirmation)
        {
            log("HALTED: an existing untracked DXSuite schema was found. Operator must confirm " +
                "it matches the baseline before it can be stamped and migrated.");
            return NeedsConfirmation;
        }

        log($"Provisioning complete. State={outcome.DetectedState}, BaselineStamped={outcome.BaselineStamped}");
        return Ok;
    }

    /// <summary>
    /// The single deferred step the MSI invokes: write appsettings, then provision the DB.
    /// Derives the install folder and seed path from the config (Site.InstallDir, set to the
    /// MSI's INSTALLFOLDER) so the MSI's custom action needs no property substitution.
    /// </summary>
    private static async Task<int> InstallAsync(Dictionary<string, string> opts, Action<string> log)
    {
        var cfg = LoadConfig(opts);
        var installDir = opts.GetValueOrDefault("installdir", cfg.Site.InstallDir);
        if (string.IsNullOrWhiteSpace(installDir))
            throw new ArgumentException("Install folder is unknown (no --installdir and no Site.InstallDir in config).");

        opts["installdir"] = installDir;
        if (cfg.Database.RunSeed && !opts.ContainsKey("seed"))
            opts["seed"] = Path.Combine(installDir, "db", "director-seed.sql");

        var settingsResult = ApplySettings(opts, log);
        if (settingsResult != Ok) return settingsResult;
        return await ProvisionAsync(opts, log);
    }

    private static SetupConfig LoadConfig(Dictionary<string, string> opts)
    {
        var path = opts.GetValueOrDefault("config", SetupConfig.DefaultPath);
        return SetupConfig.Load(path);
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null) opts[pending] = "true"; // previous was a flag
                pending = a[2..];
            }
            else if (pending is not null)
            {
                opts[pending] = a;
                pending = null;
            }
        }
        if (pending is not null) opts[pending] = "true";
        return opts;
    }

    private static string RequireOption(Dictionary<string, string> opts, string name) =>
        opts.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ArgumentException($"Missing required --{name} option.");

    private static int Fail(string message, int code)
    {
        Console.Error.WriteLine(message);
        return code;
    }

    private static Action<string> CreateLogger()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "dxsuite-dbtool.log");
        return message =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(line);
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { /* best effort */ }
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            DXSuite.Host.DbTool — installer database helper

            Commands:
              test-connection --config <json> [--which admin|app]
              detect          --config <json>
              apply-settings  --config <json> --installdir <dir>
              provision       --config <json> [--seed <seed.sql>] [--confirm-baseline]

            --config defaults to %TEMP%\dxsuite-setup.json
            """);
    }
}
