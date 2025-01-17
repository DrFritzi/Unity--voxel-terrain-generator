﻿using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelTG.Entities.Items;
using VoxelTG.Jobs;
using VoxelTG.Player;
using VoxelTG.Player.Inventory;
using VoxelTG.Terrain.Blocks;
using VoxelTG.Terrain.Chunks;
using static VoxelTG.WorldSettings;

/*
 * Michał Czemierowski
 * https://github.com/michalczemierowski
*/
namespace VoxelTG.Terrain
{
    public class Chunk : MonoBehaviour, IDisposable
    {
        #region // === Variables === \\

        #region serializable

        [SerializeField] private MeshFilter blockMeshFilter;
        [SerializeField] private MeshFilter liquidMeshFilter;
        [SerializeField] private MeshFilter plantsMeshFilter;
        [SerializeField] private MeshCollider blockMeshCollider;

        #endregion

        #region public

        public MeshFilter BlockMeshFilter => blockMeshFilter;
        public MeshCollider BlockMeshCollider => blockMeshCollider;

        /// <summary>
        /// +x, -x, +z, -z
        /// </summary>
        public Chunk[] NeighbourChunks { get; private set; } = new Chunk[4];

        /// <summary>
        /// Array containing chunk block structure [x,y,z]
        /// </summary>
        public NativeArray<BlockType> blocks;

        public NativeArray<int> lightingData;

        /// <summary>
        /// Chunk position in World space
        /// </summary>
        [System.NonSerialized] public Vector2Int ChunkPosition;

        #endregion

        #region private

        /// <summary>
        /// Array containing information about biomes [x,z]
        /// </summary>
        private NativeArray<BiomeType> biomeTypes;

        private ComputeBuffer lightingBuffer;

        private ChunkDissapearingAnimation chunkDissapearingAnimation;
        private ChunkAnimation chunkAnimation;

        private NativeHashMap<BlockParameter, short> blockParameters;

        private NativeList<float3> blockVerticles;
        private NativeList<int> blockTriangles;
        private NativeList<float2> blockUVs;

        private NativeList<float3> liquidVerticles;
        private NativeList<int> liquidTriangles;
        private NativeList<float2> liquidUVs;

        private NativeList<float3> plantsVerticles;
        private NativeList<int> plantsTriangles;
        private NativeList<float2> plantsUVs;

        private List<BlockData> blocksToBuild = new List<BlockData>();
        private Dictionary<BlockParameter, short> parametersToAdd = new Dictionary<BlockParameter, short>();

        /// <summary>
        /// Has terrain changed since load
        /// </summary>
        private bool isTerrainModified;

        private Texture2D biomeColorsTexture;
        private readonly List<int> _nativeConversionList = new List<int>();

        #endregion

        #endregion

        #region // === Monobehaviour === \\

        public void Init()
        {
            if (!blocks.IsCreated)
            {
                // init native containers
                blocks = new NativeArray<BlockType>(FixedChunkSizeXZ * ChunkSizeY * FixedChunkSizeXZ, Allocator.Persistent);
                lightingData = new NativeArray<int>(FixedChunkSizeXZ * ChunkSizeY * FixedChunkSizeXZ, Allocator.Persistent);
                biomeTypes = new NativeArray<BiomeType>(FixedChunkSizeXZ * FixedChunkSizeXZ, Allocator.Persistent);
                blockParameters = new NativeHashMap<BlockParameter, short>(2048, Allocator.Persistent);

                blockVerticles = new NativeList<float3>(16384, Allocator.Persistent);
                blockTriangles = new NativeList<int>(32768, Allocator.Persistent);
                blockUVs = new NativeList<float2>(16384, Allocator.Persistent);

                liquidVerticles = new NativeList<float3>(8192, Allocator.Persistent);
                liquidTriangles = new NativeList<int>(16384, Allocator.Persistent);
                liquidUVs = new NativeList<float2>(8192, Allocator.Persistent);

                plantsVerticles = new NativeList<float3>(4096, Allocator.Persistent);
                plantsTriangles = new NativeList<int>(8192, Allocator.Persistent);
                plantsUVs = new NativeList<float2>(4096, Allocator.Persistent);

                chunkDissapearingAnimation = GetComponent<ChunkDissapearingAnimation>();
                chunkAnimation = GetComponent<ChunkAnimation>();
            }

            if (lightingBuffer == null || !lightingBuffer.IsValid())
            {
                lightingBuffer = new ComputeBuffer(FixedChunkSizeXZ * ChunkSizeY * FixedChunkSizeXZ, sizeof(int), ComputeBufferType.Default);
                var mr = blockMeshFilter.GetComponent<MeshRenderer>();
                mr.material.SetBuffer("lightData", lightingBuffer);
            }

            World.TimeToBuild += BuildBlocks;
            StartCoroutine(CheckNeighbours());
        }

