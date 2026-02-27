using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Spydomo.Common.Enums;
using System.Text.Json;

namespace Spydomo.Models;

public partial class SpydomoContext : DbContext
{
    public SpydomoContext()
    {
    }

    public SpydomoContext(DbContextOptions<SpydomoContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AiUsageLog> AiUsageLogs { get; set; }
    public virtual DbSet<AggregatedCounter> AggregatedCounters { get; set; }

    public virtual DbSet<Client> Clients { get; set; }

    public virtual DbSet<Company> Companies { get; set; }
    public virtual DbSet<CompanyCategory> CompanyCategories { get; set; }

    public virtual DbSet<CompanyGroupStrategicSummaryState> CompanyGroupStrategicSummaryStates { get; set; }
    public virtual DbSet<CompanyKeyword> CompanyKeywords { get; set; }

    public DbSet<CompanyRelation> CompanyRelations => Set<CompanyRelation>();
    public DbSet<CompanyRelationRun> CompanyRelationRuns => Set<CompanyRelationRun>();

    public virtual DbSet<Competitor> Competitors { get; set; }

    public virtual DbSet<CompetitorName> CompetitorNames { get; set; }

    public virtual DbSet<Counter> Counters { get; set; }

    public virtual DbSet<Country> Countries { get; set; }

    public virtual DbSet<DataSource> DataSources { get; set; }

    public virtual DbSet<DataSourceType> DataSourceTypes { get; set; }
    public DbSet<FilteredUrl> FilteredUrls { get; set; }

    public DbSet<GroupSnapshot> GroupSnapshots { get; set; }
    public virtual DbSet<Hash> Hashes { get; set; }

    public virtual DbSet<SummarizedInfo> SummarizedInfos { get; set; }

    public virtual DbSet<SummarizedInfoTheme> SummarizedInfoThemes { get; set; }
    public virtual DbSet<SummarizedInfoTag> SummarizedInfoTags { get; set; }

    public virtual DbSet<SummarizedInfoCompetitor> SummarizedInfoCompetitors { get; set; }

    public DbSet<SummarizedInfoSignalType> SummarizedInfoSignalTypes { get; set; }

    public virtual DbSet<Job> Jobs { get; set; }

    public virtual DbSet<JobParameter> JobParameters { get; set; }

    public virtual DbSet<JobQueue> JobQueues { get; set; }

    public virtual DbSet<LaunchNotification> LaunchNotifications { get; set; }

    public virtual DbSet<List> Lists { get; set; }

    public DbSet<PulseObservationIndex> PulseObservationIndices { get; set; } = null!;
    public DbSet<PulseTopicState> PulseTopicStates { get; set; } = null!;

    public virtual DbSet<Region> Regions { get; set; }

    public DbSet<SnapshotJob> SnapshotJobs { get; set; }

    public virtual DbSet<Schema> Schemas { get; set; }

    public virtual DbSet<SemanticSignal> SemanticSignals { get; set; }
    public virtual DbSet<SignalType> SignalTypes { get; set; }

    public virtual DbSet<Server> Servers { get; set; }

    public virtual DbSet<Set> Sets { get; set; }

    public virtual DbSet<State> States { get; set; }

    public virtual DbSet<StrategicSummary> StrategicSummaries { get; set; }

    public DbSet<StrategicSignalCache> StrategicSignalCache { get; set; }

    public virtual DbSet<TrackedCompany> TrackedCompanies { get; set; }

    public DbSet<TrackedCompanyGroup> TrackedCompanyGroups { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<RawContent> RawContents { get; set; }

    public DbSet<CanonicalTheme> CanonicalThemes { get; set; }

    public DbSet<CanonicalTag> CanonicalTags { get; set; }

    public virtual DbSet<CompanyGroup> CompanyGroups { get; set; }
    // NEW: multi-select facets
    public virtual DbSet<UserPersona> UserPersonas { get; set; }                   // lookup
    public virtual DbSet<TargetSegment> TargetSegments { get; set; }               // lookup
    public virtual DbSet<CompanyUserPersona> CompanyUserPersonas { get; set; }     // join w/ payload? add DbSet
    public virtual DbSet<CompanyTargetSegment> CompanyTargetSegments { get; set; } // join w/ payload? add DbSet

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var originTypeConverter = new ValueConverter<OriginTypeEnum, string>(
            v => v.ToString(),
            v => Enum.Parse<OriginTypeEnum>(v, ignoreCase: true)
        );

        var slugListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v ?? new(), (JsonSerializerOptions)null),
            v => string.IsNullOrWhiteSpace(v)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null) ?? new List<string>());

        modelBuilder.Entity<AggregatedCounter>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("PK_HangFire_CounterAggregated");

            entity.ToTable("AggregatedCounter", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_AggregatedCounter_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.Property(e => e.BillingEmail).HasMaxLength(150);
            entity.Property(e => e.City).HasMaxLength(50);
            entity.Property(e => e.ContactEmail).HasMaxLength(150);
            entity.Property(e => e.ContactName).HasMaxLength(100);
            entity.Property(e => e.CountryCode)
                .HasMaxLength(2)
                .IsUnicode(false);
            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.RegionCode)
                .HasMaxLength(6)
                .IsUnicode(false);
            entity.Property(c => c.Status)
                .HasConversion<string>();

            entity.HasOne(d => d.CountryCodeNavigation).WithMany(p => p.Clients)
                .HasForeignKey(d => d.CountryCode)
                .HasConstraintName("FK_Clients_Countries");

            entity.HasOne(d => d.RegionCodeNavigation).WithMany(p => p.Clients)
                .HasForeignKey(d => d.RegionCode)
                .HasConstraintName("FK_Clients_Regions");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_Organizations");

            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("isActive");
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.RetryCount).HasDefaultValue(0);
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("NEW");
            entity.Property(e => e.Url)
                .HasMaxLength(250)
                .HasColumnName("URL");
            entity.HasOne(c => c.PrimaryCategory)
              .WithMany(cat => cat.Companies)
              .HasForeignKey(c => c.PrimaryCategoryId)
              .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyCategory>(entity =>
        {
            entity.ToTable("CompanyCategories");
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(120).IsRequired();
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<CompanyGroupStrategicSummaryState>(b =>
        {
            b.HasKey(x => x.CompanyGroupId);

            b.HasOne(x => x.CompanyGroup)
                .WithOne() // or .WithOne(g => g.StrategicSummaryState) if you add navigation
                .HasForeignKey<CompanyGroupStrategicSummaryState>(x => x.CompanyGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanyRelation>(e =>
        {
            e.ToTable("CompanyRelations");
            e.Property(x => x.RelatedCompanyNameRaw).HasMaxLength(200);
            e.Property(x => x.RelatedCompanyUrlRaw).HasMaxLength(500);
            e.Property(x => x.RelatedDomain).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(400);

            e.Property(x => x.Confidence).HasColumnType("decimal(4,3)");

            e.HasOne(x => x.Company)
                .WithMany(c => c.CompanyRelations)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RelatedCompany)
                .WithMany(c => c.RelatedToCompanies)
                .HasForeignKey(x => x.RelatedCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Run)
                .WithMany(r => r.Relations)
                .HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.SetNull);

            // Avoid duplicates once resolved (same edge)
            e.HasIndex(x => new { x.CompanyId, x.RelatedCompanyId, x.RelationType })
             .IsUnique()
             .HasFilter("[RelatedCompanyId] IS NOT NULL");

            // Fast lookup by domain when unresolved
            e.HasIndex(x => new { x.CompanyId, x.RelatedDomain, x.RelationType })
             .HasFilter("[RelatedCompanyId] IS NULL AND [RelatedDomain] IS NOT NULL");

            e.HasIndex(x => new { x.CompanyId, x.Status, x.RelationType, x.LastSeenAt });
        });

        modelBuilder.Entity<CompanyRelationRun>(e =>
        {
            e.ToTable("CompanyRelationRuns");
            e.Property(x => x.Query).HasMaxLength(500);
            e.Property(x => x.PromptVersion).HasMaxLength(50);
            e.Property(x => x.Error).HasMaxLength(800);

            e.HasOne(x => x.Company)
                .WithMany(c => c.CompanyRelationRuns)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.CompanyId, x.CreatedAt });
        });

        modelBuilder.Entity<Competitor>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<CompetitorName>(entity =>
        {
            entity.Property(e => e.Label).HasMaxLength(150);

            entity.HasOne(d => d.Competitor).WithMany()
                .HasForeignKey(d => d.CompetitorId)
                .HasConstraintName("FK_CompetitorNames_Competitors");
        });

        modelBuilder.Entity<Counter>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Id }).HasName("PK_HangFire_Counter");

            entity.ToTable("Counter", "HangFire");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.HasKey(e => e.Code);

            entity.Property(e => e.Code)
                .HasMaxLength(2)
                .IsUnicode(false);
            entity.Property(e => e.Area).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(50);
        });

        modelBuilder.Entity<DataSource>(entity =>
        {
            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("isActive");
            entity.Property(e => e.LastUpdate).HasColumnType("smalldatetime");
            entity.Property(e => e.Url)
                .HasMaxLength(250)
                .HasColumnName("URL");

            entity.HasOne(d => d.Company).WithMany(p => p.DataSources)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_DataSources_Organizations");

            entity.HasOne(d => d.Type).WithMany(p => p.DataSources)
                .HasForeignKey(d => d.TypeId)
                .HasConstraintName("FK_DataSources_DataSourceTypes");
        });

        modelBuilder.Entity<DataSourceType>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.UrlKeywords).HasMaxLength(100);
        });

        modelBuilder.Entity<FilteredUrl>()
            .HasIndex(f => new { f.CompanyId, f.PostUrl, f.SourceTypeId })
            .IsUnique();

        modelBuilder.Entity<FilteredUrl>()
            .HasOne(f => f.SourceType)
            .WithMany()
            .HasForeignKey(f => f.SourceTypeId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

        modelBuilder.Entity<GroupSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_GroupSnapshots");
            entity.ToTable("GroupSnapshots");

            entity.Property(e => e.GroupSlug)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Kind)
                .HasConversion(
                    v => v.ToString(),                           // enum -> string
                    v => Enum.Parse<GroupSnapshotKind>(v))       // string -> enum
                .HasMaxLength(16)
                .HasDefaultValue(GroupSnapshotKind.Pulse)
                .IsRequired();

            entity.Property(e => e.TimeWindowDays).HasDefaultValue(30);
            entity.Property(e => e.SchemaVersion).HasDefaultValue(1);

            entity.Property(e => e.GeneratedAtUtc).HasColumnType("datetime2");

            entity.Property(e => e.PayloadJson)
                .HasColumnType("nvarchar(max)")
                .IsRequired();

            // ✅ Your new “latest snapshot” query index
            entity.HasIndex(e => new { e.GroupId, e.Kind, e.TimeWindowDays, e.GeneratedAtUtc })
                .HasDatabaseName("IX_GroupSnapshots_GroupId_Kind_Window_Generated");

            // Optional: enforce one “latest” per exact timestamp isn’t needed.
            // But you may want to prevent duplicates for same (GroupId, Kind, Window, GeneratedAtUtc)
            entity.HasIndex(e => new { e.GroupId, e.Kind, e.TimeWindowDays, e.GeneratedAtUtc }).IsUnique();

            // Optional: if you often query by slug (GetGroupHeader), keep it
            entity.HasIndex(e => e.GroupSlug)
                .HasDatabaseName("IX_GroupSnapshots_GroupSlug");

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Snapshots) // rename nav if you can
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_GroupSnapshots_CompanyGroups");
        });

        modelBuilder.Entity<Hash>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Field }).HasName("PK_HangFire_Hash");

            entity.ToTable("Hash", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Hash_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Field).HasMaxLength(100);
        });

        modelBuilder.Entity<RawContent>()
        .Property(rc => rc.OriginType)
        .HasConversion<string>(); // Store enum as string

        modelBuilder.Entity<SnapshotJob>()
        .Property(j => j.OriginType)
        .HasConversion<string>();


        modelBuilder.Entity<SummarizedInfo>(entity =>
        {
            entity.Property(e => e.Date).HasColumnType("smalldatetime");

            entity.HasOne(d => d.Company).WithMany(p => p.SummarizedInfos)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Insights_Companies");

            entity.HasOne(d => d.SourceType).WithMany(p => p.SummarizedInfos)
                .HasForeignKey(d => d.SourceTypeId)
                .HasConstraintName("FK_Insights_DataSourceTypes");
        });

        modelBuilder.Entity<SummarizedInfo>()
            .Property(i => i.Sentiment)
            .HasConversion<int>();

        modelBuilder.Entity<SummarizedInfo>()
        .Property(j => j.OriginType)
        .HasConversion<string>();

        modelBuilder.Entity<SummarizedInfoCompetitor>(entity =>
        {
            entity.Property(e => e.Date).HasColumnType("smalldatetime");

            entity.HasOne(d => d.Competitor).WithMany(p => p.SummarizedInfoCompetitors)
                .HasForeignKey(d => d.CompetitorId)
                .HasConstraintName("FK_SummarizedInfoCompetitors_Competitors1");

            entity.HasOne(d => d.SummarizedInfo).WithMany(p => p.SummarizedInfoCompetitors)
                .HasForeignKey(d => d.SummarizedInfoId)
                .HasConstraintName("FK_SummarizedInfoCompetitors_Insights");
        });

        modelBuilder.Entity<SummarizedInfoTag>()
            .HasOne(t => t.CanonicalTag)
            .WithMany()
            .HasForeignKey(t => t.CanonicalTagId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SummarizedInfoTheme>()
            .HasOne(t => t.CanonicalTheme)
            .WithMany()
            .HasForeignKey(t => t.CanonicalThemeId)
            .OnDelete(DeleteBehavior.SetNull);


        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_HangFire_Job");

            entity.ToTable("Job", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Job_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.HasIndex(e => e.StateName, "IX_HangFire_Job_StateName").HasFilter("([StateName] IS NOT NULL)");

            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
            entity.Property(e => e.StateName).HasMaxLength(20);
        });

        modelBuilder.Entity<JobParameter>(entity =>
        {
            entity.HasKey(e => new { e.JobId, e.Name }).HasName("PK_HangFire_JobParameter");

            entity.ToTable("JobParameter", "HangFire");

            entity.Property(e => e.Name).HasMaxLength(40);

            entity.HasOne(d => d.Job).WithMany(p => p.JobParameters)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_HangFire_JobParameter_Job");
        });

        modelBuilder.Entity<JobQueue>(entity =>
        {
            entity.HasKey(e => new { e.Queue, e.Id }).HasName("PK_HangFire_JobQueue");

            entity.ToTable("JobQueue", "HangFire");

            entity.Property(e => e.Queue).HasMaxLength(50);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.FetchedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<LaunchNotification>(entity =>
        {
            entity.HasKey(e => new { e.Email, e.Date });

            entity.Property(e => e.Email)
                .HasMaxLength(150)
                .HasColumnName("email");
            entity.Property(e => e.Date)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime")
                .HasColumnName("date");
        });

        modelBuilder.Entity<List>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Id }).HasName("PK_HangFire_List");

            entity.ToTable("List", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_List_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        // In OnModelCreating
        modelBuilder.Entity<PulseObservationIndex>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(24).IsRequired();
            e.Property(x => x.TopicKey).HasMaxLength(64).IsRequired();
            e.Property(x => x.DateBucket).HasColumnType("date");

            // FK -> Company
            e.HasOne(x => x.Company)
             .WithMany() // or .WithMany(c => c.PulseObservationIndices) if you add a collection
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);

            // Unique “one row per day per topic per company”
            e.HasIndex(x => new { x.CompanyId, x.Type, x.TopicKey, x.DateBucket }).IsUnique();

            // Helpful for surge queries that filter by since-time
            e.HasIndex(x => new { x.CompanyId, x.Type, x.TopicKey, x.FirstSeenAt });

            // Optional: 14-day rollups by day
            e.HasIndex(x => new { x.CompanyId, x.DateBucket });
        });

        modelBuilder.Entity<PulseTopicState>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(24).IsRequired();
            e.Property(x => x.TopicKey).HasMaxLength(64).IsRequired();

            // FK -> Company
            e.HasOne(x => x.Company)
             .WithMany()
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);

            // Lookups by topic
            e.HasIndex(x => new { x.CompanyId, x.Type, x.TopicKey }).IsUnique();
        });

        modelBuilder.Entity<RawContent>(b =>
        {
            b.Property(x => x.OriginType)
                .HasConversion(originTypeConverter)
                .HasMaxLength(32);
        });

        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasKey(e => e.Code);

            entity.Property(e => e.Code)
                .HasMaxLength(6)
                .IsUnicode(false);
            entity.Property(e => e.CountryCode)
                .HasMaxLength(2)
                .IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasOne(d => d.CountryCodeNavigation).WithMany(p => p.Regions)
                .HasForeignKey(d => d.CountryCode)
                .HasConstraintName("FK_Regions_Countries");
        });

        modelBuilder.Entity<SnapshotJob>()
            .HasIndex(s => s.SnapshotId)
            .IsUnique();

        modelBuilder.Entity<SnapshotJob>()
            .HasOne(s => s.Company)
            .WithMany()
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SnapshotJob>()
            .HasOne(s => s.DataSourceType)
            .WithMany()
            .HasForeignKey(s => s.DataSourceTypeId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Schema>(entity =>
        {
            entity.HasKey(e => e.Version).HasName("PK_HangFire_Schema");

            entity.ToTable("Schema", "HangFire");

            entity.Property(e => e.Version).ValueGeneratedNever();
        });

        modelBuilder.Entity<SemanticSignal>(b =>
        {
            b.HasIndex(x => x.Hash).IsUnique();
            b.Property(p => p.IntentsJson).HasColumnType("nvarchar(max)");
            b.Property(p => p.KeywordsJson).HasColumnType("nvarchar(max)");
            b.Property(p => p.Embedding).HasColumnType("varbinary(max)");
            b.Property(p => p.ModelScore).HasPrecision(18, 6);

            // FK to Companies, but do NOT cascade delete (protect history)
            b.HasOne(s => s.Company)
             .WithMany()                    // or .WithMany(c => c.SemanticSignals) if you add the collection on Company
             .HasForeignKey(s => s.CompanyId)
             .OnDelete(DeleteBehavior.Restrict);   // or .NoAction in EF Core 7+

            // Helpful composite index for queries
            b.HasIndex(s => new { s.CompanyId, s.SeenAt });
        });

        modelBuilder.Entity<Server>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_HangFire_Server");

            entity.ToTable("Server", "HangFire");

            entity.HasIndex(e => e.LastHeartbeat, "IX_HangFire_Server_LastHeartbeat");

            entity.Property(e => e.Id).HasMaxLength(200);
            entity.Property(e => e.LastHeartbeat).HasColumnType("datetime");
        });

        modelBuilder.Entity<Set>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Value }).HasName("PK_HangFire_Set");

            entity.ToTable("Set", "HangFire");

            entity.HasIndex(e => e.ExpireAt, "IX_HangFire_Set_ExpireAt").HasFilter("([ExpireAt] IS NOT NULL)");

            entity.HasIndex(e => new { e.Key, e.Score }, "IX_HangFire_Set_Score");

            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Value).HasMaxLength(256);
            entity.Property(e => e.ExpireAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<State>(entity =>
        {
            entity.HasKey(e => new { e.JobId, e.Id }).HasName("PK_HangFire_State");

            entity.ToTable("State", "HangFire");

            entity.HasIndex(e => e.CreatedAt, "IX_HangFire_State_CreatedAt");

            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
            entity.Property(e => e.Name).HasMaxLength(20);
            entity.Property(e => e.Reason).HasMaxLength(100);

            entity.HasOne(d => d.Job).WithMany(p => p.States)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK_HangFire_State_Job");
        });

        modelBuilder.Entity<StrategicSummary>()
            .HasIndex(x => new { x.CompanyGroupId, x.PeriodType, x.SourceKey })
            .IsUnique()
            .HasFilter("[SourceKey] IS NOT NULL AND [SourceKey] <> ''");

        modelBuilder.Entity<StrategicSummary>(b =>
        {
            b.Property(x => x.PeriodType).HasMaxLength(32);
            b.Property(x => x.SourceKey).HasMaxLength(256);
        });

        modelBuilder.Entity<StrategicSummary>()
            .Property(x => x.IncludedSignalTypes)  
            .HasConversion(slugListConverter);

        modelBuilder.Entity<SummarizedInfo>(e =>
        {
            e.Property(x => x.OriginType)
            .HasConversion(originTypeConverter)
            .HasMaxLength(32);
        });

        modelBuilder.Entity<SummarizedInfoSignalType>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(512); // optional, but recommended (was nvarchar(max))
        });


        modelBuilder.Entity<TrackedCompany>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_FeedbackLoops");

            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime");
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Notes).HasMaxLength(1000);

            entity.HasOne(d => d.Client).WithMany(p => p.TrackedCompanies)
                .HasForeignKey(d => d.ClientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FeedbackLoops_Clients");

            entity.HasOne(d => d.Company).WithMany(p => p.TrackedCompanies)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_FeedbackLoops_Organizations");
        });

        modelBuilder.Entity<TrackedCompanyGroup>()
        .HasKey(tc => new { tc.TrackedCompanyId, tc.CompanyGroupId });

        modelBuilder.Entity<TrackedCompanyGroup>()
            .HasOne(tc => tc.TrackedCompany)
            .WithMany(c => c.TrackedCompanyGroups)
            .HasForeignKey(tc => tc.TrackedCompanyId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<TrackedCompanyGroup>()
            .HasOne(tc => tc.CompanyGroup)
            .WithMany(g => g.TrackedCompanyGroups)
            .HasForeignKey(tc => tc.CompanyGroupId);

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.DateCreated)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("smalldatetime");
            entity.Property(e => e.Email).HasMaxLength(150);
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("isActive");
            entity.Property(e => e.LastVisit).HasColumnType("smalldatetime");
            entity.Property(e => e.Name).HasMaxLength(100);

            entity.HasOne(d => d.Client).WithMany(p => p.Users)
                .HasForeignKey(d => d.ClientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Users_Clients");

            entity.HasIndex(u => u.ClerkUserId)
                .IsUnique();
        });

        modelBuilder.Entity<RawContent>(entity =>
        {
            entity.HasOne(d => d.Company).WithMany(p => p.UserFeedbacks)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK_UserFeedbacks_Companies");

            entity.HasOne(d => d.DataSourceType).WithMany(p => p.UserFeedbacks)
                .HasForeignKey(d => d.DataSourceTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserFeedbacks_DataSourceTypes");
        });

        // update Sepember 9, 2025 - User personas and Target Segments
        // CompanyTargetSegment
        modelBuilder.Entity<CompanyTargetSegment>(b =>
        {
            b.HasKey(x => new { x.CompanyId, x.TargetSegmentId });

            b.HasOne(x => x.Company)
             .WithMany(c => c.CompanyTargetSegments) // add this collection to Company
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);      // deleting a Company deletes its join rows

            b.HasOne(x => x.TargetSegment)
             .WithMany(ts => ts.CompanyTargetSegments) // add this collection to TargetSegment
             .HasForeignKey(x => x.TargetSegmentId)
             .OnDelete(DeleteBehavior.Restrict);     // or .NoAction
        });

        // CompanyUserPersona
        modelBuilder.Entity<CompanyUserPersona>(b =>
        {
            b.HasKey(x => new { x.CompanyId, x.UserPersonaId });

            b.HasOne(x => x.Company)
             .WithMany(c => c.CompanyUserPersonas)   // add this collection to Company
             .HasForeignKey(x => x.CompanyId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.UserPersona)
             .WithMany(up => up.CompanyUserPersonas) // add this collection to UserPersona
             .HasForeignKey(x => x.UserPersonaId)
             .OnDelete(DeleteBehavior.Restrict);     // or .NoAction
        });

        // Lookups: unique names
        modelBuilder.Entity<TargetSegment>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<UserPersona>().HasIndex(x => x.Name).IsUnique();

        // Company string fields (since you’re not using a lookup)
        modelBuilder.Entity<Company>(b =>
        {
            b.Property(x => x.CategoryReason).HasMaxLength(400);
            b.Property(x => x.CategoryConfidence).HasPrecision(4, 3); // e.g., 0.000–0.999
        });

        // Seed (fine to keep here or do a migration seeder)
        modelBuilder.Entity<TargetSegment>().HasData(
            new TargetSegment { Id = 1, Name = "SMB" },
            new TargetSegment { Id = 2, Name = "MidMarket" },
            new TargetSegment { Id = 3, Name = "Enterprise" },
            new TargetSegment { Id = 4, Name = "Agencies" }
        );
        modelBuilder.Entity<UserPersona>().HasData(
            new UserPersona { Id = 1, Name = "Marketing" },
            new UserPersona { Id = 2, Name = "Support" },
            new UserPersona { Id = 3, Name = "Product" },
            new UserPersona { Id = 4, Name = "Sales" },
            new UserPersona { Id = 5, Name = "DataAnalytics" },
            new UserPersona { Id = 6, Name = "Engineering" }
        );


        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
