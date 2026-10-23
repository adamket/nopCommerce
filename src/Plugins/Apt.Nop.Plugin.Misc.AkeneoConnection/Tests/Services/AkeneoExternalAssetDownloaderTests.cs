using System.Net;
using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Tests.Services;

[TestFixture]
public class AkeneoExternalAssetDownloaderTests
{
    [TestCase("10.1.2.3")]
    [TestCase("127.0.0.1")]
    [TestCase("100.64.0.1")]
    [TestCase("169.254.169.254")]
    [TestCase("172.16.0.1")]
    [TestCase("192.168.1.1")]
    [TestCase("0.0.0.0")]
    [TestCase("224.0.0.1")]
    [TestCase("::1")]
    [TestCase("::")]
    [TestCase("fe80::1")]
    [TestCase("fec0::1")]
    [TestCase("fc00::1")]
    [TestCase("fd12:3456::1")]
    [TestCase("ff02::1")]
    [TestCase("::ffff:10.0.0.1")]
    [TestCase("::ffff:169.254.169.254")]
    [TestCase("64:ff9b::a00:1")]
    public void Private_and_local_addresses_are_blocked(string address)
    {
        Assert.That(AkeneoExternalAssetDownloader.IsPrivateOrLocal(IPAddress.Parse(address)), Is.True);
    }

    [TestCase("8.8.8.8")]
    [TestCase("100.128.0.1")]
    [TestCase("172.32.0.1")]
    [TestCase("2606:4700:4700::1111")]
    [TestCase("::ffff:8.8.8.8")]
    [TestCase("64:ff9b::808:808")]
    public void Public_addresses_are_allowed(string address)
    {
        Assert.That(AkeneoExternalAssetDownloader.IsPrivateOrLocal(IPAddress.Parse(address)), Is.False);
    }
}
