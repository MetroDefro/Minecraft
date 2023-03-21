using System.Collections.Generic;
using UnityEngine;
using System.Threading;

public class Chunk
{
    public Queue<VoxelMode> modifications = new Queue<VoxelMode>();
    public Vector3 Position;

    private GameObject chunkObject;
    private MeshRenderer meshRenderer;
    private MeshFilter meshFilter;

    private ChunkCoord Coord;
    private int vertexIndex = 0;
    private byte[,,] voxelMapBlockTypes = new byte[VoxelData.ChunkWidth, VoxelData.ChunkHeight, VoxelData.ChunkWidth];
    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private List<int> transparentTriangles = new List<int>();
    private List<Vector2> uvs = new List<Vector2>();

    private World world;

    private bool isActive;
    private bool isVoxelMapPopulated = false;
    private bool threadLocked = false;

    public bool IsActive
    {
        get { return isActive; }
        set 
        {
            isActive = value;
            if(chunkObject != null)
                chunkObject.SetActive(value); 
        }
    }

    public bool IsEditable { get => isVoxelMapPopulated || threadLocked; }

    public Chunk(ChunkCoord coord, World world, Material material, Material transparentMaterial, bool generateOnLoad)
    {
        this.Coord = coord;
        this.world = world;
        isActive = true;

        if(generateOnLoad)
            Init(material, transparentMaterial);
    }

    public void Init(Material material, Material transparentMaterial)
    {
        chunkObject = new GameObject();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshFilter = chunkObject.AddComponent<MeshFilter>();

        meshRenderer.materials = new Material[] { material, transparentMaterial };
        chunkObject.transform.SetParent(world.transform);
        chunkObject.transform.position = new Vector3(Coord.x * VoxelData.ChunkWidth, 0f, Coord.z * VoxelData.ChunkWidth);
        chunkObject.name = "Chunk " + Coord.x + ", " + Coord.z;

        Thread PopulateThread = new Thread(new ThreadStart(PopulateVoxelMap));
        PopulateThread.Start();

        Position = chunkObject.transform.position;

        UpdateChunk();
    }

    public byte GetVoxelFromGlobalVector3(Vector3 pos)
    {
        int xCheck = Mathf.FloorToInt(pos.x);
        int yCheck = Mathf.FloorToInt(pos.y);
        int zCheck = Mathf.FloorToInt(pos.z);

        xCheck -= Mathf.FloorToInt(Position.x);
        zCheck -= Mathf.FloorToInt(Position.z);

        return voxelMapBlockTypes[xCheck, yCheck, zCheck];
    }

    #region private method
    private void PopulateVoxelMap()
    {
        for (int y = 0; y < VoxelData.ChunkHeight; y++)
        {
            for (int x = 0; x < VoxelData.ChunkWidth; x++)
            {
                for (int z = 0; z < VoxelData.ChunkWidth; z++)
                {
                    // Get Block Type
                    voxelMapBlockTypes[x, y, z] = world.GetVoxelBlockType(new Vector3(x, y, z) + Position);
                }
            }
        }

        // UpdateChunk();
        isVoxelMapPopulated = true;
    }

    public void StartUpdateChunk()
    {
        Thread updateThread = new Thread(new ThreadStart(UpdateChunk));
        updateThread.Start();
    }

    private void UpdateChunk()
    {
        threadLocked = true;

        // If there is something to be modified, set up the voxel block type of the that's position as the id.
        while (modifications.Count > 0)
        {
            VoxelMode v = modifications.Dequeue();
            Vector3 pos = v.position -= Position;
            voxelMapBlockTypes[(int)pos.x, (int)pos.y, (int)pos.z] = v.id;
        }

        ClearMeshData();

        for (int y = 0; y < VoxelData.ChunkHeight; y++)
        {
            for (int x = 0; x < VoxelData.ChunkWidth; x++)
            {
                for (int z = 0; z < VoxelData.ChunkWidth; z++)
                {
                    if (world.BlockTypes[voxelMapBlockTypes[x, y, z]].isSolid)
                        UpdateMeshData(new Vector3(x, y, z));
                }
            }
        }

        lock (world.chunksToDraw)
        {
            world.chunksToDraw.Enqueue(this);
        }
        
        threadLocked = false;
    }

    private void ClearMeshData()
    {
        vertexIndex = 0;
        vertices.Clear();
        triangles.Clear();
        transparentTriangles.Clear();
        uvs.Clear();
    }

