using IntentAutomation;
using Xunit;

namespace ScenarioRunner
{
    public class IntentTextScoringTests
    {
        [Fact]
        public void TokenOverlap_ReturnsFractionOfTargetTokensFoundInElement()
        {
            var score = IntentTextScoring.TokenOverlap("submit order form", "Submit Order Button");

            Assert.Equal(2.0 / 3.0, score, precision: 5);
        }

        [Fact]
        public void TokenOverlap_ReturnsZero_WhenTargetTextIsEmpty()
        {
            Assert.Equal(0.0, IntentTextScoring.TokenOverlap("", "Submit"));
        }

        [Fact]
        public void TokenOverlap_ReturnsZero_WhenElementTextIsEmpty()
        {
            Assert.Equal(0.0, IntentTextScoring.TokenOverlap("submit order", ""));
        }

        [Fact]
        public void TokenOverlap_IsCaseInsensitive()
        {
            Assert.Equal(1.0, IntentTextScoring.TokenOverlap("SUBMIT", "submit"));
        }

        [Fact]
        public void Tokens_DropsSingleCharacterTokens()
        {
            var tokens = IntentTextScoring.Tokens("a submit b order c");

            Assert.Equal(new[] { "submit", "order" }, tokens);
        }

        [Fact]
        public void ContainsNormalized_MatchesAcrossPunctuationAndCase()
        {
            Assert.True(IntentTextScoring.ContainsNormalized("Submit-Order Button!", "submit order"));
        }

        [Fact]
        public void ContainsNormalized_ReturnsFalse_WhenNeedleNormalizesToSingleCharacterOrLess()
        {
            Assert.False(IntentTextScoring.ContainsNormalized("Submit Order", "!"));
        }

        [Fact]
        public void NormalizeText_LowercasesAndReplacesNonAlphanumericWithSpace()
        {
            Assert.Equal("submit order 42 ", IntentTextScoring.NormalizeText("Submit-Order_42!"));
        }

        [Fact]
        public void Join_SkipsNullAndWhitespaceValues()
        {
            var joined = IntentTextScoring.Join("Submit", "", null!, "  ", "Order");

            Assert.Equal("Submit Order", joined);
        }
    }
}
