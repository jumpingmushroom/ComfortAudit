using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class MaterialMathTests
    {
        [Theory]
        [InlineData(4, 4, 0, MaterialState.Enough)]
        [InlineData(4, 9, 0, MaterialState.Enough)]
        [InlineData(4, 4, 7, MaterialState.Enough)]            // inventory alone suffices
        [InlineData(4, 1, 6, MaterialState.EnoughWithChests)]
        [InlineData(4, 0, 4, MaterialState.EnoughWithChests)]
        [InlineData(4, 1, 2, MaterialState.Short)]
        [InlineData(4, 0, 0, MaterialState.Short)]
        [InlineData(4, 1, -3, MaterialState.Short)]           // negative chest count is treated as none
        public void Classify(int amount, int have, int inChests, MaterialState expected)
        {
            Assert.Equal(expected, MaterialMath.Classify(amount, have, inChests));
        }

        [Theory]
        [InlineData(4, 4, 0, MaterialAnnotation.None)]         // enough: no clutter
        [InlineData(4, 4, 6, MaterialAnnotation.None)]         // enough in inventory: chests irrelevant
        [InlineData(4, 0, 0, MaterialAnnotation.None)]         // red already says "you have none"
        [InlineData(4, 2, 0, MaterialAnnotation.Have)]         // some but not enough
        [InlineData(4, 1, 6, MaterialAnnotation.HaveAndChests)]
        [InlineData(4, 0, 2, MaterialAnnotation.HaveAndChests)] // short even with chests, but chests help
        public void Annotate(int amount, int have, int inChests, MaterialAnnotation expected)
        {
            Assert.Equal(expected, MaterialMath.Annotate(amount, have, inChests));
        }
    }
}
