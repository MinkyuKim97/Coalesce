using UnityEngine;
using UnityEngine.InputSystem;

public class BlobPainter : MonoBehaviour
{
    [Header("把挂了材质的 Quad 拖进来（推荐）")]
    public Renderer targetRenderer;

    [Header("不拖 Renderer 的话，就把材质拖这里")]
    public Material targetMaterial;

    [Header("最大水滴数量（不要超过 64）")]
    [Range(1, 64)]
    public int maxBlobs = 64;

    [Header("点击映射范围（屏幕0~1 -> shader空间 -spread~spread）")]
    public float spread = 1.5f;

    [Header("新水滴初始半径")]
    [Range(0.01f, 2f)]
    public float baseRadius = 0.22f;

    [Header("点击靠近已有水滴：给最近那滴加量（不是新建）")]
    public float clickFeedDistance = 0.45f;

    [Header("每次点击加多少“量”（目标半径增加，动画过渡）")]
    public float clickRadiusIncrement = 0.10f;

    [Header("半径动画平滑时间（越小越快）")]
    [Range(0.01f, 1f)]
    public float radiusSmoothTime = 0.12f;

    [Header("自动吸附范围")]
    public float attractRange = 1.1f;

    [Header("自动吸附强度")]
    public float attractStrength = 6.0f;

    [Header("位置阻尼（越大越黏稠越不抖）")]
    public float positionDamping = 5.0f;

    [Header("进入强吸附阶段距离")]
    public float mergeStartDistance = 0.35f;

    [Header("开始动画融合距离（很近时才开始融合过程）")]
    public float mergeBeginDistance = 0.18f;

    [Header("融合动画速度")]
    public float mergeSpeed = 2.8f;

    [Header("被吸收那滴缩小速度")]
    public float mergeShrinkSpeed = 4.5f;

    [Header("融合完成阈值（小于这个半径就删掉）")]
    public float mergeKillRadius = 0.02f;

    [Header("强吸附额外加速")]
    public float mergePullBoost = 10.0f;

    [Header("流动感：位置摆动幅度")]
    public float flowPosAmplitude = 0.06f;

    [Header("流动感：位置摆动速度")]
    public float flowPosSpeed = 2.2f;

    [Header("流动感：半径呼吸幅度（0.1=±10%）")]
    [Range(0f, 0.5f)]
    public float flowRadiusAmplitude = 0.10f;

    [Header("流动感：半径呼吸速度")]
    public float flowRadiusSpeed = 2.6f;

    [Header("右键清空")]
    public bool rightClickClear = true;

    private const int MaxShaderBlobs = 64;

    private static readonly int UseExternalID = Shader.PropertyToID("_UseExternal");
    private static readonly int BlobCountID = Shader.PropertyToID("_BlobCount");
    private static readonly int BlobsID = Shader.PropertyToID("_Blobs");

    private int _count = 0;
    private Vector3[] _anchor = new Vector3[MaxShaderBlobs];
    private Vector3[] _vel = new Vector3[MaxShaderBlobs];

    private float[] _radius = new float[MaxShaderBlobs];
    private float[] _targetRadius = new float[MaxShaderBlobs];
    private float[] _radiusVel = new float[MaxShaderBlobs];

    private float[] _seedA = new float[MaxShaderBlobs];
    private float[] _seedB = new float[MaxShaderBlobs];
    private float[] _seedC = new float[MaxShaderBlobs];

    private bool[] _isMerging = new bool[MaxShaderBlobs];
    private int[] _mergeDst = new int[MaxShaderBlobs];
    private float[] _mergeFinalRadius = new float[MaxShaderBlobs];

    private Vector4[] _upload = new Vector4[MaxShaderBlobs];
    private bool _dirty = true;

    private void Start()
    {
        // 优先从 Renderer 获取实例材质，避免改错共享材质
        if (targetRenderer != null)
        {
            targetMaterial = targetRenderer.material;
        }

        if (targetMaterial == null)
        {
            Debug.LogError("BlobPainter：没指定 targetRenderer 或 targetMaterial。把 Quad 的 Renderer 或材质拖进来。");
            enabled = false;
            return;
        }

        // 强制 shader 使用外部 blobs（由脚本喂数据）
        targetMaterial.SetFloat(UseExternalID, 1f);
        Upload();
    }

