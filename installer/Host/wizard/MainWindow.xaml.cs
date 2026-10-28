using System.Windows;
using System.Windows.Controls;
using DXSuite.Host.Setup;

namespace DXSuite.Host.Setup.Wizard;

public partial class MainWindow : Window
{
    private readonly string _outputPath;

    // Known toggles, with on-prem-sensible defaults. Director is always on.
    private static readonly (string Name, bool Default)[] KnownModules =
    {
        ("Director", true), ("CoreData", true), ("Aviation", true), ("Design", true),
        ("NetJets", false), ("SLC", false), ("StyleGuide", true),
        ("Sentinel", false), ("Pulse", false),
    };

    private static readonly (string Name, bool Default)[] KnownFeatures =
    {
        ("Wayfinding", true), ("GroupDiscovery", true), ("DomainGroupAssignment", false),
        ("Engage", false), ("ContentPackage", true), ("Overrides", true), ("CounterScheduler", false),
    };

    private readonly Dictionary<string, CheckBox> _moduleBoxes = new();
    private readonly Dictionary<string, CheckBox> _featureBoxes = new();

    private DirectorDbState? _detectedState;

    // IIS/site/install-folder are owned by the MSI; we carry the incoming values through to
    // the JSON unchanged (InstallDir is pre-set by App from the MSI's INSTALLFOLDER property).
    private readonly SiteConfig _site;

    // Tab order after dropping the Site tab: 0 Database, 1 Authentication, 2 Application.
    private const int DatabaseTabIndex = 0;

    public MainWindow(string outputPath, SetupConfig cfg)
    {
        InitializeComponent();
        _outputPath = outputPath;
        _site = cfg.Site;
        BuildToggleCheckboxes(cfg);
        LoadFromConfig(cfg);
    }

    private void BuildToggleCheckboxes(SetupConfig cfg)
    {
        foreach (var (name, def) in KnownModules)
        {
            var box = new CheckBox
            {
                Content = name,
                Width = 150,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = cfg.Modules.TryGetValue(name, out var v) ? v : def,
                IsEnabled = name != "Director", // Director is mandatory
            };
            _moduleBoxes[name] = box;
            ModulesPanel.Children.Add(box);
        }

        foreach (var (name, def) in KnownFeatures)
        {
            var box = new CheckBox
            {
                Content = name,
                Width = 180,
                Margin = new Thickness(0, 0, 0, 6),
                IsChecked = cfg.Features.TryGetValue(name, out var v) ? v : def,
            };
            _featureBoxes[name] = box;
            FeaturesPanel.Children.Add(box);
        }
    }

    private void LoadFromConfig(SetupConfig cfg)
    {
        ServerBox.Text = cfg.Database.Server;
        DirectorDbBox.Text = cfg.Database.DirectorDatabase;
        TrustCertBox.IsChecked = cfg.Database.TrustServerCertificate;

        AdminIntegratedBox.IsChecked = cfg.Database.Admin.IntegratedSecurity;
        AdminUserBox.Text = cfg.Database.Admin.Username;
        AdminPassBox.Password = cfg.Database.Admin.Password;

        AppIntegratedBox.IsChecked = cfg.Database.App.IntegratedSecurity;
        AppUserBox.Text = cfg.Database.App.Username;
        AppPassBox.Password = cfg.Database.App.Password;

        CoreDataDbBox.Text = cfg.Database.ExternalDatabases.GetValueOrDefault("CoreData", "DCMS_Core");
        AviationDbBox.Text = cfg.Database.ExternalDatabases.GetValueOrDefault("Aviation", "DCMS_Core");
        RunSeedBox.IsChecked = cfg.Database.RunSeed;

        SelectComboItem(AuthTypeBox, string.IsNullOrWhiteSpace(cfg.Auth.Type) ? "Local" : cfg.Auth.Type);
        LocalAccountsBox.IsChecked = cfg.Auth.LocalAccountsEnabled;
        MsTenantBox.Text = cfg.Auth.Microsoft.TenantId;
        MsDomainBox.Text = cfg.Auth.Microsoft.Domain;
        MsClientIdBox.Text = cfg.Auth.Microsoft.ClientId;
        MsClientSecretBox.Password = cfg.Auth.Microsoft.ClientSecret;
        OktaAuthorityBox.Text = cfg.Auth.Okta.Authority;
        OktaClientIdBox.Text = cfg.Auth.Okta.ClientId;
        OktaClientSecretBox.Password = cfg.Auth.Okta.ClientSecret;
        GoogleClientIdBox.Text = cfg.Auth.Google.ClientId;
        GoogleClientSecretBox.Password = cfg.Auth.Google.ClientSecret;
        LdapServerBox.Text = cfg.Auth.Ldap.Server;
        LdapBaseDnBox.Text = cfg.Auth.Ldap.BaseDn;
        LdapSvcDnBox.Text = cfg.Auth.Ldap.ServiceAccountDn;
        LdapSvcPassBox.Password = cfg.Auth.Ldap.ServiceAccountPassword;
        LdapEmailSuffixBox.Text = cfg.Auth.Ldap.EmailDomainSuffix;

        CustomerCodeBox.Text = cfg.Customer.Code;
        TenantBox.Text = string.IsNullOrWhiteSpace(cfg.Customer.Tenant) ? "default" : cfg.Customer.Tenant;
        BaseUncBox.Text = cfg.FilePaths.BaseUNC;
        SiteUrlBox.Text = cfg.FilePaths.SiteUrl;
    }

