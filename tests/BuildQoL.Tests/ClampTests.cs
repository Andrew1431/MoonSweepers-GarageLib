using BuildQoL.Pure;
using Xunit;

namespace BuildQoL.Tests
{
    public class ClampTests
    {
        [Theory]
        [InlineData(5, 1, 10, 5)]
        [InlineData(0, 1, 10, 1)]
        [InlineData(11, 1, 10, 10)]
        [InlineData(7, 10, 1, 10)]
        public void Int_ClampsIntoRange(int value, int min, int max, int expected) => Assert.Equal(expected, Clamp.Int(value, min, max));
    }
}
