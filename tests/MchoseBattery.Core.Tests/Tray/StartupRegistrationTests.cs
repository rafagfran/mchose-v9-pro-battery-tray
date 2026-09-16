using MchoseBattery.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MchoseBattery.Core.Tests.Tray;

[TestClass]
public sealed class StartupRegistrationTests
{
    [TestMethod]
    public void StartupCommand_QuotesExecutablePath()
    {
        Assert.AreEqual("\"C:\\Program Files\\MchoseBattery\\MchoseBattery.exe\"",
            StartupRegistration.BuildCommand("C:\\Program Files\\MchoseBattery\\MchoseBattery.exe"));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("MchoseBattery.exe")]
    [DataRow("C:\\Battery\\MchoseBattery.exe\" --unexpected")]
    [DataRow("C:\\Battery\\MchoseBattery.exe\r\n")]
    public void StartupCommand_RejectsInvalidExecutablePath(string path)
    {
        Assert.ThrowsException<ArgumentException>(() => StartupRegistration.BuildCommand(path));
    }
}
