using DungAT.WorkflowEngine.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

public static class WorkflowModelBuilderExtensions
{
    /// <summary>Call from <c>OnModelCreating</c> to add the workflow tables to your DbContext.</summary>
    /// <param name="modelBuilder">The model builder of your DbContext.</param>
    /// <param name="schema">Optional database schema for the workflow tables.</param>
    /// <param name="storeDatesAsBinary">
    /// Stores <see cref="DateTimeOffset"/> columns as sortable integers. Required on SQLite, which cannot
    /// ORDER BY or compare <see cref="DateTimeOffset"/>, for the monitoring queries (dashboard).
    /// </param>
    public static ModelBuilder ApplyWorkflowEngineConfiguration(
        this ModelBuilder modelBuilder, string? schema = null, bool storeDatesAsBinary = false)
    {
        var dates = storeDatesAsBinary ? new DateTimeOffsetToBinaryConverter() : null;

        modelBuilder.Entity<StoredWorkflowDefinition>(b =>
        {
            b.ToTable("WorkflowDefinitions", schema);
            b.HasKey(x => new { x.Code, x.Version });
            b.Property(x => x.Code).HasMaxLength(100);
        });

        modelBuilder.Entity<WorkflowInstance>(b =>
        {
            b.ToTable("WorkflowInstances", schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.DefinitionCode).HasMaxLength(100);
            b.Property(x => x.EntityType).HasMaxLength(200);
            b.Property(x => x.EntityId).HasMaxLength(200);
            b.Property(x => x.CurrentStateCode).HasMaxLength(100);
            b.Property(x => x.CreatedBy).HasMaxLength(200);
            b.Property(x => x.RowVersion).IsConcurrencyToken();
            if (dates is not null)
            {
                b.Property(x => x.CreatedAt).HasConversion(dates);
                b.Property(x => x.UpdatedAt).HasConversion(dates);
            }
            b.HasIndex(x => new { x.EntityType, x.EntityId }).IsUnique();
        });

        modelBuilder.Entity<WorkflowHistoryEntry>(b =>
        {
            b.ToTable("WorkflowHistory", schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.FromStateCode).HasMaxLength(100);
            b.Property(x => x.CommandCode).HasMaxLength(100);
            b.Property(x => x.ToStateCode).HasMaxLength(100);
            b.Property(x => x.ExecutedBy).HasMaxLength(200);
            if (dates is not null) b.Property(x => x.ExecutedAt).HasConversion(dates);
            b.HasIndex(x => x.InstanceId);
        });

        modelBuilder.Entity<WorkflowPostActionRun>(b =>
        {
            b.ToTable("WorkflowPostActionRuns", schema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ActionName).HasMaxLength(200);
            b.Property(x => x.Error).HasMaxLength(2000);
            if (dates is not null) b.Property(x => x.StartedAt).HasConversion(dates);
            b.HasIndex(x => new { x.InstanceId, x.HistoryId });
        });

        return modelBuilder;
    }
}