        public void UpdateLightBuffer()
        {
            lightingBuffer.SetData(lightingData);
        }

        private void OnDisable()
        {
            World.TimeToBuild -= BuildBlocks;
        }

        public void Dispose()
        {
            if (!blocks.IsCreated)
                return;

            // dispose native containers
            blocks.Dispose();
            lightingData.Dispose();
            biomeTypes.Dispose();
            blockParameters.Dispose();

            blockVerticles.Dispose();
            blockTriangles.Dispose();
            blockUVs.Dispose();

            liquidVerticles.Dispose();
            liquidTriangles.Dispose();
            liquidUVs.Dispose();

            plantsVerticles.Dispose();
            plantsTriangles.Dispose();
            plantsUVs.Dispose();

            blocksToBuild.Clear();
            parametersToAdd.Clear();

            // clear meshes
            blockMeshFilter.mesh.Clear();
            liquidMeshFilter.mesh.Clear();
            plantsMeshFilter.mesh.Clear();

            // clear texture
            if (biomeColorsTexture != null)
                Destroy(biomeColorsTexture);

            lightingBuffer.Dispose();
        }

        #endregion

        private IEnumerator CheckNeighbours()
        {
            yield return new WaitForEndOfFrame();

            NeighbourChunks = new Chunk[]
            {
                World.GetChunk(ChunkPosition.x + ChunkSizeXZ, ChunkPosition.y),
                World.GetChunk(ChunkPosition.x - ChunkSizeXZ, ChunkPosition.y),
                World.GetChunk(ChunkPosition.x, ChunkPosition.y + ChunkSizeXZ),
                World.GetChunk(ChunkPosition.x, ChunkPosition.y - ChunkSizeXZ)
            };


            if (NeighbourChunks[0] && NeighbourChunks[0].NeighbourChunks.Length > 0)
                NeighbourChunks[0].SetNeighbour(1, this);
            if (NeighbourChunks[1] && NeighbourChunks[1].NeighbourChunks.Length > 0)
                NeighbourChunks[1].SetNeighbour(0, this);
            if (NeighbourChunks[2] && NeighbourChunks[2].NeighbourChunks.Length > 0)
                NeighbourChunks[2].SetNeighbour(3, this);
            if (NeighbourChunks[3] && NeighbourChunks[3].NeighbourChunks.Length > 0)
                NeighbourChunks[3].SetNeighbour(2, this);
        }

        private void SetNeighbour(int index, Chunk neighbour)
        {
            NeighbourChunks[index] = neighbour;
        }

        #region // === Mesh methods === \\

        /// <summary>
        /// Generate terrain data and build mesh
        /// </summary>
        public void GenerateTerrainDataAndBuildMesh(NativeQueue<JobHandle> jobHandles, int xPos, int zPos)
        {
            GenerateTerrainData generateTerrainData = new GenerateTerrainData()
            {
                chunkPosX = xPos,
                chunkPosZ = zPos,
                blockData = blocks,
                biomeTypes = biomeTypes,
                blockParameters = blockParameters,
                noise = World.FastNoise,
                biomeConfigs = Biomes.biomeConfigs,
                random = new Unity.Mathematics.Random((uint)(xPos * 10000 + zPos + 1000))
            };

            JobHandle handle = CreateMeshDataJob().Schedule(generateTerrainData.Schedule());
            jobHandles.Enqueue(handle);
        }

        /// <summary>
        /// Build mesh without generating terrain data
        /// </summary>
        public void BuildMesh(NativeQueue<JobHandle> jobHandles)
        {
            JobHandle handle = CreateMeshDataJob().Schedule();
            jobHandles.Enqueue(handle);
        }

        /// <summary>
        /// Build mesh without generating terrain data
        /// </summary>
        public void BuildMesh(List<JobHandle> jobHandles)
        {
            JobHandle handle = CreateMeshDataJob().Schedule();
            jobHandles.Add(handle);
        }

        private CreateMeshData CreateMeshDataJob()
        {
            CreateMeshData createMeshData = new CreateMeshData
            {
                blocks = blocks,
                biomeTypes = biomeTypes,
                blockParameters = blockParameters,

                blockVerticles = blockVerticles,
                blockTriangles = blockTriangles,
                blockUVs = blockUVs,
                liquidVerticles = liquidVerticles,
                liquidTriangles = liquidTriangles,
                liquidUVs = liquidUVs,
                plantsVerticles = plantsVerticles,
                plantsTriangles = plantsTriangles,
                plantsUVs = plantsUVs
            };
            return createMeshData;
        }

