using CVPN.Models;
using CVPN.ViewModels;

namespace CVPN.Tests;

public class ProfilesSearchTests
{
    private static ServerProfile Make(string name, string host) => new() { Name = name, Host = host, Port = 443 };

    [Theory]
    [InlineData("")]
    [InlineData("frank")]
    [InlineData("DE.EXAMPLE")]
    [InlineData(":443")]
    public void Поиск_находит_по_имени_и_адресу(string query)
    {
        Assert.True(ProfilesViewModel.Matches(Make("Germany-Frankfurt", "de.example.net"), query));
    }

    [Fact]
    public void Поиск_отсеивает_чужие_серверы()
    {
        Assert.False(ProfilesViewModel.Matches(Make("Finland-Helsinki", "fi.example.net"), "amsterdam"));
    }
}
