using System.Collections.Generic;
using UnityEngine;

public class EndlessLevelManager : MonoBehaviour
{
    #region [Variables/Fields]
    [Header("Grid Settings")]
    [SerializeField] private GameObject[] platformPrefabs;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float platformWidth = 10f;  // ความกว้างแกน X
    [SerializeField] private float platformLength = 10f; // ความยาวแกน Z
    [SerializeField, Tooltip("ระยะการมองเห็น: 1 = สร้าง 3x3, 2 = สร้าง 5x5")] 
    private int viewDistance = 1; 

    private Vector2Int currentPlayerGridPos;
    private Dictionary<Vector2Int, GameObject> activePlatforms = new Dictionary<Vector2Int, GameObject>();
    #endregion

    #region [Unity Methods]
    private void Start()
    {
        if (playerTransform != null)
        {
            currentPlayerGridPos = GetGridPosition(playerTransform.position);
            UpdateGrid();
        }
    }

    private void Update()
    {
        if (playerTransform == null) return;

        Vector2Int newGridPos = GetGridPosition(playerTransform.position);
        
        // ตรวจสอบว่าผู้เล่นขยับเปลี่ยน Grid (ช่อง) หรือยัง
        if (newGridPos != currentPlayerGridPos)
        {
            currentPlayerGridPos = newGridPos;
            UpdateGrid();
        }
    }
    #endregion

    #region [Private Methods]
    private Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        // แปลง World Position ของผู้เล่นให้เป็นพิกัด Grid เช่น (0,0), (1,0)
        int x = Mathf.RoundToInt(worldPosition.x / platformWidth);
        int z = Mathf.RoundToInt(worldPosition.z / platformLength);
        return new Vector2Int(x, z);
    }

    private void UpdateGrid()
    {
        List<Vector2Int> platformsToKeep = new List<Vector2Int>();

        // 1. สร้าง Platform ใหม่ในระยะ 3x3 (หรือตาม viewDistance)
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                Vector2Int gridPos = new Vector2Int(currentPlayerGridPos.x + x, currentPlayerGridPos.y + z);
                platformsToKeep.Add(gridPos);

                // ถ้าช่องนี้ยังไม่เคยถูกสร้าง ให้สร้างใหม่
                if (!activePlatforms.ContainsKey(gridPos))
                {
                    SpawnPlatform(gridPos);
                }
            }
        }

        // 2. ทำลาย Platform ที่อยู่นอกระยะมองเห็น
        List<Vector2Int> platformsToRemove = new List<Vector2Int>();
        foreach (var kvp in activePlatforms)
        {
            if (!platformsToKeep.Contains(kvp.Key))
            {
                platformsToRemove.Add(kvp.Key);
            }
        }

        foreach (Vector2Int pos in platformsToRemove)
        {
            Destroy(activePlatforms[pos]);
            activePlatforms.Remove(pos);
        }
    }

    private void SpawnPlatform(Vector2Int gridPos)
    {
        // คำนวณตำแหน่งเกิดในโลก (World Position) จากพิกัด Grid
        Vector3 spawnPosition = new Vector3(gridPos.x * platformWidth, 0f, gridPos.y * platformLength);
        int prefabIndex = Random.Range(0, platformPrefabs.Length);
        
        // บังคับให้จุดเริ่มต้น (0,0) เป็น Prefab ชิ้นแรกเสมอ (เพื่อความชัวร์ว่าจุดเกิดปลอดภัย)
        if (gridPos == Vector2Int.zero)
        {
            prefabIndex = 0; 
        }

        GameObject platform = Instantiate(platformPrefabs[prefabIndex], spawnPosition, Quaternion.identity, transform);
        platform.name = $"Platform_{gridPos.x}_{gridPos.y}"; // ตั้งชื่อให้ดูง่ายใน Hierarchy
        
        // เก็บลงใน Dictionary เพื่อไว้เช็คและทำลายทีหลัง
        activePlatforms.Add(gridPos, platform);
    }
    #endregion
}
