using System.Transactions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Domain.Catalog;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

/// <summary>
/// A product's write unit (a variant, its parent and every picture) can take
/// longer than .NET's 60-second transaction default, and a unit that rolls back
/// must not leave the parents it created in the run's parent cache.
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

    [Test]
    public void A_failed_unit_leaves_the_parent_cache_as_it_found_it()
    {
        var kept = new Product { Id = 337 };
        var replaced = new Product { Id = 404 };
        var cache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
        {
            ["nursery_red_maple"] = kept,
            ["nursery_blue_spruce"] = replaced,
            ["nursery_unavailable"] = null!
        };
        var before = new Dictionary<string, Product>(cache, StringComparer.OrdinalIgnoreCase);

        // The unit creates the Spicebush parent, replaces two entries, then rolls back.
        cache["nursery_spicebush"] = new Product { Id = 2131 };
        cache["nursery_blue_spruce"] = new Product { Id = 9999 };
        cache["nursery_unavailable"] = new Product { Id = 8888 };

        AkeneoProductBatchSyncService.RestoreParentCache(cache, before);

        Assert.Multiple(() =>
        {
            Assert.That(cache.ContainsKey("nursery_spicebush"), Is.False);
            Assert.That(cache["nursery_red_maple"], Is.SameAs(kept));
            Assert.That(cache["nursery_blue_spruce"], Is.SameAs(replaced));
            Assert.That(cache["nursery_unavailable"], Is.Null);
        });
    }
}
