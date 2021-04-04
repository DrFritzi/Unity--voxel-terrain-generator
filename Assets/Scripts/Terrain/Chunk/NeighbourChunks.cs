using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelTG.Terrain.Blocks;
using static VoxelTG.WorldSettings;

namespace VoxelTG.Terrain.Chunks
{
    public class NeighbourChunks
    {
        private Chunk[] neighbours;
        public NeighbourChunks(Chunk chunk)
        {
            neighbours = new Chunk[9];

            World world = World.Instance;
            if (chunk == null || world == null)
                return;

            Vector2Int chunkPos = chunk.ChunkPosition;
            Direction[] allDirections = (Direction[])System.Enum.GetValues(typeof(Direction));
            foreach (var dir in allDirections)
            {
                var neighbour = World.GetChunk(chunkPos + dir.ToVector2Int() * WorldSettings.ChunkSizeXZ);
                this[dir] = neighbour;
                if (neighbour != null && neighbour.NeighbourChunks != null)
                    neighbour.NeighbourChunks[dir.GetOpposite()] = chunk;
            }
        }

        public Chunk this[Direction dir]
        {
            get => neighbours[(int)dir];
            set => neighbours[(int)dir] = value;
        }

        public Chunk this[int dir]
        {
            get => neighbours[dir];
            set => neighbours[dir] = value;
        }

        public bool IsNeighbour(Chunk chunk)
        {
            foreach (var neighbour in neighbours)
                if (neighbour == chunk)
                    return true;

            return false;
        }

        public void SyncNeighbourParameters(BlockParameter parameter, short value)
        {
            int3 blockPos = parameter.blockPos;
            // check neighbours
            if (blockPos.x == ChunkSizeXZ && this[Direction.E])
            {
                BlockParameter neighbourParameter = parameter;
                neighbourParameter.blockPos = new int3(0, blockPos.y, blockPos.z);
                this[Direction.E].SetBlockParameterWithoutSync(neighbourParameter, value);
            }
            else if (blockPos.x == 1 && this[Direction.W])
            {
                BlockParameter neighbourParameter = parameter;
                neighbourParameter.blockPos = new int3(FixedChunkSizeXZ - 1, blockPos.y, blockPos.z);
                this[Direction.W].SetBlockParameterWithoutSync(neighbourParameter, value);
            }

            if (blockPos.z == 16 && this[Direction.N])
            {
                BlockParameter neighbourParameter = parameter;
                neighbourParameter.blockPos = new int3(blockPos.x, blockPos.y, 0);
                this[Direction.N].SetBlockParameterWithoutSync(neighbourParameter, value);
            }
            else if (blockPos.z == 1 && this[Direction.S])
            {
                BlockParameter neighbourParameter = parameter;
                neighbourParameter.blockPos = new int3(blockPos.x, blockPos.y, FixedChunkSizeXZ - 1);
                this[Direction.S].SetBlockParameterWithoutSync(neighbourParameter, value);
            }
        }

        public void SyncNeighbourParametersRemove(BlockParameter parameter)
        {
            int3 blockPosition = parameter.blockPos;
            // check neighbours
            if (blockPosition.x == ChunkSizeXZ && this[Direction.E])
            {
                this[Direction.E].RemoveParameterAtWithoutSync(new int3(0, blockPosition.y, blockPosition.z));
            }
            else if (blockPosition.x == 1 && this[Direction.W])
            {
                this[Direction.W].RemoveParameterAtWithoutSync(new int3(FixedChunkSizeXZ - 1, blockPosition.y, blockPosition.z));
            }

            if (blockPosition.z == 16 && this[Direction.N])
            {
                this[Direction.N].RemoveParameterAtWithoutSync(new int3(blockPosition.x, blockPosition.y, 0));
            }
            else if (blockPosition.z == 1 && this[Direction.S])
            {
                this[Direction.S].RemoveParameterAtWithoutSync(new int3(blockPosition.x, blockPosition.y, FixedChunkSizeXZ - 1));
            }
        }

        public void SyncNeighbourBlocks(List<Chunk> chunksToBuild, BlockPosition blockPosition, BlockType blockType)
        {
            // check neighbours
            if (blockPosition.x == ChunkSizeXZ && this[Direction.E])
            {
                var chunk = this[Direction.E];
                chunk.SetBlock(0, blockPosition.y, blockPosition.z, blockType, SetBlockSettings.VANISH);
                chunksToBuild.Add(chunk);
            }
            else if (blockPosition.x == 1 && this[Direction.W])
            {
                var chunk = this[Direction.W];
                chunk.SetBlock(FixedChunkSizeXZ - 1, blockPosition.y, blockPosition.z, blockType, SetBlockSettings.VANISH);
                chunksToBuild.Add(chunk);
            }

            if (blockPosition.z == 16 && this[Direction.N])
            {
                var chunk = this[Direction.N];
                chunk.SetBlock(blockPosition.x, blockPosition.y, 0, blockType, SetBlockSettings.VANISH);
                chunksToBuild.Add(chunk);
            }
            else if (blockPosition.z == 1 && this[Direction.S])
            {
                var chunk = this[Direction.S];
                chunk.SetBlock(blockPosition.x, blockPosition.y, FixedChunkSizeXZ - 1, blockType, SetBlockSettings.VANISH);
                chunksToBuild.Add(chunk);
            }
        }

        public void SyncBorders()
        {

        }
    }
}