        /// <summary>
        /// Apply values from native containers (jobs) to meshes
        /// </summary>
        public void ApplyMesh()
        {
            Mesh blockMesh = blockMeshFilter.mesh;
            blockMesh.Clear();

            Mesh liquidMesh = liquidMeshFilter.mesh;
            liquidMesh.Clear();

            Mesh plantsMesh = plantsMeshFilter.mesh;
            plantsMesh.Clear();

            //TODO: change to new mesh API. Example https://github.com/keijiro/NoiseBall5/blob/master/Assets/NoiseBallRenderer.cs
            // blocks
            blockMesh.SetVertices<float3>(blockVerticles);
            _nativeConversionList.Clear();
            for (int i = 0; i < blockTriangles.Length; i++)
            {
                _nativeConversionList.Add(blockTriangles[i]);
            }
            blockMesh.SetTriangles(_nativeConversionList, 0, false);
            blockMesh.SetUVs<float2>(0, blockUVs);

            // liquids
            liquidMesh.SetVertices<float3>(liquidVerticles);
            _nativeConversionList.Clear();
            for (int i = 0; i < liquidTriangles.Length; i++)
            {
                _nativeConversionList.Add(liquidTriangles[i]);
            }
            liquidMesh.SetTriangles(_nativeConversionList, 0, false);
            liquidMesh.SetUVs<float2>(0, liquidUVs);

            //plants
            plantsMesh.SetVertices<float3>(plantsVerticles);
            _nativeConversionList.Clear();
            for (int i = 0; i < plantsTriangles.Length; i++)
            {
                _nativeConversionList.Add(plantsTriangles[i]);
            }
            plantsMesh.SetTriangles(_nativeConversionList, 0, false);
            plantsMesh.SetUVs<float2>(0, plantsUVs);
            

            blockMesh.RecalculateNormals((MeshUpdateFlags)int.MaxValue);
            blockMeshFilter.mesh = blockMesh;

            // bake mesh immediately if player is near
            Vector2 playerPosition = new Vector2(PlayerController.PlayerTransform.position.x, PlayerController.PlayerTransform.position.z);
            if (Vector2.Distance(new Vector2(ChunkPosition.x, ChunkPosition.y), playerPosition) < FixedChunkSizeXZ * 2)
                blockMeshCollider.sharedMesh = blockMesh;
            else
                World.SchedulePhysicsBake(this);

            liquidMesh.RecalculateNormals((MeshUpdateFlags)int.MaxValue);
            liquidMeshFilter.mesh = liquidMesh;

            plantsMesh.RecalculateNormals((MeshUpdateFlags)int.MaxValue);
            plantsMeshFilter.mesh = plantsMesh;

            // clear blocks
            blockVerticles.Clear();
            blockTriangles.Clear();
            blockUVs.Clear();

            // clear liquids
            liquidVerticles.Clear();
            liquidTriangles.Clear();
            liquidUVs.Clear();

            // clear plants
            plantsVerticles.Clear();
            plantsTriangles.Clear();
            plantsUVs.Clear();
        }

        /// <summary>
        /// Enable/disable chunk mesh renderers
        /// </summary>
        public void SetMeshRenderersActive(bool active)
        {
            foreach (var mr in GetComponentsInChildren<MeshRenderer>())
            {
                mr.enabled = active;
            }
        }

        #endregion

        #region // === Animations === \\

        /// <summary>
        /// Start chunk appear animation
        /// </summary>
        public void Animation()
        {
            ClearAnimations();

            chunkAnimation.enabled = true;
        }

        /// <summary>
        /// Reset all animations
        /// </summary>
        private void ClearAnimations()
        {
            transform.position = new Vector3(transform.position.x, 0, transform.position.z);
            chunkAnimation.enabled = false;
            chunkDissapearingAnimation.enabled = false;
        }

        /// <summary>
        /// Start chunk dissapearing animation
        /// </summary>
        public void DissapearingAnimation()
        {
            ClearAnimations();
            chunkDissapearingAnimation.enabled = true;
        }

        #endregion

        #region // === Parameters === \\

