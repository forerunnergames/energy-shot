using com.forerunnergames.energyshot.utilities;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public class ToolsTest
{
  [TestCase]
  public void ValidPlayerNamesAreAccepted()
  {
    AssertBool (Tools.IsValidPlayerName ("Aaron")).IsTrue();
    AssertBool (Tools.IsValidPlayerName ("a")).IsTrue();
    AssertBool (Tools.IsValidPlayerName ("Player123")).IsTrue();
    AssertBool (Tools.IsValidPlayerName ("1234567890123456")).IsTrue(); // 16 chars (max)
  }

  [TestCase]
  public void InvalidPlayerNamesAreRejected()
  {
    AssertBool (Tools.IsValidPlayerName (string.Empty)).IsFalse();
    AssertBool (Tools.IsValidPlayerName ("12345678901234567")).IsFalse(); // 17 chars (over max)
    AssertBool (Tools.IsValidPlayerName ("has space")).IsFalse();
    AssertBool (Tools.IsValidPlayerName ("emoji😀")).IsFalse();
    AssertBool (Tools.IsValidPlayerName ("under_score")).IsFalse();
  }

  [TestCase]
  public void ValidServerAddressesAreAccepted()
  {
    AssertBool (Tools.IsValidServerAddress ("192.168.1.1")).IsTrue();
    AssertBool (Tools.IsValidServerAddress ("255.255.255.255")).IsTrue();
    AssertBool (Tools.IsValidServerAddress ("::1")).IsTrue();
    AssertBool (Tools.IsValidServerAddress ("2001:db8::ff00:42:8329")).IsTrue();
    AssertBool (Tools.IsValidServerAddress ("example.com")).IsTrue();
    AssertBool (Tools.IsValidServerAddress ("sub.domain.example.com")).IsTrue();
  }

  [TestCase]
  public void InvalidServerAddressesAreRejected()
  {
    AssertBool (Tools.IsValidServerAddress (string.Empty)).IsFalse();
    AssertBool (Tools.IsValidServerAddress ("256.1.1.1")).IsFalse();
    AssertBool (Tools.IsValidServerAddress ("1.2.3")).IsFalse();
    AssertBool (Tools.IsValidServerAddress ("not valid")).IsFalse();
    AssertBool (Tools.IsValidServerAddress ("-bad.com")).IsFalse();
  }
}
