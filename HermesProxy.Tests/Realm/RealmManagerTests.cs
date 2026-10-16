using Framework.Constants;
using Framework.Realm;
using HermesProxy.Configuration.Options;
using Xunit;

// Not HermesProxy.Tests.Realm: a namespace of that name would hide the Realm class from every
// other test in this assembly.
namespace HermesProxy.Tests.Realms;

public sealed class RealmManagerTests
{
    /// <summary>
    /// The auth response tells the client its realm by two names, and the client builds a
    /// player's full name from the normalized one. A realm added without it left that name empty,
    /// and every full name came out as "Name-": the hyphen with nothing behind it that issue #335
    /// reported on a whisper.
    /// </summary>
    [Theory]
    [InlineData("AzerothCore", "AzerothCore")]
    [InlineData("Trinity 4.3.4", "Trinity4.3.4")]
    public void AddRealm_SetsTheNormalizedName(string name, string normalized)
    {
        var realms = new RealmManager(new ClientOptions(), new ProxyNetworkOptions());

        realms.AddRealm(1, name, "127.0.0.1", 8085, RealmType.Normal, RealmFlags.None, 0, 1, 0f);

        Realm? realm = realms.GetRealm(new RealmId(1, 1, 1));
        Assert.NotNull(realm);
        Assert.Equal(name, realm.Name);
        Assert.Equal(normalized, realm.NormalizedName);
    }
}