        /// <summary>
        /// Set parameter of block
        /// </summary>
        /// <param name="parameter">parameter type</param>
        /// <param name="value">parameter value</param>
        public void SetParameters(BlockParameter parameter, short value)
        {
            int3 blockPos = parameter.blockPos;
            // check neighbours
            if (blockPos.x == ChunkSizeXZ)
            {
                Chunk chunk = NeighbourChunks[0];
                if (chunk)
                {
                    BlockParameter neighbourParameter = parameter;
                    neighbourParameter.blockPos = new int3(0, blockPos.y, blockPos.z);

                    if (chunk.blockParameters.ContainsKey(neighbourParameter))
                        chunk.blockParameters[neighbourParameter] = value;
                    else
                        chunk.blockParameters.Add(neighbourParameter, value);
                }
            }
            else if (blockPos.x == 1)
            {
                Chunk chunk = NeighbourChunks[1];
                if (chunk)
                {
                    BlockParameter neighbourParameter = parameter;
                    neighbourParameter.blockPos = new int3(ChunkSizeXZ + 1, blockPos.y, blockPos.z);

                    if (chunk.blockParameters.ContainsKey(neighbourParameter))
                        chunk.blockParameters[neighbourParameter] = value;
                    else
                        chunk.blockParameters.Add(neighbourParameter, value);
                }
            }

            if (blockPos.z == ChunkSizeXZ)
            {
                Chunk chunk = NeighbourChunks[2];
                if (chunk)
                {
                    BlockParameter neighbourParameter = parameter;
                    neighbourParameter.blockPos = new int3(blockPos.x, blockPos.y, 0);

                    if (chunk.blockParameters.ContainsKey(neighbourParameter))
                        chunk.blockParameters[neighbourParameter] = value;
                    else
                        chunk.blockParameters.Add(neighbourParameter, value);
                }
            }
            else if (blockPos.z == 1)
            {
                Chunk chunk = NeighbourChunks[3];
                if (chunk)
                {
                    BlockParameter neighbourParameter = parameter;
                    neighbourParameter.blockPos = new int3(blockPos.x, blockPos.y, ChunkSizeXZ + 1);

                    if (chunk.blockParameters.ContainsKey(neighbourParameter))
                        chunk.blockParameters[neighbourParameter] = value;
                    else
                        chunk.blockParameters.Add(neighbourParameter, value);
                }
            }

            if (blockParameters.ContainsKey(parameter))
                blockParameters[parameter] = value;
            else
                blockParameters.Add(parameter, value);
        }

        /// <summary>
        /// Get parameter value
        /// </summary>
        /// <param name="parameter">parameter</param>
        /// <returns>value of parameter</returns>
        public short GetParameterValue(BlockParameter parameter)
        {
            if (blockParameters.ContainsKey(parameter))
            {
                return blockParameters[parameter]; ;
            }

            return 0;
        }

        /// <summary>
        /// Remove all parameters from block
        /// </summary>
        /// <param name="blockPosition">position of block</param>
        public void ClearParameters(BlockPosition blockPosition)
        {
            ClearParameters(blockPosition.ToInt3());
        }
        /// <summary>
        /// Remove all parameters from block
        /// </summary>
        /// <param name="x">x position of block</param>
        /// <param name="y">y position of block</param>
        /// <param name="z">z position of block</param>
        public void ClearParameters(int x, int y, int z)
        {
            ClearParameters(new int3(x, y, z));
        }
        /// <summary>
        /// Remove all parameters from block
        /// </summary>
        /// <param name="blockPos">position of block</param>
        public void ClearParameters(int3 blockPos)
        {
            BlockParameter key = new BlockParameter(blockPos);
            // check neighbours
            // TODO: add check neighbours method taking position as param and returning neighbour chunk and it's index
            if (blockPos.x == ChunkSizeXZ)
            {
                Chunk chunk = NeighbourChunks[0];
                if (chunk)
                {
                    BlockParameter neighbourKey = new BlockParameter(new int3(0, blockPos.y, blockPos.z));
                    while (chunk.blockParameters.ContainsKey(neighbourKey))
                        chunk.blockParameters.Remove(neighbourKey);
                }
            }
            else if (blockPos.x == 1)
            {
                Chunk chunk = NeighbourChunks[1];
                if (chunk)
                {
                    BlockParameter neighbourKey = new BlockParameter(new int3(ChunkSizeXZ + 1, blockPos.y, blockPos.z));
                    while (chunk.blockParameters.ContainsKey(neighbourKey))
                        chunk.blockParameters.Remove(neighbourKey);
                }
            }

            if (blockPos.z == ChunkSizeXZ)
            {
                Chunk chunk = NeighbourChunks[2];
                if (chunk)
                {
                    BlockParameter neighbourKey = new BlockParameter(new int3(blockPos.x, blockPos.y, 0));
                    while (chunk.blockParameters.ContainsKey(neighbourKey))
                        chunk.blockParameters.Remove(neighbourKey);
                }
            }
            else if (blockPos.z == 1)
            {
                Chunk chunk = NeighbourChunks[3];
                if (chunk)
                {
                    BlockParameter neighbourKey = new BlockParameter(new int3(blockPos.x, blockPos.y, ChunkSizeXZ + 1));
                    while (chunk.blockParameters.ContainsKey(neighbourKey))
                        chunk.blockParameters.Remove(neighbourKey);
                }
            }

            while (blockParameters.ContainsKey(key))
                blockParameters.Remove(key);
        }