    private void UpdateMeshData(Vector3 pos)
    {
        byte blockID = voxelMapBlockTypes[(int)pos.x, (int)pos.y, (int)pos.z];
        bool isTransparent = world.BlockTypes[blockID].isTransparent;

        for (int p = 0; p < 6; p++)
        {
            // If the face - 1 voxel is not transparent, there is no need to draw it.
            if (CheckVoxel(pos + VoxelData.FaceChecks[p]))
            {
                for (int i = 0; i < 4; i++)
                    vertices.Add(pos + VoxelData.VoxelVerts[VoxelData.VoxelTris[p, i]]);

                AddTexture(world.BlockTypes[blockID].GetTextureID(p));

                if (!isTransparent)
                {
                    triangles.Add(vertexIndex);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex + 2);
                    triangles.Add(vertexIndex + 2);
                    triangles.Add(vertexIndex + 1);
                    triangles.Add(vertexIndex + 3);
                } else
                {
                    transparentTriangles.Add(vertexIndex);
                    transparentTriangles.Add(vertexIndex + 1);
                    transparentTriangles.Add(vertexIndex + 2);
                    transparentTriangles.Add(vertexIndex + 2);
                    transparentTriangles.Add(vertexIndex + 1);
                    transparentTriangles.Add(vertexIndex + 3);
                }

                vertexIndex += 4;
            }
        }
    }

    public void CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();

        mesh.subMeshCount = 2;
        mesh.SetTriangles(triangles.ToArray(), 0);
        mesh.SetTriangles(transparentTriangles.ToArray(), 1);

        mesh.uv = uvs.ToArray();

        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
    }

    private bool IsVoxelInChunk(int x, int y, int z) => x >= 0 && x < VoxelData.ChunkWidth && y >= 0 && y < VoxelData.ChunkHeight && z >= 0 && z < VoxelData.ChunkWidth;

    public void EditVoxel(Vector3 pos, byte newID)
    {
        int xCheck = Mathf.FloorToInt(pos.x);
        int yCheck = Mathf.FloorToInt(pos.y);
        int zCheck = Mathf.FloorToInt(pos.z);

        xCheck -= Mathf.FloorToInt(chunkObject.transform.position.x);
        zCheck -= Mathf.FloorToInt(chunkObject.transform.position.z);

        voxelMapBlockTypes[xCheck, yCheck, zCheck] = newID;

        UpdateSurroundingVoxels(xCheck, yCheck, zCheck);

        UpdateChunk();
    }

    private void UpdateSurroundingVoxels(int x, int y, int z)
    {
        Vector3 thisVoxel = new Vector3(x, y, z);

        for (int p = 0; p < 6; p++)
        {
            Vector3 currentVoxel = thisVoxel + VoxelData.FaceChecks[p];

            // When the added voxel affects other chunks, that chunk also needs to be updated.
            if (!IsVoxelInChunk((int)currentVoxel.x, (int)currentVoxel.y, (int)currentVoxel.z))
            {
                world.GetChunkFromVector3(currentVoxel + Position).UpdateChunk();
            }
        }
    }

    private bool CheckVoxel(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x);
        int y = Mathf.FloorToInt(pos.y);
        int z = Mathf.FloorToInt(pos.z);

        if (!IsVoxelInChunk(x, y, z))
            return world.CheckIfVoxelTransparent(pos + Position);

        return world.BlockTypes[voxelMapBlockTypes[x, y, z]].isTransparent;
    }

    private void AddTexture(int textureID)
    {
        float y = textureID / VoxelData.TextureAtlassWidth;
        float x = textureID - (y * VoxelData.TextureAtlassWidth);

        x *= VoxelData.NormalizedTextureAtlassWidth;
        y *= VoxelData.NormalizedTextureAtlassHeight;

        y = 1f - y - VoxelData.NormalizedTextureAtlassHeight;

        uvs.Add(new Vector2(x, y));
        uvs.Add(new Vector2(x, y + VoxelData.NormalizedTextureAtlassHeight));
        uvs.Add(new Vector2(x + VoxelData.NormalizedTextureAtlassWidth, y));
        uvs.Add(new Vector2(x + VoxelData.NormalizedTextureAtlassWidth, y + VoxelData.NormalizedTextureAtlassHeight));
    }

    #endregion
}

public class ChunkCoord
{
    public int x;
    public int z;

    public ChunkCoord()
    {
        x = 0;
        z = 0;
    }

    public ChunkCoord(int x, int z)
    {
        this.x = x;
        this.z = z;
    }

    public ChunkCoord(Vector3 pos)
    {
        int xCheck = Mathf.FloorToInt(pos.x);
        int zCheck = Mathf.FloorToInt(pos.z);

        x = xCheck / VoxelData.ChunkWidth;
        z = zCheck / VoxelData.ChunkWidth;
    }

    public bool Equals (ChunkCoord other)
    {
        if (other == null)
            return false;
        else if (other.x == x && other.z == z)
            return true;
        else
            return false;
    }
}