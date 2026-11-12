using System;
using System.Collections;
using Emberhold.Core;
using Emberhold.Gameplay.Building;
using Emberhold.Gameplay.Items;
using Emberhold.Gameplay.Lighting;
using Emberhold.Gameplay.Workers;
using Emberhold.World;
using Emberhold.World.Editing;
using Emberhold.World.Items;
using Emberhold.World.Voxels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberhold.Tests.PlayMode.Workers
{
    public sealed class WorkersPrototypeSceneTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_WorkersBuildBlueprintsWithStone()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;

            var crew = Object.FindAnyObjectByType<WorkerCrew>();
            Assert.That(crew, Is.Not.Null, "В сцене Prototype нет WorkerCrew");
            Assert.That(crew.enabled, Is.True);
            var editor = Object.FindAnyObjectByType<BlockEditController>();
            var view = Object.FindAnyObjectByType<VoxelWorldView>();
            var zone = Object.FindAnyObjectByType<BuildZone>();
            var dropped = Object.FindAnyObjectByType<DroppedItemsView>();

            // Стартовый состав (#161) стоит в свете костра: лесоруб, копатель, истопник и двое свободных — они и строят.
            Assert.That(crew.Workers, Has.Count.EqualTo(5));
            var roles = new WorkerRole[crew.Workers.Count];
            for (var i = 0; i < roles.Length; i++)
                roles[i] = crew.Workers[i].Role;
            Assert.That(roles, Is.EqualTo(new[] { WorkerRole.Woodcutter, WorkerRole.Digger, WorkerRole.Stoker, WorkerRole.Free, WorkerRole.Free }));
            foreach (var worker in crew.Workers)
                Assert.That(zone.Contains(view.transform.TransformPoint(worker.Position)), Is.True, $"Рабочий в темноте: {worker.Position}");

            // Чертёж земляного блока (бесплатный) в трёх блоках от костра; повторный клик его снимает.
            var dirtHit = SurfaceHit(view, 3.5f, 0.5f);
            editor.Tool = BlockTool.Blueprint;
            while (editor.Selection.Material != BlockMaterials.Dirt)
                editor.Selection.NextMaterial();
            Assert.That(editor.Apply(dirtHit), Is.True);
            Assert.That(crew.Orders.At(dirtHit.Block + Vector3Int.up), Is.Not.Null);
            Assert.That(editor.Apply(dirtHit), Is.True);
            Assert.That(HasBlueprints(crew), Is.False, "Повторный клик снимает чертёж");
            Assert.That(editor.Apply(dirtHit), Is.True);
            var dirtCell = dirtHit.Block + Vector3Int.up;
            Assert.That(view.World.GetBlock(dirtCell).IsAir, Is.True, "Чертёж — ещё не блок");

            // Чертёж каменного блока с другой стороны костра и камень на земле рядом.
            var stoneHit = SurfaceHit(view, -3.5f, 0.5f);
            while (editor.Selection.Material != BlockMaterials.Stone)
                editor.Selection.NextMaterial();
            Assert.That(editor.Apply(stoneHit), Is.True);
            var stoneCell = stoneHit.Block + Vector3Int.up;
            dropped.Items.Drop(view.World, SurfaceHit(view, 0.5f, 4.5f).Block + Vector3Int.up, new ItemDrop("stone", 1));

            var timeScale = Time.timeScale;
            Time.timeScale = 4f;
            try
            {
                var start = Time.realtimeSinceStartup;
                while (HasBlueprints(crew) && Time.realtimeSinceStartup - start < 30f)
                    yield return null;
            }
            finally
            {
                Time.timeScale = timeScale;
            }

            Assert.That(HasBlueprints(crew), Is.False, "Рабочие не достроили чертежи");
            Assert.That(view.World.GetBlock(dirtCell).Material, Is.EqualTo(BlockMaterials.Dirt));
            Assert.That(view.World.GetBlock(stoneCell).Material, Is.EqualTo(BlockMaterials.Stone));
            Assert.That(dropped.Items.Items, Is.Empty, "Камень ушёл в блок");
        }

        /// <summary>Остались чертежи (задачи топлива для костра висят всегда, пока в нём есть место).</summary>
        private static bool HasBlueprints(WorkerCrew crew)
        {
            foreach (var order in crew.Orders.Orders)
            {
                if (order.Kind == WorkKind.Build)
                    return true;
            }
            return false;
        }

        /// <summary>Верхний блок под точкой сцены (x, z) и попадание по его верхней грани.</summary>
        private static BlockHit SurfaceHit(VoxelWorldView view, float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var raycast, 400f, Layers.GroundMask)
                || !view.TryGetBlock(raycast.collider, raycast.triangleIndex, out var block))
                throw new InvalidOperationException($"Нет земли под ({x}, {z})");
            return new BlockHit(block, Vector3Int.up);
        }
    }
}