        #endregion

        #region // === Editing methods === \\

        /// <summary>
        /// Get block at position [don't check if not out of range]
        /// </summary>
        /// <param name="blockPos">position of block</param>
        /// <returns>type of block</returns>
        public BlockType GetBlock(int3 blockPos)
        {
            return blocks[Utils.BlockPosition3DtoIndex(blockPos.x, blockPos.y, blockPos.z)];
        }
        /// <summary>
        /// Get block at position [don't check if not out of range]
        /// </summary>
        /// <param name="x">x position of block</param>
        /// <param name="y">y position of block</param>
        /// <param name="z">z position of block</param>
        /// <returns>type of block</returns>
        public BlockType GetBlock(int x, int y, int z)
        {
            return blocks[Utils.BlockPosition3DtoIndex(x, y, z)];
        }
        /// <summary>
        /// Get block at position [don't check if not out of range]
        /// </summary>
        /// <param name="blockPos">position of block</param>
        /// <returns>type of block</returns>
        public BlockType GetBlock(BlockPosition blockPos)
        {
            return blocks[Utils.BlockPosition3DtoIndex(blockPos.x, blockPos.y, blockPos.z)];
        }

        // TODO: add x,y,z and int3
        /// <summary>
        /// Try to get block at position [check if not out of range]
        /// </summary>
        /// <param name="blockPos">position of block</param>
        /// <param name="blockType">out type of block at position</param>
        /// <returns>true if chunk contains block at position</returns>
        public bool TryGetBlock(BlockPosition blockPos, out BlockType blockType)
        {
            if (Utils.IsPositionInChunkBounds(blockPos.x, blockPos.y, blockPos.z))
            {
                blockType = blocks[Utils.BlockPosition3DtoIndex(blockPos.x, blockPos.y, blockPos.z)];
                return true;
            }

            blockType = BlockType.AIR;
            return false;
        }

        /// <summary>
        /// Set block at position but don't rebuild mesh
        /// </summary>
        /// <param name="x">x position of block</param>
        /// <param name="y">y position of block</param>
        /// <param name="z">z position of block</param>
        /// <param name="blockType">type of block you want to place</param>
        /// <param name="destroy">spawn destroy particle</param>
        private void SetBlockWithoutRebuild(BlockPosition blockPosition, BlockType blockType, SetBlockSettings blockSettings)
        {
            //if (!Utils.IsPositionInChunkBounds(blockPosition)) return;
            BlockType currentBlock = GetBlock(blockPosition);

            if (blockSettings.callDestroyEvent)
                World.InvokeBlockDestroyEvent(new BlockEventData(this, blockPosition, currentBlock));
            if (blockSettings.callPlaceEvent)
                World.InvokeBlockPlaceEvent(new BlockEventData(this, blockPosition, blockType));
            if (blockSettings.dropItemPickup)
            {
                Vector3 worldPosition = Utils.LocalToWorldPositionVector3Int(ChunkPosition, blockPosition) + new Vector3(0.5f, 0.5f, 0.5f);

                ItemType dropItemType = ItemType.MATERIAL;
                BlockType dropBlockType = currentBlock;
                int count = 1;

                WorldData.GetCustomBlockDrops(currentBlock, ref dropItemType, ref dropBlockType, ref count);

                if (dropItemType == ItemType.MATERIAL)
                    DroppedItemsManager.Instance.DropItem(dropBlockType, worldPosition, amount: count, velocity: blockSettings.droppedItemVelocity, rotate: blockSettings.rotateDroppedItem);
                // TODO: check if name starts with tool etc.
                else
                    // TODO: item objects pool
                    DroppedItemsManager.Instance.DropItem(dropItemType, worldPosition, amount: count, velocity: blockSettings.droppedItemVelocity);
            }

            blocks[Utils.BlockPosition3DtoIndex(blockPosition)] = blockType;
            isTerrainModified = true;
        }

