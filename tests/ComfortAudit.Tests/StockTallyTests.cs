using System.Linq;
using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class StockTallyTests
    {
        [Fact]
        public void SumsAcrossStacksAndContainers()
        {
            var t = new StockTally();
            t.CountContainer();
            t.Add("$item_finewood", 10);
            t.Add("$item_finewood", 5);
            t.CountContainer();
            t.Add("$item_finewood", 3);

            Assert.Equal(18, t.Count("$item_finewood"));
            Assert.Equal(2, t.Containers);
        }

        [Fact]
        public void UnknownTokenIsZero()
        {
            Assert.Equal(0, new StockTally().Count("$item_bronze"));
            Assert.Equal(0, new StockTally().Count(null));
        }

        [Fact]
        public void IgnoresEmptyTokensAndNonPositiveCounts()
        {
            var t = new StockTally();
            t.Add(null, 5);
            t.Add("", 5);
            t.Add("$item_stone", 0);
            t.Add("$item_stone", -2);

            Assert.Empty(t.Items);
        }

        [Fact]
        public void TokensAreCaseSensitive()
        {
            // Inventory.CountItems compares m_shared.m_name ordinally; so must we.
            var t = new StockTally();
            t.Add("$item_Wood", 1);
            Assert.Equal(0, t.Count("$item_wood"));
        }

        [Fact]
        public void ItemsAreSortedForStableOutput()
        {
            var t = new StockTally();
            t.Add("$item_wood", 1);
            t.Add("$item_bronze", 1);
            Assert.Equal(new[] { "$item_bronze", "$item_wood" }, t.Items.Select(kv => kv.Key));
        }

        [Fact]
        public void EmptyIsEmpty()
        {
            Assert.Equal(0, StockTally.Empty.Containers);
            Assert.Empty(StockTally.Empty.Items);
        }
    }
}
