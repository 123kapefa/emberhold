using System.Collections.Generic;
using System.Linq;
using Emberhold.Gameplay.Workers;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Workers
{
    /// <summary>Ручки зон (#136): что меняет каждая (логика в WorkZones) и где они стоят (ZoneHandleLayout).</summary>
    public sealed class ZoneHandleTests
    {
        private WorkZones zones;
        private WorkOrders orders;

        [SetUp]
        public void SetUp()
        {
            zones = new WorkZones();
            orders = new WorkOrders();
        }

        private LevelZone Rect(int x, int z, int sx, int sz, int level = 2) =>
            zones.AddLevel(new Vector3Int(x, level + 1, z), new Vector3Int(x + sx - 1, level + 1, z + sz - 1));

        [Test]
        public void DragEdges_MovesOnlyDraggedSides()
        {
            var zone = Rect(5, 5, 3, 2);

            zones.DragEdges(zone, 1, 0, new Vector2Int(10, 99), orders);
            Assert.That(zone.Min, Is.EqualTo(new Vector2Int(5, 5)), "Противоположная сторона стоит");
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(6, 2)), "Сторона больших x — до столбца 10");

            zones.DragEdges(zone, -1, 0, new Vector2Int(3, 0), orders);
            Assert.That(zone.Min, Is.EqualTo(new Vector2Int(3, 5)));
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(8, 2)));

            // Угол двигает сразу две стороны.
            zones.DragEdges(zone, 1, -1, new Vector2Int(4, 2), orders);
            Assert.That(zone.Min, Is.EqualTo(new Vector2Int(3, 2)));
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(2, 5)));
            Assert.That(zone.Level, Is.EqualTo(2), "Уровень стоит");
        }

        [Test]
        public void DragEdges_CannotCrossOppositeSide_NorExceedMaxSize()
        {
            var zone = Rect(5, 5, 3, 3);

            zones.DragEdges(zone, 1, 0, new Vector2Int(-20, 0), orders);
            Assert.That(zone.Size.x, Is.EqualTo(LevelZone.MinSize), "Остаётся хотя бы столбец");
            Assert.That(zone.Min.x, Is.EqualTo(5));

            zones.DragEdges(zone, 0, -1, new Vector2Int(0, -500), orders);
            Assert.That(zone.Size.y, Is.EqualTo(LevelZone.MaxSize));
            Assert.That(zone.Min.y + zone.Size.y - 1, Is.EqualTo(7), "Дальняя сторона по z стоит");
        }

        [Test]
        public void Move_KeepsSizeAndLevel_CancelsOrdersLeftBehind()
        {
            var zone = Rect(5, 5, 2, 1);
            var order = orders.AddDig(new Vector3Int(5, 3, 5));
            Assert.That(orders.Assign(order, zone.Assignment), Is.True);

            zones.Move(zone, new Vector2Int(20, 8), orders);

            Assert.That(zone.Min, Is.EqualTo(new Vector2Int(20, 8)));
            Assert.That(zone.Size, Is.EqualTo(new Vector2Int(2, 1)));
            Assert.That(zone.Level, Is.EqualTo(2));
            Assert.That(orders.Contains(order), Is.False, "Приказ за зоной снят");
        }

        [Test]
        public void ChopZone_MoveAndRadiusToPoint()
        {
            var zone = zones.AddChop(new Vector3Int(10, 4, 10), 3);

            zones.SetRadiusTo(zone, new Vector2(10.5f + 5.2f, 10.5f), orders);
            Assert.That(zone.Radius, Is.EqualTo(5));
            zones.SetRadiusTo(zone, new Vector2(10.5f, 10.5f), orders);
            Assert.That(zone.Radius, Is.EqualTo(ChopZone.MinRadius));

            zones.Move(zone, new Vector3Int(3, 4, 7), orders);
            Assert.That(zone.Cell, Is.EqualTo(new Vector3Int(3, 4, 7)));
        }

        [Test]
        public void Layout_RectHasSidesCornersMoveAndLevel()
        {
            var zone = Rect(5, 5, 4, 2, level: 2);
            var handles = new List<ZoneHandle>();

            ZoneHandleLayout.Build(zone, _ => 4f, handles);

            Assert.That(handles.Count(h => h.Kind == ZoneHandleKind.Edge), Is.EqualTo(8), "4 стороны и 4 угла");
            var east = handles.Single(h => h.Kind == ZoneHandleKind.Edge && h.Dx == 1 && h.Dz == 0);
            Assert.That(east.Position.x, Is.EqualTo(9f));
            Assert.That(east.Position.z, Is.EqualTo(6f), "Посередине стороны");
            var corner = handles.Single(h => h.Dx == -1 && h.Dz == -1);
            Assert.That(new Vector2(corner.Position.x, corner.Position.z), Is.EqualTo(new Vector2(5f, 5f)));
            var move = handles.Single(h => h.Kind == ZoneHandleKind.Move);
            Assert.That(new Vector2(move.Position.x, move.Position.z), Is.EqualTo(zone.Center));
            var level = handles.Single(h => h.Kind == ZoneHandleKind.Level);
            Assert.That(level.Position.y, Is.EqualTo(4f + 0.15f + ZoneHandleLayout.LevelLift).Within(1e-4f), "Над землёй, если она выше уровня");
        }

        [Test]
        public void Layout_ChopZoneHasMoveAndRadius()
        {
            var zone = zones.AddChop(new Vector3Int(10, 4, 10), 3);
            var handles = new List<ZoneHandle>();

            ZoneHandleLayout.Build(zone, _ => 4f, handles);

            Assert.That(handles.Select(h => h.Kind), Is.EquivalentTo(new[] { ZoneHandleKind.Move, ZoneHandleKind.Radius }));
            var radius = handles.Single(h => h.Kind == ZoneHandleKind.Radius);
            Assert.That(radius.Position.x, Is.EqualTo(10.5f + 3f).Within(1e-4f));
            Assert.That(radius.Position.z, Is.EqualTo(10.5f).Within(1e-4f));
        }
    }
}
