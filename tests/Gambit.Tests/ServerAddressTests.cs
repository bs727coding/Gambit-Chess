using Gambit.Online.Client;

namespace Gambit.Tests;

public class ServerAddressTests
{
    [Theory]
    [InlineData("http://81.2.69.160:5080", true)]          // a public address
    [InlineData("http://chess.example.com", true)]
    [InlineData("http://172.32.0.1:5080", true)]           // just outside 172.16.0.0/12
    [InlineData("https://chess.example.com", false)]       // encrypted
    [InlineData("http://localhost:5080", false)]
    [InlineData("http://127.0.0.1:5080", false)]
    [InlineData("http://[::1]:5080", false)]
    [InlineData("http://192.168.1.20:5080", false)]        // home network
    [InlineData("http://10.0.0.5:5080", false)]
    [InlineData("http://172.20.1.1:5080", false)]
    [InlineData("http://100.101.102.103:5080", false)]     // Tailscale
    [InlineData("http://my-pc.tail1234.ts.net:5080", false)]
    [InlineData("http://my-pc:5080", false)]               // a computer name on the network
    [InlineData("http://my-pc.local:5080", false)]
    [InlineData("not an address", false)]
    public void Warns_only_for_unencrypted_addresses_on_the_internet(string url, bool warn) =>
        Assert.Equal(warn, ServerAddress.IsUnencryptedOverInternet(url));
}
