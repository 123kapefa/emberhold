using Emberhold.Gameplay.Building;
using Emberhold.World.Voxels;
using NUnit.Framework;

namespace Emberhold.Tests.EditMode.Building
{
    public sealed class BuildSelectionTests
    {
        [Test]
        public void Shapes_MatchDecidedForms()
        {
            Assert.That(BuildSelection.Shape(BuildForm.Full, 0, 8), Is.EqualTo(BlockShape.Full));
            Assert.That(BuildSelection.Shape(BuildForm.Slab, 0, 5), Is.EqualTo(BlockShape.Flat(5)));
            Assert.That(BuildSelection.Shape(BuildForm.Ramp, 0, 8), Is.EqualTo(new BlockShape(0, 0, 16, 16)));
            Assert.That(BuildSelection.Shape(BuildForm.OuterCorner, 0, 8), Is.EqualTo(new BlockShape(0, 0, 0, 16)));
            Assert.That(BuildSelection.Shape(BuildForm.InnerCorner, 0, 8), Is.EqualTo(new BlockShape(0, 16, 16, 16)));
            Assert.That(BuildSelection.Shape(BuildForm.Stairs, 0, 8), Is.EqualTo(BlockShape.Catalog(BlockForm.Stairs, 0)));
        }

        [Test]
        public void Rotation_TurnsEveryFormClockwise()
        {
            Assert.That(BuildSelection.Shape(BuildForm.Ramp, 1, 8), Is.EqualTo(new BlockShape(0, 0, 16, 16).RotatedClockwise()));
            Assert.That(BuildSelection.Shape(BuildForm.Stairs, 3, 8), Is.EqualTo(BlockShape.Catalog(BlockForm.Stairs, 3)));
            // Плоские формы от поворота не меняются.
            Assert.That(BuildSelection.Shape(BuildForm.Slab, 2, 8), Is.EqualTo(BlockShape.Flat(8)));
        }

        [Test]
        public void Selection_CyclesAndClamps()
        {
            var selection = new BuildSelection();
            var forms = System.Enum.GetValues(typeof(BuildForm)).Length;
            for (var i = 0; i < forms; i++)
                selection.NextForm();
            Assert.That(selection.Form, Is.EqualTo(BuildForm.Full));

            for (var i = 0; i < 4; i++)
                selection.Rotate();
            Assert.That(selection.Rotation, Is.Zero);

            selection.SlabHeight = 40;
            Assert.That(selection.SlabHeight, Is.EqualTo(16));
            selection.SlabHeight = -3;
            Assert.That(selection.SlabHeight, Is.EqualTo(1));

            Assert.That(BuildSelection.Materials, Does.Contain(selection.Material));
            selection.NextMaterial(-1);
            Assert.That(BuildSelection.Materials, Does.Contain(selection.Material));
        }
    }
}
