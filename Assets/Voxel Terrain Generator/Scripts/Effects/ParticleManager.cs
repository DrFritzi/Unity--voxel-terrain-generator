﻿using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelTG.Terrain;

/*
 * Michał Czemierowski
 * https://github.com/michalczemierowski
*/
namespace VoxelTG.Effects
{
    public class ParticleManager : MonoBehaviour
    {
        public static ParticleManager Instance;

        [SerializeField] private GameObject onBlockDestroyParticle;

        private static Dictionary<BlockType, Mesh> particleMeshes = new Dictionary<BlockType, Mesh>();

        private static int targetPoolSize = 32;
        private static Dictionary<ParticleType, Queue<ParticleSystem>> particlesPool = new Dictionary<ParticleType, Queue<ParticleSystem>>();

        // onBlockDestroyParticle == onBDP
        private static float onBDPuvSize = 0.25f;

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            else
                Instance = this;
        }

        private void Start()
        {
            foreach (ParticleType particleType in System.Enum.GetValues(typeof(ParticleType)))
            {
                particlesPool.Add(particleType, new Queue<ParticleSystem>());
            }

            StartCoroutine(BlockParticlesPoolCleaner(0.5f));
        }

        public static void InstantiateBlockDestroyParticle(ParticleType particleType, Vector3Int blockPosition, BlockType blockType = BlockType.AIR)
        {
            ParticleSystem particle = SpawnParticle(ParticleType.BLOCK_DESTROY_PARTICLE, blockPosition);

            if (blockType != BlockType.AIR)
            {
                ParticleSystemRenderer particleSystemRenderer = particle.GetComponent<ParticleSystemRenderer>();
                particleSystemRenderer.mesh = CreateBlockParticleMeh(blockType);
            }

            particle.Play();
            DestroyBlockParticle(particleType, particle, particle.main.duration);
        }

        private static ParticleSystem SpawnParticle(ParticleType type, Vector3Int blockPosition)
        {
            ParticleSystem result = null;
            Vector3 position = new Vector3(blockPosition.x + 0.5f, blockPosition.y, blockPosition.z + 0.5f);

            if (particlesPool[type].Count > 0)
            {
                result = particlesPool[type].Dequeue();
                result.transform.position = position;
                result.gameObject.SetActive(true);
                return result;
            }

            switch (type)
            {
                case ParticleType.BLOCK_DESTROY_PARTICLE:
                    result = Instantiate(Instance.onBlockDestroyParticle, new Vector3(blockPosition.x + 0.5f,
                        blockPosition.y, blockPosition.z + 0.5f), Quaternion.identity).GetComponent<ParticleSystem>();
                    break;
                case ParticleType.BLOCK_PLACE_PARTICLE:
                    result = Instantiate(Instance.onBlockDestroyParticle, new Vector3(blockPosition.x + 0.5f,
                        blockPosition.y, blockPosition.z + 0.5f), Quaternion.identity).GetComponent<ParticleSystem>();
                    break;
            }

            return result;
        }

        private static void DestroyBlockParticle(ParticleType type, ParticleSystem particle, float time)
        {
            Instance.StartCoroutine(DestroyBlockParticleEnumerator(type, particle, time));
        }

        private static Mesh CreateBlockParticleMeh(BlockType type)
        {
            Mesh particleMesh;
            Block block = WorldData.GetBlock(type);

            float offsetX = UnityEngine.Random.Range(0f, 1 - onBDPuvSize) / Terrain.Blocks.TilePos.textureSize;
            float offsetY = UnityEngine.Random.Range(0f, onBDPuvSize) / Terrain.Blocks.TilePos.textureSize;
            float size = onBDPuvSize / Terrain.Blocks.TilePos.textureSize;

            if (particleMeshes.ContainsKey(type))
            {
                particleMesh = particleMeshes[type];

                particleMesh.uv = new Vector2[]
                {
                    block.sidePos.uv0 + new float2(offsetX, offsetY),
                    block.sidePos.uv0 + new float2(offsetX, offsetY + size),
                    block.sidePos.uv0 + new float2(offsetX + size, offsetY + size),
                    block.sidePos.uv0 + new float2(offsetX + size, offsetY)
                };

                particleMesh.RecalculateNormals();

                return particleMeshes[type];
            }

            particleMesh = new Mesh();

            particleMesh.vertices = new Vector3[]
            {
                new Vector3(0,0,0),
                new Vector3(0,1,0),
                new Vector3(1,1,0),
                new Vector3(1,0,0)
            };
            particleMesh.uv = new Vector2[]
            {
                block.sidePos.uv0 + new float2(offsetX, offsetY),
                block.sidePos.uv0 + new float2(offsetX, offsetY + size),
                block.sidePos.uv0 + new float2(offsetX + size, offsetY + size),
                block.sidePos.uv0 + new float2(offsetX + size, offsetY)
            };
            particleMesh.triangles = new int[]
            {
                0,
                1,
                2,
                0,
                2,
                3
            };

            particleMesh.RecalculateNormals();

            particleMeshes.Add(type, particleMesh);
            return particleMesh;
        }

        #region IEnumerators

        private static IEnumerator DestroyBlockParticleEnumerator(ParticleType type, ParticleSystem particle, float time)
        {
            yield return new WaitForSecondsRealtime(time);
            particlesPool[type].Enqueue(particle);
            particle.gameObject.SetActive(false);
        }

        private static IEnumerator BlockParticlesPoolCleaner(float repeatTime)
        {
            var wait = new WaitForSecondsRealtime(repeatTime);
            while (true)
            {
                foreach (var queue in particlesPool.Values)
                {
                    if (queue.Count > targetPoolSize)
                        Destroy(queue.Dequeue());
                }

                yield return wait;
            }
        }

        #endregion
    }
}