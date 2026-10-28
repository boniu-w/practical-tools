using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace PracticalTools.Sqlite;

/// <summary>Converters capture a protector; never reuse another provider/data directory's converter.</summary>
public sealed class SecretProtectionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is MyDbContext database
        ? (context.GetType(), database.SecretProtector, designTime)
        : (object)(context.GetType(), designTime);
}
