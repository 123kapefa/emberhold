using Emberhold.World.Voxels;
using UnityEngine;

namespace Emberhold.World.Generation
{
    [CreateAssetMenu(fileName = "ValleySettings", menuName = "Emberhold/World/Valley Settings")]
    public sealed class ValleySettings : ScriptableObject
    {
        [SerializeField] private ValleyParameters parameters = ValleyParameters.Default;

        public ValleyParameters Parameters => parameters;

        private void OnValidate()
        {
            parameters.SizeInChunks = Vector3Int.Max(parameters.SizeInChunks, Vector3Int.one);
            var maxHeight = parameters.SizeInChunks.y * (float)ChunkData.Size;
            parameters.FloorHeight = Mathf.Clamp(parameters.FloorHeight, 0f, maxHeight);
            parameters.RimHeight = Mathf.Clamp(parameters.RimHeight, 0f, maxHeight);
        }
    }
}
