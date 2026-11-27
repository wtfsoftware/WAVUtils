using Microsoft.VisualStudio.TestTools.UnitTesting;
using WAVUtils.Core;

namespace WAVUtils.Tests;

[TestClass]
public sealed class LtcPolarityTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(-1)]
    public void Finding43DecoderStartReferenceIsPolarityInvariant(int polarity)
    {
        var tc = LtcDecoder.Decode(LtcRegressionTests.Signal().Select(sample => sample * polarity).ToArray(), sampleRate: 48000);
        Assert.IsNotNull(tc);
        long actual = LtcDecoder.ToSampleReference(tc.Value, sampleRate: 48000);
        Assert.IsTrue(Math.Abs(actual - LtcRegressionTests.Reference) <= 2, $"Decoded {tc}; reference {actual}.");
    }
}
