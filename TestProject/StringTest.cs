using PracticalTools.Constant;
using PracticalTools.util;

namespace TestProject1;

[TestClass]
public class StringTest
{
    [TestMethod]
    public void Stest0()
    {
        string passphrase = "Soccer0boy$";
        string a = StringUtil.Xor(passphrase, CommonConst.Salt);
        Console.WriteLine(a);

        string s = StringUtil.XorFromBase64(a, CommonConst.Salt);
        Console.WriteLine(s);
    }
}