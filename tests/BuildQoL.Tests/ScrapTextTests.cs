using System.Collections.Generic;
using BuildQoL.Pure;
using Xunit;

namespace BuildQoL.Tests
{
    public class ScrapTextTests
    {
        static List<KeyValuePair<string, int>> O(params (string name, int n)[] xs)
        {
            var l = new List<KeyValuePair<string, int>>();
            foreach (var (name, n) in xs) l.Add(new KeyValuePair<string, int>(name, n));
            return l;
        }

        [Theory]
        [InlineData("LeftCtrl", "Ctrl")]
        [InlineData("RightCtrl", "Ctrl")]
        [InlineData("LeftShift", "Shift")]
        [InlineData("RightAlt", "Alt")]
        [InlineData("LeftArrow", "LeftArrow")]
        [InlineData("Q", "Q")]
        [InlineData("", "?")]
        [InlineData(null, "?")]
        public void KeyLabel_DropsTheSideOfModifiersOnly(string key, string expected) => Assert.Equal(expected, ScrapText.KeyLabel(key));

        [Fact]
        public void Held_ShowsCountAndKg() =>
            Assert.Equal("Ctrl: scrap contents (5 canisters, 5.0 kg)", ScrapText.Prompt(true, "Ctrl", 5, 5.0, null, true));

        [Fact]
        public void Held_Singular() =>
            Assert.Equal("Ctrl: scrap contents (1 canister, 0.3 kg)", ScrapText.Prompt(true, "Ctrl", 1, 0.25, null, true));

        [Fact]
        public void NotHeld_ShowsHint() =>
            Assert.Equal("Hold Ctrl to scrap contents (5)", ScrapText.Prompt(false, "Ctrl", 5, 5.0, null, true));

        [Fact]
        public void Empty_IsNull()
        {
            Assert.Null(ScrapText.Prompt(true, "Ctrl", 0, 0, null, true));
            Assert.Null(ScrapText.Prompt(false, "Ctrl", 0, 0, O(("Andrew", 2)), false));
        }

        [Fact]
        public void OthersScrapped_AreNamed() =>
            Assert.Equal("Ctrl: scrap contents (5 canisters, 5.0 kg, 2 are Andrew's)", ScrapText.Prompt(true, "Ctrl", 5, 5.0, O(("Andrew", 2)), true));

        [Fact]
        public void OthersScrapped_UnknownFallsBack()
        {
            Assert.Equal("Hold Ctrl to scrap contents (5, 2 are others')", ScrapText.Prompt(false, "Ctrl", 5, 5.0, O((null, 2)), true));
            Assert.Equal("Hold Ctrl to scrap contents (3, 1 is Andrew's, 1 is another player's)", ScrapText.Prompt(false, "Ctrl", 3, 3.0, O(("Andrew", 1), ("", 1)), true));
        }

        [Fact]
        public void OthersKept_SayTheyStay() =>
            Assert.Equal("Ctrl: scrap contents (3 canisters, 3.0 kg; Andrew's 2 stay)", ScrapText.Prompt(true, "Ctrl", 3, 3.0, O(("Andrew", 2)), false));

        [Fact]
        public void ZeroCountOthers_AreIgnored() =>
            Assert.Equal("Hold Ctrl to scrap contents (2)", ScrapText.Prompt(false, "Ctrl", 2, 2.0, O(("Andrew", 0)), true));

        [Theory]
        [InlineData(3, 3.0, 12, false, "scrapped 3 canisters (3.0 kg) from belt 12 as host")]
        [InlineData(1, 1.04, 7, true, "scrapped 1 canister (1.0 kg) from belt 7 as client")]
        public void Log_Line(int n, double kg, int belt, bool client, string expected) => Assert.Equal(expected, ScrapText.Log(n, kg, belt, client));

        [Theory]
        [InlineData("local", "local", "other", true)]
        [InlineData("steam-1", "local", "steam-1", true)]
        [InlineData("steam-2", "local", "steam-1", false)]
        [InlineData("", "local", null, true)]
        [InlineData(null, null, null, true)]
        [InlineData("x", null, "", false)]
        public void IsMine_EitherLocalId(string owner, string a, string b, bool expected) => Assert.Equal(expected, ScrapText.IsMine(owner, a, b));

        static List<KeyValuePair<string, double>> R(params (string owner, double kg)[] xs)
        {
            var l = new List<KeyValuePair<string, double>>();
            foreach (var (o, k) in xs) l.Add(new KeyValuePair<string, double>(o, k));
            return l;
        }

        [Fact]
        public void Count_OthersScrapped_CountsAllAndListsOthersInOrder()
        {
            var t = ScrapText.Count(R(("me", 1.0), ("andrew", 2.0), ("bob", 0.5), ("andrew", 1.0), ("", 0.25)), "me", "steam-me", true);
            Assert.Equal(5, t.Count);
            Assert.Equal(4.75, t.Kg, 6);
            Assert.Equal(new[] { new KeyValuePair<string, int>("andrew", 2), new KeyValuePair<string, int>("bob", 1) }, t.Others);
        }

        [Fact]
        public void Count_OthersKept_CountsMineOnly()
        {
            var t = ScrapText.Count(R(("me", 1.0), ("andrew", 2.0), ("steam-me", 1.5)), "me", "steam-me", false);
            Assert.Equal(2, t.Count);
            Assert.Equal(2.5, t.Kg, 6);
            Assert.Single(t.Others);
        }

        [Theory]
        [InlineData("LeftCtrl", "RightCtrl")]
        [InlineData("RightShift", "LeftShift")]
        [InlineData("LeftAlt", "RightAlt")]
        [InlineData("LeftArrow", null)]
        [InlineData("X", null)]
        [InlineData(null, null)]
        public void OtherSide_PairedModifiersOnly(string key, string expected) => Assert.Equal(expected, ScrapText.OtherSide(key));

        [Fact]
        public void Kg_IsInvariant()
        {
            var prev = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.Equal("2.5 kg", ScrapText.Kg(2.5));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
        }
    }
}
