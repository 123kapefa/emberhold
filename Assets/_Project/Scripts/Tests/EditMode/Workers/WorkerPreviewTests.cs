using System.IO;
using Emberhold.Editor.Workers;
using NUnit.Framework;
using UnityEngine;

namespace Emberhold.Tests.EditMode.Workers
{
    public sealed class WorkerPreviewTests
    {
        [Test]
        public void RenderToTexture_DrawsWorkerOverBackground()
        {
            using var preview = new WorkerPreview { Wireframe = true };
            preview.Rebuild();
            var texture = preview.RenderToTexture(256, 256);
            try
            {
                // Посередине кадра — рабочий, в углу — фон.
                var corner = texture.GetPixel(2, 2);
                var differs = 0;
                for (var y = 64; y < 192; y += 4)
                for (var x = 112; x < 144; x += 4)
                {
                    var c = texture.GetPixel(x, y);
                    if (Mathf.Abs(c.r - corner.r) + Mathf.Abs(c.g - corner.g) + Mathf.Abs(c.b - corner.b) > 0.1f)
                        differs++;
                }
                Assert.That(differs, Is.GreaterThan(50), "Рабочий нарисован в середине кадра");
                Assert.That(preview.TriangleCount, Is.GreaterThan(500));
                var output = System.Environment.GetEnvironmentVariable("WORKER_PREVIEW_PNG");
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllBytes(output, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