    private void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 sp = mouse.position.ReadValue();
                Vector3 p = ScreenToShaderPoint(sp);
                ClickAddOrFeed(p);
            }

            if (rightClickClear && mouse.rightButton.wasPressedThisFrame)
            {
                ClearAll();
            }
        }

        Simulate(Time.deltaTime);

        if (_dirty)
        {
            Upload();
            _dirty = false;
        }
    }

    // 屏幕坐标 -> shader 坐标（带 aspect 修正，宽屏不偏）
    private Vector3 ScreenToShaderPoint(Vector2 screenPos)
    {
        float u = Mathf.Clamp01(screenPos.x / Screen.width);
        float v = Mathf.Clamp01(screenPos.y / Screen.height);

        float aspect = Screen.width / (float)Screen.height;

        float x = (u * 2f - 1f) * spread * aspect;
        float y = (v * 2f - 1f) * spread;

        return new Vector3(x, y, 0f);
    }

    // 点击：靠近就“喂给”最近那滴，否则新建
    private void ClickAddOrFeed(Vector3 clickPos)
    {
        int limit = Mathf.Min(maxBlobs, MaxShaderBlobs);

        if (_count > 0)
        {
            int nearest = -1;
            float best = float.PositiveInfinity;

            for (int i = 0; i < _count; i++)
            {
                if (_isMerging[i]) continue;

                float d = Vector3.Distance(_anchor[i], clickPos);
                if (d < best)
                {
                    best = d;
                    nearest = i;
                }
            }

            if (nearest >= 0 && best <= clickFeedDistance)
            {
                _targetRadius[nearest] += clickRadiusIncrement;

                Vector3 dir = clickPos - _anchor[nearest];
                if (dir.sqrMagnitude > 1e-6f)
                {
                    _vel[nearest] += dir.normalized * 0.8f;
                }

                _dirty = true;
                return;
            }
        }

        if (_count >= limit)
        {
            // 满了就覆盖最后一滴（最简单稳定）
            int idx = limit - 1;
            InitBlob(idx, clickPos, baseRadius);
            _dirty = true;
            return;
        }

        InitBlob(_count, clickPos, baseRadius);
        _count++;
        _dirty = true;
    }

    private void InitBlob(int i, Vector3 anchorPos, float r)
    {
        _anchor[i] = anchorPos;
        _vel[i] = Vector3.zero;

        _radius[i] = r;
        _targetRadius[i] = r;
        _radiusVel[i] = 0f;

        _seedA[i] = Random.Range(0f, 1000f);
        _seedB[i] = Random.Range(0f, 1000f);
        _seedC[i] = Random.Range(0f, 1000f);

        _isMerging[i] = false;
        _mergeDst[i] = -1;
        _mergeFinalRadius[i] = 0f;
    }

    private void Simulate(float dt)
    {
        if (_count <= 0) return;

        // 1) 半径平滑（连续动画）
        for (int i = 0; i < _count; i++)
        {
            _radius[i] = Mathf.SmoothDamp(_radius[i], _targetRadius[i], ref _radiusVel[i], radiusSmoothTime, Mathf.Infinity, dt);
        }

        // 2) 吸附 + 触发融合
        for (int i = 0; i < _count; i++)
        {
            if (_isMerging[i]) continue;

            for (int j = i + 1; j < _count; j++)
            {
                if (_isMerging[j]) continue;

                Vector3 d = _anchor[j] - _anchor[i];
                float dist = d.magnitude;
                if (dist < 1e-5f) continue;

                if (dist <= attractRange)
                {
                    Vector3 dir = d / dist;
                    float t = 1f - (dist / attractRange);
                    float force = attractStrength * t;

                    if (dist <= mergeStartDistance)
                    {
                        force += mergePullBoost * (1f - dist / mergeStartDistance);
                    }

                    _vel[i] += dir * force * dt;
                    _vel[j] -= dir * force * dt;

                    if (dist <= mergeBeginDistance)
                    {
                        BeginMerge(i, j);
                    }
                }
            }
        }

        // 3) 融合动画：src 贴近 + 缩小；dst 变大
        for (int i = 0; i < _count; i++)
        {
            if (!_isMerging[i]) continue;

            int dst = _mergeDst[i];
            if (dst < 0 || dst >= _count || dst == i)
            {
                _isMerging[i] = false;
                continue;
            }

            float lerpPos = 1f - Mathf.Exp(-mergeSpeed * dt);
            _anchor[i] = Vector3.Lerp(_anchor[i], _anchor[dst], lerpPos);

            _targetRadius[i] = Mathf.Max(0f, _targetRadius[i] - mergeShrinkSpeed * dt);

            float lerpRad = 1f - Mathf.Exp(-mergeSpeed * dt);
            _targetRadius[dst] = Mathf.Lerp(_targetRadius[dst], _mergeFinalRadius[i], lerpRad);

            if (_radius[i] <= mergeKillRadius || _targetRadius[i] <= mergeKillRadius)
            {
                RemoveBlob(i);
                i--;
                _dirty = true;
            }
        }

        // 4) 阻尼 + 位置更新
        float damp = Mathf.Clamp01(1f - positionDamping * dt);
        for (int i = 0; i < _count; i++)
        {
            _vel[i] *= damp;
            _anchor[i] += _vel[i] * dt;
        }

        _dirty = true;
    }

    private void BeginMerge(int a, int b)
    {
        if (_isMerging[a] || _isMerging[b]) return;

        // 大的做 dst，小的做 src
        int dst = (_targetRadius[a] >= _targetRadius[b]) ? a : b;
        int src = (dst == a) ? b : a;

        // 体积守恒：r^3 相加
        float rDst = Mathf.Max(0.0001f, _targetRadius[dst]);
        float rSrc = Mathf.Max(0.0001f, _targetRadius[src]);
        float vDst = rDst * rDst * rDst;
        float vSrc = rSrc * rSrc * rSrc;
        float rFinal = Mathf.Pow(vDst + vSrc, 1f / 3f);

        _isMerging[src] = true;
        _mergeDst[src] = dst;
        _mergeFinalRadius[src] = rFinal;

        // 给 dst 一点速度，观感更像吸过去
        Vector3 dir = (_anchor[src] - _anchor[dst]);
        if (dir.sqrMagnitude > 1e-6f)
        {
            _vel[dst] += dir.normalized * 0.6f;
        }
    }

    private void RemoveBlob(int index)
    {
        int last = _count - 1;
        if (index < 0 || index > last) return;

        if (index != last)
        {
            _anchor[index] = _anchor[last];
            _vel[index] = _vel[last];

            _radius[index] = _radius[last];
            _targetRadius[index] = _targetRadius[last];
            _radiusVel[index] = _radiusVel[last];

            _seedA[index] = _seedA[last];
            _seedB[index] = _seedB[last];
            _seedC[index] = _seedC[last];

            _isMerging[index] = _isMerging[last];
            _mergeDst[index] = _mergeDst[last];
            _mergeFinalRadius[index] = _mergeFinalRadius[last];

            // 修正 mergeDst 指针
            for (int i = 0; i < _count; i++)
            {
                if (_isMerging[i] && _mergeDst[i] == last)
                {
                    _mergeDst[i] = index;
                }
            }
        }

        _count--;
    }

    private void ClearAll()
    {
        _count = 0;
        _dirty = true;
    }

    // 上传：anchor + 流动偏移；半径 * 呼吸
    private void Upload()
    {
        int count = Mathf.Clamp(_count, 0, Mathf.Min(maxBlobs, MaxShaderBlobs));
        targetMaterial.SetInt(BlobCountID, count);

        float t = Time.time;

        for (int i = 0; i < count; i++)
        {
            float ox = Mathf.Sin(t * flowPosSpeed + _seedA[i]);
            float oy = Mathf.Sin(t * (flowPosSpeed * 1.17f) + _seedB[i]);
            Vector3 flowOffset = new Vector3(ox, oy, 0f) * flowPosAmplitude;

            float breath = 1f + flowRadiusAmplitude * Mathf.Sin(t * flowRadiusSpeed + _seedC[i]);
            float r = Mathf.Max(0.0001f, _radius[i] * breath);

            Vector3 p = _anchor[i] + flowOffset;
            _upload[i] = new Vector4(p.x, p.y, p.z, r);
        }

        targetMaterial.SetVectorArray(BlobsID, _upload);
    }
}
