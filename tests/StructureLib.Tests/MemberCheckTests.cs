using Xunit;

namespace StructureLib.Tests
{
    public class MemberCheckTests
    {
        [Fact] public void MemberCheck_MissingMember_ReportsNameAndDisables() { var missing = MemberCheck.Missing(new[] { "A.B:C", "A.B:D" }, (t, m) => m == "C"); Assert.Equal(new[] { "A.B:D" }, missing); }
    }
}
