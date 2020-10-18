 # Voxel Terrain Generator
Unity Version: **2020.1.6f1**

Features:
* Procedurally generated terrain
* Daylight cycle
* Terrain modification
* Multithreading  
* Saving/Loading terrain 
* Items, tools, weapons
* Inventory system (WIP)
* Menu, async scene loading
* Entities (WIP)

## Screenshots:
![Screenshot 0](https://michalczemierowski.github.io/img/screenshots/voxel_terrain_generator-0.jpg)
![Screenshot 0](https://michalczemierowski.github.io/img/screenshots/voxel_terrain_generator-1.jpg)
![Screenshot 0](https://michalczemierowski.github.io/img/screenshots/voxel_terrain_generator-2.jpg)

# Code examples
*Easy to configure event listeners*

> IBlockUpdateListener - called when neighbour block is placed/removed

```csharp
public class OnGrassBlockUpdate : MonoBehaviour, IBlockUpdateListener
{
    public BlockType GetBlockType()
    {
	    // Block type you want to listen for updates
        return BlockType.GRASS_BLOCK;
    }

    public void OnBlockUpdate(BlockEventData data, Dictionary<BlockFace, BlockEventData> neighbours, params int[] args)
    {
        // if above block is solid block
        if (WorldData.GetBlockState(neighbours[BlockFace.TOP].type) == BlockState.SOLID)
        {
            // replace current block with dirt in next update
            data.chunk.AddBlockToBuildList(data.position, BlockType.DIRT);
        }
    }
}
```

> IBlockArrayDestroyListener - called when block is removed

```csharp
public class OnAnyDestroy : MonoBehaviour, IBlockArrayDestroyListener
{
    public BlockType[] GetBlockTypes()
    {
        // register this event listener to all blocks
        return Utils.GetAllBlockTypes();
    }

    public void OnBlockDestroy(BlockEventData data, params int[] args)
    {
        BlockType type = data.blockType == BlockType.GRASS_BLOCK ? BlockType.DIRT : data.blockType;
        // instantiate particle at block position
        ParticleManager.InstantiateBlockDestroyParticle(ParticleType.BLOCK_DESTROY_PARTICLE, data.WorldPosition, type);
    }
}
```
