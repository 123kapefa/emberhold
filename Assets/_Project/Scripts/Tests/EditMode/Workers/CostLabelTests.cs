using Emberhold.Gameplay.Workers;
using Emberhold.World.Items;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class CostLabelTests
    {
        [TestCase(1, "1 бревно")]
        [TestCase(3, "3 бревна")]
        [TestCase(6, "6 брёвен")]
        [TestCase(11, "11 брёвен")]
        [TestCase(21, "21 бревно")]
        [TestCase(24, "24 бревна")]
        public void Amount_UsesRussianNumberForms(int count, string expected) =>
            Assert.That(CostLabel.Amount(new ItemDrop("wood", count)), Is.EqualTo(expected));

        [Test]
        public void Amount_OtherItems()
        {
            Assert.That(CostLabel.Amount(new ItemDrop("stone", 2)), Is.EqualTo("2 камня"));
            Assert.That(CostLabel.Amount(new ItemDrop("stone", 5)), Is.EqualTo("5 камней"));
            Assert.That(CostLabel.Amount(new ItemDrop("iron", 3)), Is.EqualTo("3 iron"), "Незнакомый ресурс — по id");
        }

        [Test]
        public void Progress_ShowsDeliveredOfNeeded() =>
            Assert.That(CostLabel.Progress("wood", 2, 6), Is.EqualTo("бревно 2/6"));
    }
}
