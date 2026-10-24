using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Api.Data.Entities;

namespace TravelPlanner.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Interest> Interests => Set<Interest>();
    public DbSet<TripInterest> TripInterests => Set<TripInterest>();
    public DbSet<GenerationRun> GenerationRuns => Set<GenerationRun>();
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<ItineraryStop> ItineraryStops => Set<ItineraryStop>();
    public DbSet<Place> Places => Set<Place>();
    public DbSet<UserSavedPlace> UserSavedPlaces => Set<UserSavedPlace>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Creates all the ASP.NET Core Identity tables (AspNetUsers, AspNetRoles, ...)
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(100);
        });

        builder.Entity<Trip>(e =>
        {
            e.Property(t => t.DestinationName).HasMaxLength(200);
            e.Property(t => t.DestinationCountryCode).HasMaxLength(2);
            e.Property(t => t.BudgetLevel).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Notes).HasMaxLength(2000);

            // Deleting a user deletes their trips
            e.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_Trips_EndDate_After_StartDate", "\"EndDate\" >= \"StartDate\""));
        });

        builder.Entity<Interest>(e =>
        {
            e.Property(i => i.Name).HasMaxLength(100);
            e.Property(i => i.Description).HasMaxLength(500);
            e.HasIndex(i => i.Name).IsUnique();
        });

        builder.Entity<TripInterest>(e =>
        {
            e.HasKey(ti => new { ti.TripId, ti.InterestId });

            e.HasOne(ti => ti.Trip)
                .WithMany(t => t.TripInterests)
                .HasForeignKey(ti => ti.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ti => ti.Interest)
                .WithMany(i => i.TripInterests)
                .HasForeignKey(ti => ti.InterestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GenerationRun>(e =>
        {
            e.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(g => g.Provider).HasMaxLength(50);
            e.Property(g => g.Model).HasMaxLength(100);
            e.Property(g => g.PromptVersion).HasMaxLength(50);
            e.Property(g => g.ErrorCode).HasMaxLength(100);
            e.Property(g => g.ErrorMessage).HasMaxLength(2000);
            e.Property(g => g.RequestPayload).HasColumnType("jsonb");
            e.Property(g => g.ResponsePayload).HasColumnType("jsonb");

            e.HasOne(g => g.Trip)
                .WithMany(t => t.GenerationRuns)
                .HasForeignKey(g => g.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_GenerationRuns_Duration", "\"DurationMilliseconds\" IS NULL OR \"DurationMilliseconds\" >= 0"));
        });

        builder.Entity<Itinerary>(e =>
        {
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Summary).HasMaxLength(2000);

            e.HasOne(i => i.Trip)
                .WithMany(t => t.Itineraries)
                .HasForeignKey(i => i.TripId)
                .OnDelete(DeleteBehavior.Cascade);

            // One-to-one: a run produces at most one itinerary (EF makes GenerationRunId unique).
            // Deleting the run keeps the itinerary and sets GenerationRunId to null.
            e.HasOne(i => i.GenerationRun)
                .WithOne(g => g.Itinerary)
                .HasForeignKey<Itinerary>(i => i.GenerationRunId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(i => new { i.TripId, i.RevisionNumber }).IsUnique();

            // At most one IsCurrent = true itinerary per trip
            e.HasIndex(i => i.TripId)
                .IsUnique()
                .HasFilter("\"IsCurrent\" = true")
                .HasDatabaseName("IX_Itineraries_OneCurrentPerTrip");

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_Itineraries_RevisionNumber", "\"RevisionNumber\" > 0"));
        });

        builder.Entity<ItineraryDay>(e =>
        {
            e.Property(d => d.Theme).HasMaxLength(200);
            e.Property(d => d.Notes).HasMaxLength(2000);

            e.HasOne(d => d.Itinerary)
                .WithMany(i => i.Days)
                .HasForeignKey(d => d.ItineraryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(d => new { d.ItineraryId, d.DayNumber }).IsUnique();
            e.HasIndex(d => new { d.ItineraryId, d.ItineraryDate }).IsUnique();

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_ItineraryDays_DayNumber", "\"DayNumber\" > 0"));
        });

        builder.Entity<ItineraryStop>(e =>
        {
            e.Property(s => s.TimeSlot).HasConversion<string>().HasMaxLength(20);
            e.Property(s => s.DisplayName).HasMaxLength(200);
            e.Property(s => s.Category).HasMaxLength(100);
            e.Property(s => s.ShortDescription).HasMaxLength(1000);
            e.Property(s => s.DisplayAddress).HasMaxLength(300);
            e.Property(s => s.EstimatedCost).HasPrecision(10, 2);
            e.Property(s => s.CurrencyCode).HasMaxLength(3);

            e.HasOne(s => s.ItineraryDay)
                .WithMany(d => d.Stops)
                .HasForeignKey(s => s.ItineraryDayId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a place keeps the stop and sets PlaceId to null
            e.HasOne(s => s.Place)
                .WithMany(p => p.Stops)
                .HasForeignKey(s => s.PlaceId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(s => new { s.ItineraryDayId, s.SortOrder }).IsUnique();

            e.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_ItineraryStops_SortOrder", "\"SortOrder\" > 0");
                tb.HasCheckConstraint("CK_ItineraryStops_EstimatedCost", "\"EstimatedCost\" IS NULL OR \"EstimatedCost\" >= 0");
                tb.HasCheckConstraint("CK_ItineraryStops_VisitMinutes", "\"EstimatedVisitMinutes\" IS NULL OR \"EstimatedVisitMinutes\" > 0");
                tb.HasCheckConstraint("CK_ItineraryStops_TravelMinutes", "\"TravelMinutesFromPrevious\" IS NULL OR \"TravelMinutesFromPrevious\" >= 0");
                tb.HasCheckConstraint("CK_ItineraryStops_Latitude", "\"DisplayLatitude\" IS NULL OR \"DisplayLatitude\" BETWEEN -90 AND 90");
                tb.HasCheckConstraint("CK_ItineraryStops_Longitude", "\"DisplayLongitude\" IS NULL OR \"DisplayLongitude\" BETWEEN -180 AND 180");
                tb.HasCheckConstraint("CK_ItineraryStops_CoordinatePair", "(\"DisplayLatitude\" IS NULL) = (\"DisplayLongitude\" IS NULL)");
                tb.HasCheckConstraint("CK_ItineraryStops_Times", "\"StartTimeLocal\" IS NULL OR \"EndTimeLocal\" IS NULL OR \"EndTimeLocal\" > \"StartTimeLocal\"");
            });
        });

        builder.Entity<Place>(e =>
        {
            e.Property(p => p.GooglePlaceId).HasMaxLength(300);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.FormattedAddress).HasMaxLength(300);
            e.Property(p => p.PrimaryCategory).HasMaxLength(100);
            e.Property(p => p.GoogleMapsUri).HasMaxLength(500);
            e.Property(p => p.OpeningHours).HasColumnType("jsonb");

            // Unique when present: PostgreSQL allows many NULLs in a unique index
            e.HasIndex(p => p.GooglePlaceId).IsUnique();

            e.ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_Places_Latitude", "\"Latitude\" IS NULL OR \"Latitude\" BETWEEN -90 AND 90");
                tb.HasCheckConstraint("CK_Places_Longitude", "\"Longitude\" IS NULL OR \"Longitude\" BETWEEN -180 AND 180");
                tb.HasCheckConstraint("CK_Places_CoordinatePair", "(\"Latitude\" IS NULL) = (\"Longitude\" IS NULL)");
            });
        });

        builder.Entity<UserSavedPlace>(e =>
        {
            e.Property(u => u.Notes).HasMaxLength(1000);

            // Deleting a user deletes their saved places
            e.HasOne(u => u.User)
                .WithMany()
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A place that someone saved can't be deleted
            e.HasOne(u => u.Place)
                .WithMany(p => p.SavedBy)
                .HasForeignKey(u => u.PlaceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(u => new { u.UserId, u.PlaceId }).IsUnique();
        });
    }
}