        /// <summary>
        /// Set block at position and rebuild mesh - use when you want to place one block, else take a look at
        /// <see cref="SetBlockWithoutRebuild(int, int, int, BlockType, bool)"/> or <see cref="SetBlocks(BlockData[], bool)"/>
        /// </summary>
        /// <param name="position">position of block</param>
        /// <param name="blockType">type of block you want to place</param>
        /// <param name="destroy">spawn destroy particle</param>
        public void SetBlock(int3 position, BlockType blockType, SetBlockSettings blockSettings)
        {
            SetBlock(new BlockPosition(position.x, position.y, position.z), blockType, blockSettings);
        }
        /// <summary>
        /// Set block at position and rebuild mesh - use when you want to place one block, else take a look at
        /// <see cref="SetBlockWithoutRebuild(int, int, int, BlockType, bool)"/> or <see cref="SetBlocks(BlockData[], bool)"/>
        /// </summary>
        /// <param name="position">position of block</param>
        /// <param name="blockType">type of block you want to place</param>
        /// <param name="destroy">spawn destroy particle</param>
        public void SetBlock(int x, int y, int z, BlockType blockType, SetBlockSettings blockSettings)
        {
            SetBlock(new BlockPosition(x, y, z), blockType, blockSettings);
        }
        /// <summary>
        /// Set block at position and rebuild mesh - use when you want to place one block, else take a look at
        /// <see cref="SetBlockWithoutRebuild(int, int, int, BlockType, bool)"/> or <see cref="SetBlocks(BlockData[], bool)"/>
        /// </summary>
        /// <param name="x">x position of block</param>
        /// <param name="y">y position of block</param>
        /// <param name="z">z position of block</param>
        /// <param name="blockType">type of block you want to place</param>
        /// <param name="destroy">spawn destroy particle</param>
        public void SetBlock(BlockPosition blockPosition, BlockType blockType, SetBlockSettings blockSettings)
        {
            List<JobHandle> jobHandles = new List<JobHandle>();
            List<Chunk> chunksToBuild = new List<Chunk>();

            SetBlockWithoutRebuild(blockPosition, blockType, blockSettings);

            // add current chunk
            BuildMesh(jobHandles);
            chunksToBuild.Add(this);

            // check neighbours
            // TODO: add check neighbours method taking position as param and returning neighbour chunk and it's index
            if (blockPosition.x == ChunkSizeXZ)
            {
                Chunk neighbourChunk = NeighbourChunks[0];
                if (neighbourChunk)
                {
                    neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(0, blockPosition.y, blockPosition.z)] = blockType;
                    neighbourChunk.isTerrainModified = true;
                    neighbourChunk.BuildMesh(jobHandles);
                    chunksToBuild.Add(neighbourChunk);
                }
            }
            else if (blockPosition.x == 1)
            {
                Chunk neighbourChunk = NeighbourChunks[1];
                if (neighbourChunk)
                {
                    neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(ChunkSizeXZ + 1, blockPosition.y, blockPosition.z)] = blockType;
                    neighbourChunk.isTerrainModified = true;
                    neighbourChunk.BuildMesh(jobHandles);
                    chunksToBuild.Add(neighbourChunk);
                }
            }