    private SetupConfig BuildConfig()
    {
        var cfg = new SetupConfig
        {
            Site = _site, // owned by the MSI; carried through unchanged
            Database = BuildDatabaseConfig(),
            Auth =
            {
                Type = SelectedComboText(AuthTypeBox) ?? "Local",
                LocalAccountsEnabled = LocalAccountsBox.IsChecked == true,
                Microsoft =
                {
                    TenantId = MsTenantBox.Text.Trim(),
                    Domain = MsDomainBox.Text.Trim(),
                    ClientId = MsClientIdBox.Text.Trim(),
                    ClientSecret = MsClientSecretBox.Password,
                },
                Okta =
                {
                    Authority = OktaAuthorityBox.Text.Trim(),
                    ClientId = OktaClientIdBox.Text.Trim(),
                    ClientSecret = OktaClientSecretBox.Password,
                },
                Google =
                {
                    ClientId = GoogleClientIdBox.Text.Trim(),
                    ClientSecret = GoogleClientSecretBox.Password,
                },
                Ldap =
                {
                    Server = LdapServerBox.Text.Trim(),
                    BaseDn = LdapBaseDnBox.Text.Trim(),
                    ServiceAccountDn = LdapSvcDnBox.Text.Trim(),
                    ServiceAccountPassword = LdapSvcPassBox.Password,
                    EmailDomainSuffix = LdapEmailSuffixBox.Text.Trim(),
                },
            },
            FilePaths = { BaseUNC = BaseUncBox.Text.Trim(), SiteUrl = SiteUrlBox.Text.Trim() },
            Customer = { Code = CustomerCodeBox.Text.Trim(), Tenant = TenantBox.Text.Trim() },
        };

        foreach (var (name, box) in _moduleBoxes) cfg.Modules[name] = box.IsChecked == true;
        foreach (var (name, box) in _featureBoxes) cfg.Features[name] = box.IsChecked == true;
        return cfg;
    }

