using UnityEngine;
using UnityEngine.InputSystem;

public class BlobInput : MonoBehaviour
{
    [Header("max droplet amount")]
    [Range(1, 64)]
    public int maxBlobs = 64;

    [Header("clikc area")]
    public float spread = 1.5f;

    [Header("new droplet radius")]
    [Range(0.01f, 2f)]
    public float baseRadius = 0.22f;

    [Header("点击靠近已有水滴给最近那滴加量 不是新建")]
    public float clickFeedDistance = 0.45f;

    [Header("每次点击加多少量")]
    public float clickRadiusIncrement = 0.10f;

    [Header("right click to clear all the droplet")]
    public bool rightClickClear = true;

    private const int Max = 64;

    private int _count = 0;

    // 只存“出生位置”（visual 里只在新增时读取一次）
    private Vector3[] _spawnPos = new Vector3[Max];

    // 目标半径（visual 每帧读取，用于 SmoothDamp/融合等）
    private float[] _targetRadius = new float[Max];

    private Vector3[] _impulse = new Vector3[Max];

    // 清空标记：visual ConsumeClearFlag() 后清掉
    private bool _clearFlag = false;

    public int Count => _count;

   
    public Vector3 GetSpawnPos(int i)
    {
        if (i < 0 || i >= _count) return Vector3.zero;
        return _spawnPos[i];
    }

    // visual：每帧拿目标半径
    public float GetTargetRadius(int i)
    {
        if (i < 0 || i >= _count) return 0f;
        return _targetRadius[i];
    }

   
    public Vector3 ConsumeImpulse(int i)
    {
        if (i < 0 || i >= _count) return Vector3.zero;
        Vector3 v = _impulse[i];
        _impulse[i] = Vector3.zero;
        return v;
    }

   
    public bool ConsumeClearFlag()
    {
        if (!_clearFlag) return false;
        _clearFlag = false;
        return true;
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 sp = mouse.position.ReadValue();
            Vector3 p = ScreenToPoint(sp);
            ClickAddOrFeed(p);
        }

        if (rightClickClear && mouse.rightButton.wasPressedThisFrame)
        {
            ClearAll();
        }
    }

    // 屏幕坐标 -> 数据坐标
    private Vector3 ScreenToPoint(Vector2 screenPos)
    {
        float u = Mathf.Clamp01(screenPos.x / Screen.width);
        float v = Mathf.Clamp01(screenPos.y / Screen.height);

        float aspect = Screen.width / (float)Screen.height;

        float x = (u * 2f - 1f) * spread * aspect;
        float y = (v * 2f - 1f) * spread;

        return new Vector3(x, y, 0f);
    }

    // 点击：靠近就给最近那滴，否则新建
    private void ClickAddOrFeed(Vector3 clickPos)
    {
        int limit = Mathf.Min(maxBlobs, Max);

        if (_count > 0)
        {
            int nearest = -1;
            float best = float.PositiveInfinity;

            for (int i = 0; i < _count; i++)
            {
                float d = Vector3.Distance(_spawnPos[i], clickPos);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }

            if (nearest >= 0 && best <= clickFeedDistance)
            {
                _targetRadius[nearest] += clickRadiusIncrement;

                
                Vector3 dir = clickPos - _spawnPos[nearest];
                if (dir.sqrMagnitude > 1e-6f)
                {
                    _impulse[nearest] += dir.normalized * 0.8f;
                }
                return;
            }
        }

        // 新建
        if (_count >= limit)
        {
            // 满了就覆盖最后一个
            int idx = limit - 1;
            InitBlob(idx, clickPos, baseRadius);
            return;
        }

        InitBlob(_count, clickPos, baseRadius);
        _count++;
    }

    private void InitBlob(int i, Vector3 pos, float r)
    {
        _spawnPos[i] = pos;
        _targetRadius[i] = r;
        _impulse[i] = Vector3.zero;
    }

    private void ClearAll()
    {
        _count = 0;
        _clearFlag = true;
    }
}
