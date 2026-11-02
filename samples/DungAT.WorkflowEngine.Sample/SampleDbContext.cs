using DungAT.WorkflowEngine.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DungAT.WorkflowEngine.Sample;

public class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
{
    public DbSet<Report> Reports => Set<Report>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Your own tables are mapped as usual; the workflow tables come from one call.
        modelBuilder.Entity<Report>().Property(r => r.Status).HasConversion<string>();
        modelBuilder.ApplyWorkflowEngineConfiguration(storeDatesAsBinary: true); // true: SQLite cannot sort DateTimeOffset
    }
}