    private DatabaseConfig BuildDatabaseConfig()
    {
        var db = new DatabaseConfig
        {
            Server = ServerBox.Text.Trim(),
            DirectorDatabase = DirectorDbBox.Text.Trim(),
            TrustServerCertificate = TrustCertBox.IsChecked == true,
            Admin = new SqlCredential
            {
                IntegratedSecurity = AdminIntegratedBox.IsChecked == true,
                Username = AdminUserBox.Text.Trim(),
                Password = AdminPassBox.Password,
            },
            App = new SqlCredential
            {
                IntegratedSecurity = AppIntegratedBox.IsChecked == true,
                Username = AppUserBox.Text.Trim(),
                Password = AppPassBox.Password,
            },
            RunSeed = RunSeedBox.IsChecked == true,
            OperatorConfirmedExistingBaseline = ConfirmBaselineBox.IsChecked == true,
        };

        var coreData = CoreDataDbBox.Text.Trim();
        if (coreData.Length > 0) db.ExternalDatabases["CoreData"] = coreData;
        var aviation = AviationDbBox.Text.Trim();
        if (aviation.Length > 0) db.ExternalDatabases["Aviation"] = aviation;

        return db;
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        try
        {
            await DetectAsync(showFresh: true);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Tests the admin connection and, if the database already exists, detects its migration
    /// state. Reveals the baseline-confirmation panel for an existing untracked schema.
    /// </summary>
    private async Task<bool> DetectAsync(bool showFresh)
    {
        var db = BuildDatabaseConfig();
        if (db.Server.Length == 0 || db.DirectorDatabase.Length == 0)
        {
            DbStatusText.Text = "Enter the server and Director database name first.";
            return false;
        }

        DbStatusText.Text = "Testing connection…";
        var masterConn = ConnectionStringFactory.ForAdmin(db, "master");

        var (ok, message) = await DirectorDbDetector.TestConnectionAsync(masterConn);
        if (!ok)
        {
            _detectedState = null;
            DbStatusText.Text = $"Connection failed: {message}";
            BaselinePanel.Visibility = Visibility.Collapsed;
            return false;
        }

        if (!await DirectorDbDetector.DatabaseExistsAsync(masterConn, db.DirectorDatabase))
        {
            _detectedState = DirectorDbState.Fresh;
            BaselinePanel.Visibility = Visibility.Collapsed;
            if (showFresh)
                DbStatusText.Text = "Connected. Database does not exist yet — it will be created and migrated (Fresh).";
            return true;
        }

        var dbConn = ConnectionStringFactory.ForAdmin(db, db.DirectorDatabase);
        var state = await DirectorDbDetector.DetectAsync(dbConn);
        _detectedState = state;

        switch (state)
        {
            case DirectorDbState.Fresh:
                DbStatusText.Text = "Connected. Empty database — schema will be created (Fresh).";
                BaselinePanel.Visibility = Visibility.Collapsed;
                break;
            case DirectorDbState.Tracked:
                DbStatusText.Text = "Connected. Migration history present — only pending migrations will apply.";
                BaselinePanel.Visibility = Visibility.Collapsed;
                break;
            case DirectorDbState.ExistingUntracked:
                DbStatusText.Text = "Connected. Existing schema with no migration tracking — confirmation required below.";
                BaselinePanel.Visibility = Visibility.Visible;
                Tabs.SelectedIndex = DatabaseTabIndex;
                break;
        }

        return true;
    }

    private async void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;

        FinishButton.IsEnabled = false;
        try
        {
            // Detect now if the operator never clicked Test, so the confirmation gate applies.
            if (_detectedState is null)
                await DetectAsync(showFresh: false);

            if (_detectedState == DirectorDbState.ExistingUntracked && ConfirmBaselineBox.IsChecked != true)
            {
                Tabs.SelectedIndex = DatabaseTabIndex;
                BaselinePanel.Visibility = Visibility.Visible;
                MessageBox.Show(this,
                    "An existing untracked DXSuite schema was found. Confirm it matches the baseline " +
                    "before continuing.", "Confirmation required", MessageBoxButton.OK, MessageBoxImage.Warning);
                FinishButton.IsEnabled = true;
                return;
            }

            var cfg = BuildConfig();
            cfg.Save(_outputPath);

            Environment.ExitCode = 0;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            FinishButton.IsEnabled = true;
        }
    }

    private bool Validate()
    {
        if (ServerBox.Text.Trim().Length == 0) return Fail("SQL Server is required.", DatabaseTabIndex);
        if (DirectorDbBox.Text.Trim().Length == 0) return Fail("Director database name is required.", DatabaseTabIndex);

        if (AdminIntegratedBox.IsChecked != true && AdminUserBox.Text.Trim().Length == 0)
            return Fail("Admin login is required (or use Windows authentication).", DatabaseTabIndex);
        if (AppIntegratedBox.IsChecked != true && AppUserBox.Text.Trim().Length == 0)
            return Fail("Application login is required (or use Windows authentication).", DatabaseTabIndex);
        return true;
    }

    private bool Fail(string message, int tabIndex)
    {
        Tabs.SelectedIndex = tabIndex;
        FooterText.Text = message;
        MessageBox.Show(this, message, "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // Non-zero tells the MSI the operator cancelled.
        Environment.ExitCode = 1602; // ERROR_INSTALL_USEREXIT
        Close();
    }

    private static void SelectComboItem(ComboBox combo, string text)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string? SelectedComboText(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();
}
