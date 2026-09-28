using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class SpriteTagsTests
    {
        [Fact]
        public void TagWrapsNameAndAddsSpacing()
        {
            Assert.Equal("<sprite name=\"piece_chair\"> ", SpriteTags.Tag("piece_chair"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NoNameNoTag(string name)
        {
            Assert.Equal("", SpriteTags.Tag(name));
        }

        [Theory]
        [InlineData("piece_chair", "piece_chair")]
        [InlineData("Some Mod \"Chair\" <v2>", "Some Mod _Chair_ _v2_")] // would break the tag
        [InlineData(null, null)]
        [InlineData("", null)]
        public void GlyphNameStripsTagBreakingCharacters(string prefab, string expected)
        {
            Assert.Equal(expected, SpriteTags.GlyphName(prefab));
        }
    }
}
