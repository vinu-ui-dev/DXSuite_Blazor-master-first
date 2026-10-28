using Microsoft.EntityFrameworkCore;

namespace DXSuite.Aviation.Entities;

public partial class AviationDbContext : DbContext
{
    public AviationDbContext()
    {
    }

    public AviationDbContext(DbContextOptions<AviationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TblAirline> TblAirlines { get; set; }

    public virtual DbSet<TblAirlineGate> TblAirlineGates { get; set; }

    public virtual DbSet<TblArrival> TblArrivals { get; set; }

    public virtual DbSet<TblBaggageTerminal> TblBaggageTerminals { get; set; }

    public virtual DbSet<TblCarousel> TblCarousels { get; set; }

    public virtual DbSet<TblDeparture> TblDepartures { get; set; }

    public virtual DbSet<TblFlight> TblFlights { get; set; }

    public virtual DbSet<TblFlightStatus> TblFlightStatuses { get; set; }

    public virtual DbSet<TblGate> TblGates { get; set; }

    public virtual DbSet<TblLocation> TblLocations { get; set; }

    public virtual DbSet<TblRcpcode> TblRcpcodes { get; set; }

    public virtual DbSet<TblSpacialDisposition> TblSpacialDispositions { get; set; }

    public virtual DbSet<ViewBhsCarouselChangeReport> ViewBhsCarouselChangeReports { get; set; }

    public virtual DbSet<ViewBhsLastBagExpiredReport> ViewBhsLastBagExpiredReports { get; set; }

    public virtual DbSet<ViewBhsUsageReport> ViewBhsUsageReports { get; set; }

    public virtual DbSet<ViewDuplicateFlightCheckArrival> ViewDuplicateFlightCheckArrivals { get; set; }

    public virtual DbSet<ViewGetWeatherDestination> ViewGetWeatherDestinations { get; set; }

    public virtual DbSet<ViewRadGidsFight> ViewRadGidsFights { get; set; }

    public virtual DbSet<ViewRadGidsFightsAllDay> ViewRadGidsFightsAllDays { get; set; }

    public virtual DbSet<ViewRadGidsFightsOld> ViewRadGidsFightsOlds { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TblAirline>(entity =>
        {
            entity.HasKey(e => e.RecordId).HasName("PK_tblCarrier");

            entity.Property(e => e.RecordId).ValueGeneratedNever();
            entity.Property(e => e.CbidiconWidth).HasDefaultValue(1);
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.LastBagDelaySeconds).HasDefaultValue(120);
            entity.Property(e => e.LastBagExpirationMinutes).HasDefaultValue(45);

            entity.HasMany(d => d.IataCodes).WithMany(p => p.AirlineCodes)
                .UsingEntity<Dictionary<string, object>>(
                    "TblAirlineLocation",
                    r =>
                    {
                        r.IndexerProperty<string>("IataCode")
                            .HasMaxLength(3)
                            .IsUnicode(false)
                            .HasColumnName("IATA_CODE");
                        return r.HasOne<TblLocation>().WithMany()
                            .HasForeignKey("IataCode")
                            .OnDelete(DeleteBehavior.ClientSetNull)
                            .HasConstraintName("FK_tblAirlineLocation_tblLocation");
                    },
                    l =>
                    {
                        // Explicitly declare AirlineCode as string before FK inference
                        // to prevent EF from defaulting to int (TblAirline's PK type)
                        l.IndexerProperty<string>("AirlineCode")
                            .HasMaxLength(3)
                            .IsUnicode(false)
                            .HasColumnName("Airline_Code");
                        return l.HasOne<TblAirline>().WithMany()
                            .HasPrincipalKey("AirlineCode")
                            .HasForeignKey("AirlineCode")
                            .OnDelete(DeleteBehavior.ClientSetNull)
                            .HasConstraintName("FK_tblAirlineLocation_tblAirline");
                    },
                    j =>
                    {
                        j.HasKey("AirlineCode", "IataCode");
                        j.ToTable("tblAirlineLocation");
                    });
        });

        modelBuilder.Entity<TblAirlineGate>(entity =>
        {
            entity.HasOne(d => d.AirlineCodeNavigation).WithMany(p => p.TblAirlineGates)
                .HasPrincipalKey(p => p.AirlineCode)
                .HasForeignKey(d => d.AirlineCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAirlineGate_tblAirline");

            entity.HasOne(d => d.GatNavigation).WithMany(p => p.TblAirlineGates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_tblAirlineGate_tblGate");
        });

        modelBuilder.Entity<TblArrival>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Type).HasDefaultValue("A");
        });

