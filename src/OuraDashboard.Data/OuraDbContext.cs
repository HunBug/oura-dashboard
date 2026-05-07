using Microsoft.EntityFrameworkCore;
using OuraDashboard.Data.Entities;

namespace OuraDashboard.Data;

public class OuraDbContext(DbContextOptions<OuraDbContext> options) : DbContext(options)
{
    public DbSet<OuraUser> Users => Set<OuraUser>();
    public DbSet<DailySleep> DailySleeps => Set<DailySleep>();
    public DbSet<SleepSession> SleepSessions => Set<SleepSession>();
    public DbSet<DailyReadiness> DailyReadinesses => Set<DailyReadiness>();
    public DbSet<HeartRateSample> HeartRateSamples => Set<HeartRateSample>();
    public DbSet<DailyStress> DailyStresses => Set<DailyStress>();
    public DbSet<DailyHrv> DailyHrvs => Set<DailyHrv>();
    public DbSet<DailyActivity> DailyActivities => Set<DailyActivity>();
    public DbSet<Vo2Max> Vo2Maxes => Set<Vo2Max>();
    public DbSet<DailySpo2> DailySpo2s => Set<DailySpo2>();
    public DbSet<DailyResilience> DailyResilienceRecords => Set<DailyResilience>();
    public DbSet<Workout> Workouts => Set<Workout>();
    public DbSet<WeatherLocation> WeatherLocations => Set<WeatherLocation>();
    public DbSet<WeatherStation> WeatherStations => Set<WeatherStation>();
    public DbSet<WeatherHourlySample> WeatherHourlySamples => Set<WeatherHourlySample>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // OuraUser
        model.Entity<OuraUser>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(u => u.Name).IsUnique();
        });

        // DailySleep — unique per user+day
        model.Entity<DailySleep>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailySleeps).HasForeignKey(x => x.UserId);
        });

        // SleepSession — unique per user+OuraId (multiple sessions per day possible)
        model.Entity<SleepSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.OuraId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Day });
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.HeartRateSeries).HasColumnType("jsonb");
            e.Property(x => x.HrvSeries).HasColumnType("jsonb");
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.SleepSessions).HasForeignKey(x => x.UserId);
        });

        // DailyReadiness — unique per user+day
        model.Entity<DailyReadiness>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailyReadinesses).HasForeignKey(x => x.UserId);
        });

        // HeartRateSample — unique per user+timestamp
        model.Entity<HeartRateSample>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Timestamp }).IsUnique();
            e.Property(x => x.Source).HasMaxLength(50);
            e.HasOne(x => x.User).WithMany(u => u.HeartRateSamples).HasForeignKey(x => x.UserId);
        });

        // DailyStress — unique per user+day
        model.Entity<DailyStress>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailyStresses).HasForeignKey(x => x.UserId);
        });

        // DailyHrv — unique per user+day
        model.Entity<DailyHrv>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailyHrvs).HasForeignKey(x => x.UserId);
        });

        // DailyActivity — unique per user+day
        model.Entity<DailyActivity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailyActivities).HasForeignKey(x => x.UserId);
        });

        // Vo2Max — unique per user+day
        model.Entity<Vo2Max>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.Vo2Maxes).HasForeignKey(x => x.UserId);
        });

        // DailySpo2 — unique per user+day
        model.Entity<DailySpo2>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailySpo2s).HasForeignKey(x => x.UserId);
        });

        // DailyResilience — unique per user+day
        model.Entity<DailyResilience>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.Day }).IsUnique();
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.DailyResilienceRecords).HasForeignKey(x => x.UserId);
        });

        // Workout — unique per user+OuraId (multiple workouts per day possible)
        model.Entity<Workout>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.OuraId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Day });
            e.Property(x => x.OuraId).HasMaxLength(50).IsRequired();
            e.Property(x => x.Activity).HasMaxLength(100);
            e.Property(x => x.Intensity).HasMaxLength(20);
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.User).WithMany(u => u.Workouts).HasForeignKey(x => x.UserId);
        });

        model.Entity<WeatherLocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Timezone).HasMaxLength(100).IsRequired();
            e.Property(x => x.RawJson).HasColumnType("jsonb");
        });

        model.Entity<WeatherStation>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WeatherLocationId, x.Source, x.StationCode, x.ElementCode }).IsUnique();
            e.Property(x => x.Source).HasMaxLength(60).IsRequired();
            e.Property(x => x.StationCode).HasMaxLength(80).IsRequired();
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.ElementCode).HasMaxLength(40);
            e.Property(x => x.ElementName).HasMaxLength(200);
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.WeatherLocation).WithMany(x => x.Stations).HasForeignKey(x => x.WeatherLocationId);
        });

        model.Entity<WeatherHourlySample>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WeatherLocationId, x.Source, x.Model, x.WeatherStationId, x.TimestampUtc }).IsUnique();
            e.HasIndex(x => new { x.WeatherLocationId, x.TimestampUtc });
            e.Property(x => x.Source).HasMaxLength(60).IsRequired();
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.TimestampLocal).HasColumnType("timestamp without time zone");
            e.Property(x => x.RawJson).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.WeatherLocation).WithMany(x => x.HourlySamples).HasForeignKey(x => x.WeatherLocationId);
            e.HasOne(x => x.WeatherStation).WithMany(x => x.HourlySamples).HasForeignKey(x => x.WeatherStationId);
        });
    }
}
