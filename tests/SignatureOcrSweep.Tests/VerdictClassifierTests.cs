using Xunit;

namespace SignatureOcrSweep.Tests;

public class VerdictClassifierTests
{
    [Theory]
    [InlineData("2,000", Verdict.Correct)]
    [InlineData("2.000", Verdict.Correct)] // the period-grouped fold from plugins #76
    [InlineData("2000", Verdict.Correct)]
    [InlineData("3,100", Verdict.WrongNumber)]
    [InlineData("2", Verdict.WrongNumber)]
    [InlineData("tooo", Verdict.Unreadable)]
    [InlineData("", Verdict.Blank)]
    [InlineData("  \n", Verdict.Blank)]
    public void Reads_are_judged_with_the_plugins_own_parser(string raw, Verdict expected)
    {
        Assert.Equal(expected, VerdictClassifier.Classify(raw, 2000, out _));
    }

    [Fact]
    public void A_frame_without_a_label_is_unlabelled_but_still_parsed()
    {
        Assert.Equal(Verdict.Unlabelled, VerdictClassifier.Classify("17,020", null, out var parsed));
        Assert.Equal(17020, parsed);
    }
}
