using System.Collections.Generic;
using UnityEngine;

public class SpatialHashGrid
{
    private Dictionary<int, List<int>> _cells = new Dictionary<int, List<int>>();
    private float _cellSize;

    public SpatialHashGrid(float cellSize)
    {
        _cellSize = cellSize;
    }

    // تحويل موضع لمفتاح خلية
    private int Hash(int x, int y, int z)
    {
        return x * 73856093 ^ y * 19349663 ^ z * 83492791;
    }

    private int GetKey(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x / _cellSize);
        int y = Mathf.FloorToInt(pos.y / _cellSize);
        int z = Mathf.FloorToInt(pos.z / _cellSize);
        return Hash(x, y, z);
    }

    // بناء الشبكة من مواضع الجسيمات
    public void Build(Vector3[] positions)
    {
        _cells.Clear();
        for (int i = 0; i < positions.Length; i++)
        {
            int key = GetKey(positions[i]);
            if (!_cells.ContainsKey(key))
                _cells[key] = new List<int>();
            _cells[key].Add(i);
        }
    }

    // إيجاد الجيران فقط (بدل كل الجسيمات)
    public List<int> GetNeighbors(Vector3 pos)
    {
        var neighbors = new List<int>();
        int cx = Mathf.FloorToInt(pos.x / _cellSize);
        int cy = Mathf.FloorToInt(pos.y / _cellSize);
        int cz = Mathf.FloorToInt(pos.z / _cellSize);

        // فحص 27 خلية مجاورة فقط بدل الكل
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int key = Hash(cx + dx, cy + dy, cz + dz);
                    if (_cells.TryGetValue(key, out var list))
                        neighbors.AddRange(list);
                }
        return neighbors;
    }
}