        modelBuilder.Entity<TblBaggageTerminal>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.HelpMessageStyle).HasDefaultValue("d");
            entity.Property(e => e.LastSync).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Orientation).HasDefaultValue("L");
            entity.Property(e => e.ShowHelpMessaging).HasDefaultValue(false);
            entity.Property(e => e.ShowMetrics).HasDefaultValue(false);
            entity.Property(e => e.ShowWeather).HasDefaultValue(false);

            entity.HasOne(d => d.Carousel).WithMany(p => p.TblBaggageTerminals).HasConstraintName("FK_tblBaggageTerminal_tblCarousel");
        });

        modelBuilder.Entity<TblCarousel>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Output).HasDefaultValue(true);
        });

        modelBuilder.Entity<TblDeparture>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.Type).HasDefaultValue("D");
        });

        modelBuilder.Entity<TblFlight>(entity =>
        {
            entity.Property(e => e.FlightRecordId).ValueGeneratedNever();
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.CarNavigation).WithMany(p => p.TblFlights).HasConstraintName("FK_tblFlights_tblCarousel");

            entity.HasOne(d => d.FlcNavigation).WithMany(p => p.TblFlights)
                .HasPrincipalKey(p => p.AirlineCode)
                .HasForeignKey(d => d.Flc)
                .HasConstraintName("FK_tblFlights_tblAirline");
        });

        modelBuilder.Entity<TblFlightStatus>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<TblGate>(entity =>
        {
            entity.Property(e => e.Defaultstyle).HasDefaultValue("M");
            entity.Property(e => e.Orientation).HasDefaultValue("L");
        });

        modelBuilder.Entity<TblLocation>(entity =>
        {
            entity.Property(e => e.CreateDate).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.LanguageCodes).HasDefaultValue("");
        });

        modelBuilder.Entity<TblRcpcode>(entity =>
        {
            entity.HasKey(e => e.Rcpcode).HasName("PK_tblRCP");
        });

        modelBuilder.Entity<TblSpacialDisposition>(entity =>
        {
            entity.HasKey(e => new { e.Origin, e.Destination }).HasName("PK_tblDeviceSpacialDisposition");
        });

        modelBuilder.Entity<ViewBhsCarouselChangeReport>(entity =>
        {
            entity.ToView("View_BHS_Carousel_Change_Report");
        });

        modelBuilder.Entity<ViewBhsLastBagExpiredReport>(entity =>
        {
            entity.ToView("View_BHS_Last_Bag_Expired_Report");
        });

        modelBuilder.Entity<ViewBhsUsageReport>(entity =>
        {
            entity.ToView("View_BHS_Usage_Report");
        });

        modelBuilder.Entity<ViewDuplicateFlightCheckArrival>(entity =>
        {
            entity.ToView("View_DuplicateFlightCheckArrivals");
        });

        modelBuilder.Entity<ViewGetWeatherDestination>(entity =>
        {
            entity.ToView("View_GetWeatherDestinations");
        });

        modelBuilder.Entity<ViewRadGidsFight>(entity =>
        {
            entity.ToView("View_RAD_GIDS_Fights");
        });

        modelBuilder.Entity<ViewRadGidsFightsAllDay>(entity =>
        {
            entity.ToView("View_RAD_GIDS_FightsAllDay");
        });

        modelBuilder.Entity<ViewRadGidsFightsOld>(entity =>
        {
            entity.ToView("View_RAD_GIDS_FightsOld");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
