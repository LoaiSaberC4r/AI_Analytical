using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CameraAnalytics.Infrastructure.Persistence;

internal static class WriteTransaction
{
    public static Task<IDbContextTransaction> Begin(AnalyticsDb db, CancellationToken ct) =>
        Acquire(db, "Exclusive", ct);

    public static Task<IDbContextTransaction> Read(AnalyticsDb db, CancellationToken ct) =>
        Acquire(db, "Shared", ct);

    private static async Task<IDbContextTransaction> Acquire(AnalyticsDb db, string mode, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'CameraAnalytics.Writer', @LockMode = {mode}, @LockOwner = N'Transaction';
                IF @result < 0 THROW 50001, 'Could not acquire the analytics state lock.', 1;
                """, ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