            if (blockPosition.z == ChunkSizeXZ)
            {
                Chunk neighbourChunk = NeighbourChunks[2];
                if (neighbourChunk)
                {
                    neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(blockPosition.x, blockPosition.y, 0)] = blockType;
                    neighbourChunk.isTerrainModified = true;
                    neighbourChunk.BuildMesh(jobHandles);
                    chunksToBuild.Add(neighbourChunk);
                }
            }
            else if (blockPosition.z == 1)
            {
                Chunk neighbourChunk = NeighbourChunks[3];
                if (neighbourChunk)
                {
                    neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(blockPosition.x, blockPosition.y, ChunkSizeXZ + 1)] = blockType;
                    neighbourChunk.isTerrainModified = true;
                    neighbourChunk.BuildMesh(jobHandles);
                    chunksToBuild.Add(neighbourChunk);
                }
            }

            NativeArray<JobHandle> njobHandles = new NativeArray<JobHandle>(jobHandles.ToArray(), Allocator.TempJob);
            JobHandle.CompleteAll(njobHandles);

            // build meshes
            foreach (Chunk tc in chunksToBuild)
            {
                tc.ApplyMesh();
            }

            // clear & dispose
            jobHandles.Clear();
            chunksToBuild.Clear();
            njobHandles.Dispose();

            UpdateNeighbourBlocks(blockPosition, 10);
        }

        /// <summary>
        /// Set array of blocks
        /// </summary>
        /// <param name="blockDatas">array containing data of each block you want to place</param>
        /// <param name="destroy">spawn destroy particle</param>
        public void SetBlocks(BlockData[] blockDatas, SetBlockSettings blockSettings)
        {
            List<JobHandle> jobHandles = new List<JobHandle>();
            List<Chunk> chunksToBuild = new List<Chunk>();

            bool[] neighboursToBuild = new bool[4];

            for (int i = 0; i < blockDatas.Length; i++)
            {
                BlockPosition blockPosition = blockDatas[i].position;
                BlockType blockType = blockDatas[i].blockType;

                SetBlockWithoutRebuild(blockPosition, blockType, blockSettings);

                // check neighbours
                if (blockPosition.x == ChunkSizeXZ)
                {
                    Chunk neighbourChunk = NeighbourChunks[0];
                    if (neighbourChunk)
                    {
                        neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(0, blockPosition.y, blockPosition.z)] = blockType;
                        neighbourChunk.isTerrainModified = true;
                        neighboursToBuild[0] = true;
                    }
                }
                else if (blockPosition.x == 1)
                {
                    Chunk neighbourChunk = NeighbourChunks[1];
                    if (neighbourChunk)
                    {
                        neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(ChunkSizeXZ + 1, blockPosition.y, blockPosition.z)] = blockType;
                        neighbourChunk.isTerrainModified = true;
                        neighboursToBuild[1] = true;
                    }
                }

                if (blockPosition.z == ChunkSizeXZ)
                {
                    Chunk neighbourChunk = NeighbourChunks[2];
                    if (neighbourChunk)
                    {
                        neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(blockPosition.x, blockPosition.y, 0)] = blockType;
                        neighbourChunk.isTerrainModified = true;
                        neighboursToBuild[2] = true;
                    }
                }
                else if (blockPosition.z == 1)
                {
                    Chunk neighbourChunk = NeighbourChunks[3];
                    if (neighbourChunk)
                    {
                        neighbourChunk.blocks[Utils.BlockPosition3DtoIndex(blockPosition.x, blockPosition.y, ChunkSizeXZ + 1)] = blockType;
                        neighbourChunk.isTerrainModified = true;
                        neighboursToBuild[3] = true;
                    }
                }

                UpdateNeighbourBlocks(blockDatas[i].position, 10);
            }

            // add current chunk
            BuildMesh(jobHandles);
            chunksToBuild.Add(this);

            for (int j = 0; j < 4; j++)
            {
                if (neighboursToBuild[j])
                {
                    Chunk tc = NeighbourChunks[j];
                    tc.BuildMesh(jobHandles);
                    chunksToBuild.Add(tc);
                }
            }

            NativeArray<JobHandle> njobHandles = new NativeArray<JobHandle>(jobHandles.ToArray(), Allocator.Temp);
            JobHandle.CompleteAll(njobHandles);

            // build meshes
            foreach (Chunk tc in chunksToBuild)
            {
                tc.ApplyMesh();
            }

            // clear & dispose
            jobHandles.Clear();
            chunksToBuild.Clear();

            njobHandles.Dispose();
        }

        #endregion

        #region // === Block updates === \\

        /// <summary>
        /// Schedule update event on current block and nearby blocks
        /// </summary>
        /// <param name="blockPos">current block position</param>
        /// <param name="ticks">ticks wait before calling update</param>
        private void UpdateNeighbourBlocks(BlockPosition blockPos, int ticks = 1)
        {
            World.ScheduleUpdate(this, blockPos, ticks);

            int x = blockPos.x;
            int y = blockPos.y;
            int z = blockPos.z;

            int[] neighbours = new int[6];
            BlockPosition[] positions = new BlockPosition[]
            {
                new BlockPosition(x, y + 1, z, out neighbours[0]),
                new BlockPosition(x, y - 1, z, out neighbours[1]),
                new BlockPosition(x + 1, y, z, out neighbours[2]),
                new BlockPosition(x - 1, y, z, out neighbours[3]),
                new BlockPosition(x, y, z + 1, out neighbours[4]),
                new BlockPosition(x, y, z - 1, out neighbours[5])
            };
            for (int i = 0; i < 6; i++)
            {
                Chunk chunk = neighbours[i] < 0 ? this : NeighbourChunks[neighbours[i]];
                World.ScheduleUpdate(chunk, positions[i], ticks);
            }
        }

        /// <summary>
        /// Called when block receives update
        /// </summary>
        public void OnBlockUpdate(BlockPosition position, params int[] args)
        {
            BlockType blockType = blocks[Utils.BlockPosition3DtoIndex(position.x, position.y, position.z)];

            int x = position.x;
            int y = position.y;
            int z = position.z;

            int[] neighbours = new int[4];
            BlockPosition[] sideBlocks = new BlockPosition[]
            {
                new BlockPosition(x, y, z + 1, out neighbours[0]),
                new BlockPosition(x, y, z - 1, out neighbours[1]),
                new BlockPosition(x + 1, y, z, out neighbours[2]),
                new BlockPosition(x - 1, y, z, out neighbours[3])
            };

            BlockPosition[] upDownBlocks = new BlockPosition[]
            {
                new BlockPosition(x, y + 1, z, false),
                new BlockPosition(x, y - 1, z, false)
            };

            Dictionary<BlockFace, BlockEventData> neighbourBlocks = new Dictionary<BlockFace, BlockEventData>();
            neighbourBlocks.Add(BlockFace.TOP, new BlockEventData(this, upDownBlocks[0], GetBlock(upDownBlocks[0])));
            neighbourBlocks.Add(BlockFace.BOTTOM, new BlockEventData(this, upDownBlocks[1], GetBlock(upDownBlocks[1]))); // down

            for (int i = 0; i < 4; i++)
            {
                Chunk chunk = neighbours[i] < 0 ? this : NeighbourChunks[neighbours[i]] == null ? this : NeighbourChunks[neighbours[i]];
                BlockPosition blockPos = sideBlocks[i];
                neighbourBlocks.Add((BlockFace)i + 2, new BlockEventData(chunk, blockPos, chunk.GetBlock(blockPos)));
            }

            World.InvokeBlockUpdateEvent(new BlockEventData(this, position, blockType), neighbourBlocks, args);
        }

        /// <summary>
        /// Add block to build queue and build it in next mesh update
        /// </summary>
        /// <param name="blockPos">position of block</param>
        /// <param name="blockType">type of block you want to place</param>
        public void AddBlockToBuildList(BlockPosition blockPos, BlockType blockType)
        {
            AddBlockToBuildList(new BlockData(blockType, blockPos));
        }
        /// <summary>
        /// Add block to build queue and build it in next mesh update
        /// </summary>
        /// <param name="data">data of block you want to place</param>
        public void AddBlockToBuildList(BlockData data)
        {
            if (!blocksToBuild.Contains(data))
                blocksToBuild.Add(data);
        }

        /// <summary>
        /// Add parameter to parameter queue and add it in next mesh update
        /// </summary>
        /// <param name="param">parameter you want to set</param>
        /// <param name="value">value of parameter</param>
        /// <param name="overrideIfExists">override value if queue contains parameter of same type</param>
        public void AddParameterToList(BlockParameter param, short value, bool overrideIfExists = true)
        {
            if (!parametersToAdd.ContainsKey(param))
                parametersToAdd.Add(param, value);
            else if (overrideIfExists)
                parametersToAdd[param] = value;
        }

        /// <summary>
        /// Listener for World build update timer
        /// </summary>
        private void BuildBlocks()
        {
            // set parameters
            foreach (var param in parametersToAdd)
            {
                SetParameters(param.Key, param.Value);
            }

            parametersToAdd.Clear();

            // update blocks
            if (blocksToBuild.Count > 0)
            {
                BlockData[] datas = blocksToBuild.ToArray();
                blocksToBuild.Clear();
                SetBlocks(datas, SetBlockSettings.VANISH);
            }
        }

        #endregion

        /// <summary>
        /// Save block data
        /// </summary>
        private void SaveDataInWorldDictionary()
        {
            return;

            if (isTerrainModified && blocks.IsCreated)
            {
                // TODO: save parameters
                //NativeArray<BlockParameter> blockParameterKeys = blockParameters.GetKeyArray(Allocator.Temp);
                //NativeArray<short> blockParameterValues = blockParameters.GetValueArray(Allocator.Temp);
                ChunkSaveData data = new ChunkSaveData(blocks.ToArray());

                SerializableVector2Int serializableChunkPos = SerializableVector2Int.FromVector2Int(ChunkPosition);
                // add new key or update existing data
                if (World.WorldSave.savedChunks.ContainsKey(serializableChunkPos))
                    World.WorldSave.savedChunks[serializableChunkPos] = data;
                else
                    World.WorldSave.savedChunks.Add(serializableChunkPos, data);

                //blockParameterKeys.Dispose();
                //blockParameterValues.Dispose();
            }
        }

        public void CreateBiomeTexture()
        {
            StartCoroutine(nameof(ColorDataCoroutine));
        }

        private IEnumerator ColorDataCoroutine()
        {
            if (biomeColorsTexture == null)
            {
                biomeColorsTexture = new Texture2D(FixedChunkSizeXZ, FixedChunkSizeXZ, TextureFormat.RGBA32, false);
                biomeColorsTexture.filterMode = FilterMode.Bilinear;
                biomeColorsTexture.wrapMode = TextureWrapMode.Clamp;
            }

            NativeArray<Color> biomeColors = new NativeArray<Color>(World.GetBiomeColors(), Allocator.TempJob);
            CreateBiomeColorData job = new CreateBiomeColorData()
            {
                biomeColors = biomeColors,
                biomes = biomeTypes,
                colors = biomeColorsTexture.GetRawTextureData<Color32>()
            };

            JobHandle handle = job.Schedule();
            yield return new WaitUntil(() => handle.IsCompleted);
            handle.Complete();

            biomeColorsTexture.Apply();
            blockMeshFilter.GetComponent<MeshRenderer>().material.SetTexture("_BiomeTexture", biomeColorsTexture);
            plantsMeshFilter.GetComponent<MeshRenderer>().material.SetTexture("_BiomeTexture", biomeColorsTexture);

            biomeColors.Dispose();
        }
    }
}