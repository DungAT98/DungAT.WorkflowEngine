using System.ComponentModel;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

/// <summary>Loads and identifies host entities through their EF Core primary key.</summary>
internal static class EfEntityKeys
{
    public static async Task<TEntity?> FindAsync<TEntity, TContext>(IServiceProvider services, string id, CancellationToken ct)
        where TEntity : class where TContext : DbContext
    {
        var db = services.GetRequiredService<TContext>();
        if (!TryConvert(KeyOf<TEntity>(db).ClrType, id, out var value)) return null;
        // Tracked on purpose: the engine writes the state column on this instance and it is saved together with the workflow rows.
        return await db.Set<TEntity>().FindAsync([value], ct);
    }

    public static string GetId<TEntity, TContext>(IServiceProvider services, TEntity entity)
        where TEntity : class where TContext : DbContext
    {
        var db = services.GetRequiredService<TContext>();
        var value = db.Entry(entity).Property(KeyOf<TEntity>(db).Name).CurrentValue;
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }

    private static IProperty KeyOf<TEntity>(DbContext db)
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not part of {db.GetType().Name}'s model.");
        var key = entityType.FindPrimaryKey()
                  ?? throw new InvalidOperationException($"{typeof(TEntity).Name} has no primary key. Specify .Load(...) and .Id(...).");
        return key.Properties.Count == 1
            ? key.Properties[0]
            : throw new InvalidOperationException($"{typeof(TEntity).Name} has a composite key. Specify .Load(...) and .Id(...).");
    }

    private static bool TryConvert(Type keyType, string id, out object? value)
    {
        var type = Nullable.GetUnderlyingType(keyType) ?? keyType;
        try
        {
            value = type == typeof(string) ? id : TypeDescriptor.GetConverter(type).ConvertFromInvariantString(id);
            return value is not null;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or OverflowException or ArgumentException)
        {
            value = null; // an id that cannot be a key of this type simply does not match any row
            return false;
        }
    }
}
