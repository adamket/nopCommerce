using System.Linq.Expressions;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Domain;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Nop.Core.Caching;
using Nop.Data;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoSyncLeaseServiceTests
{
    private const string Key = "akeneo-catalog-writer";
    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    private List<AkeneoSyncLease> _rows = null!;
    private Func<IQueryable<AkeneoSyncLease>>? _tableOverride;
    private int _nextId;
    private AkeneoSyncLeaseService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _rows = [];
        _tableOverride = null;
        _nextId = 1;

        var repository = new Mock<IRepository<AkeneoSyncLease>>();
        repository.SetupGet(r => r.Table)
            .Returns(() => _tableOverride?.Invoke() ?? _rows.ToList().AsQueryable());
        repository
            .Setup(r => r.InsertAsync(It.IsAny<AkeneoSyncLease>(), It.IsAny<bool>()))
            .Returns((AkeneoSyncLease lease, bool _) =>
            {
                // Mirrors the unique index on LockKey.
                if (_rows.Any(row => row.LockKey == lease.LockKey))
                    throw new InvalidOperationException("Unique index violation.");

                lease.Id = _nextId++;
                _rows.Add(lease);
                return Task.CompletedTask;
            });
        repository
            .Setup(r => r.UpdateAsync(It.IsAny<AkeneoSyncLease>(), It.IsAny<bool>()))
            .Returns((AkeneoSyncLease lease, bool _) =>
            {
                // Like SQL, updating a deleted row changes nothing and raises nothing.
                var index = _rows.FindIndex(row => row.Id == lease.Id);
                if (index >= 0)
                    _rows[index] = Copy(lease);
                return Task.CompletedTask;
            });
        repository
            .Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<AkeneoSyncLease, bool>>>()))
            .ReturnsAsync((Expression<Func<AkeneoSyncLease, bool>> predicate) =>
                _rows.RemoveAll(new Predicate<AkeneoSyncLease>(predicate.Compile())));
        repository
            .Setup(r => r.DeleteAsync(It.IsAny<AkeneoSyncLease>(), It.IsAny<bool>()))
            .Returns((AkeneoSyncLease lease, bool _) =>
            {
                _rows.RemoveAll(row => row.Id == lease.Id);
                return Task.CompletedTask;
            });
        repository
            .Setup(r => r.GetByIdAsync(
                It.IsAny<int?>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()))
            .ReturnsAsync((int? id, Func<ICacheKeyService, CacheKey> _, bool _, bool _) =>
                _rows.Where(row => row.Id == id).Select(Copy).FirstOrDefault());

        _service = new AkeneoSyncLeaseService(repository.Object, Mock.Of<ILogger>());
    }

    [Test]
    public async Task A_second_acquire_fails_while_the_lease_is_held()
    {
        var first = await _service.TryAcquireAsync(Key, Duration);
        var second = await _service.TryAcquireAsync(Key, Duration);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Null);
        Assert.That(_rows, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task An_expired_lease_is_taken_over()
    {
        var first = (await _service.TryAcquireAsync(Key, Duration))!;
        Expire(first.Id);

        var second = await _service.TryAcquireAsync(Key, Duration);

        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Id, Is.Not.EqualTo(first.Id));
        Assert.That(_rows.Single().Id, Is.EqualTo(second.Id));
    }

    [Test]
    public async Task A_takeover_does_not_delete_a_lease_renewed_after_it_was_read()
    {
        var holder = (await _service.TryAcquireAsync(Key, Duration))!;

        // The acquirer reads the lease while it looks expired; the holder renews
        // it before the acquirer's delete runs.
        var staleCopy = Copy(holder);
        staleCopy.ExpiresOnUtc = DateTime.UtcNow.AddMinutes(-1);
        _tableOverride = () => new[] { staleCopy }.AsQueryable();

        var acquired = await _service.TryAcquireAsync(Key, Duration);

        Assert.That(acquired, Is.Null);
        Assert.That(_rows.Single().Id, Is.EqualTo(holder.Id));
    }

    [Test]
    public async Task Renewing_a_lease_that_was_taken_over_reports_it_lost()
    {
        var first = (await _service.TryAcquireAsync(Key, Duration))!;
        Expire(first.Id);
        var second = (await _service.TryAcquireAsync(Key, Duration))!;
        var secondExpiry = _rows.Single().ExpiresOnUtc;

        var stillHeld = await _service.RenewAsync(first, Duration);

        Assert.That(stillHeld, Is.False);
        Assert.That(_rows.Single().Id, Is.EqualTo(second.Id));
        Assert.That(_rows.Single().ExpiresOnUtc, Is.EqualTo(secondExpiry));
        Assert.That(await _service.RenewByIdAsync(first.Id, Duration), Is.False);
    }

    [Test]
    public async Task Renewing_a_held_lease_extends_it()
    {
        var lease = (await _service.TryAcquireAsync(Key, TimeSpan.FromMinutes(1)))!;

        Assert.That(await _service.RenewByIdAsync(lease.Id, Duration), Is.True);
        Assert.That(_rows.Single().ExpiresOnUtc, Is.GreaterThan(DateTime.UtcNow.AddMinutes(50)));
    }

    [Test]
    public async Task RenewIfDue_throws_when_the_lease_was_lost_and_skips_when_not_due()
    {
        var lease = (await _service.TryAcquireAsync(Key, Duration))!;
        _rows.Clear();

        // Checked in just now, so nothing is due and nothing is checked.
        Assert.DoesNotThrowAsync(() => _service.RenewIfDueAsync(lease, Duration));

        Assert.ThrowsAsync<AkeneoSyncLeaseLostException>(() =>
            _service.RenewIfDueAsync(lease, Duration, force: true));

        lease.HeartbeatOnUtc = DateTime.UtcNow - AkeneoSyncLeaseService.RenewalInterval - TimeSpan.FromSeconds(1);
        Assert.ThrowsAsync<AkeneoSyncLeaseLostException>(() =>
            _service.RenewIfDueAsync(lease, Duration));
    }

    [Test]
    public async Task Force_release_leaves_a_lease_whose_holder_is_still_checking_in()
    {
        await _service.TryAcquireAsync(Key, Duration);

        var released = await _service.ForceReleaseAsync(Key, TimeSpan.FromMinutes(30));

        Assert.That(released, Is.Null);
        Assert.That(_rows, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Force_release_removes_a_stale_lease()
    {
        var lease = (await _service.TryAcquireAsync(Key, Duration))!;
        _rows.Single().HeartbeatOnUtc = DateTime.UtcNow.AddMinutes(-31);

        var released = await _service.ForceReleaseAsync(Key, TimeSpan.FromMinutes(30));

        Assert.That(released?.Id, Is.EqualTo(lease.Id));
        Assert.That(_rows, Is.Empty);
    }

    private void Expire(int id) =>
        _rows.Single(row => row.Id == id).ExpiresOnUtc = DateTime.UtcNow.AddMinutes(-1);

    private static AkeneoSyncLease Copy(AkeneoSyncLease lease) => new()
    {
        Id = lease.Id,
        LockKey = lease.LockKey,
        SyncRunRecordId = lease.SyncRunRecordId,
        AcquiredOnUtc = lease.AcquiredOnUtc,
        HeartbeatOnUtc = lease.HeartbeatOnUtc,
        ExpiresOnUtc = lease.ExpiresOnUtc
    };
}
