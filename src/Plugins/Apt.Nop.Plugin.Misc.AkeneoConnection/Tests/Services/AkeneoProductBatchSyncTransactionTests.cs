using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A product's write unit (a variant, its parent and every picture) can take
/// longer than .NET's 60-second transaction default.
/// </summary>
[TestFixture]
public class AkeneoProductBatchSyncTransactionTests
{
    [Test]
    public void The_unit_transaction_outlasts_the_default_without_being_capped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AkeneoProductBatchSyncService.UnitTransactionTimeout, Is.GreaterThan(TransactionManager.DefaultTimeout));
            Assert.That(AkeneoProductBatchSyncService.UnitTransactionTimeout, Is.LessThanOrEqualTo(TransactionManager.MaximumTimeout));
        });
    }
}
