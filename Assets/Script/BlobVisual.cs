using UnityEngine;

public class BlobVisual : MonoBehaviour
{
    [Header("数据来源（拖 BlobInput 进来）")]
    public BlobInput source;

    [Header("把挂了材质的 Quad 拖进来（推荐）")]
    public Renderer targetRenderer;

    [Header("不拖 Renderer 的话，就把材质拖这里")]
    public Material targetMaterial;

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

    private const int Max = 64;

    private static readonly int UseExternalID = Shader.PropertyToID("_UseExternal");
    private static readonly int BlobCountID = Shader.PropertyToID("_BlobCount");
    private static readonly int BlobsID = Shader.PropertyToID("_Blobs");

    private int _count = 0;

    // 视觉自己的“可视位置/速度”
    private Vector3[] _pos = new Vector3[Max];
    private Vector3[] _vel = new Vector3[Max];

    // 半径：当前 / 目标 / 平滑速度
    private float[] _radius = new float[Max];
    private float[] _targetRadius = new float[Max];
    private float[] _radiusVel = new float[Max];

    // 融合状态（src -> dst）
    private bool[] _isMerging = new bool[Max];
    private int[] _mergeDst = new int[Max];
    private float[] _mergeFinalRadius = new float[Max];

    // 流动种子
    private float[] _seedA = new float[Max];
    private float[] _seedB = new float[Max];
    private float[] _seedC = new float[Max];

    private Vector4[] _upload = new Vector4[Max];
    private bool _ok = false;

    private void Start()
    {
        // 优先从 Renderer 获取实例材质，避免改到共享材质
        if (targetRenderer != null)
        {
            targetMaterial = targetRenderer.material;
        }

        if (targetMaterial == null)
        {
            Debug.LogError("BlobVisual：没指定 targetRenderer 或 targetMaterial。");
            enabled = false;
            return;
        }

        // 强制 shader 使用外部 blobs（由脚本喂数据）
        targetMaterial.SetFloat(UseExternalID, 1f);
        _ok = true;
    }

    private void Update()
    {
        if (!_ok) return;
        if (source == null) return;

        // 处理清空
        if (source.ConsumeClearFlag())
        {
            _count = 0;
            targetMaterial.SetInt(BlobCountID, 0);
            return;
        }

        // 只在“新增”时，从 source 拿出生位置；平时不覆盖 _pos
        int srcCount = Mathf.Clamp(source.Count, 0, Max);

        if (srcCount > _count)
        {
            for (int i = _count; i < srcCount; i++)
            {
                Vector3 spawn = source.GetSpawnPos(i);
                float tr = source.GetTargetRadius(i);

                _pos[i] = spawn;
                _vel[i] = Vector3.zero;

                _radius[i] = tr;
                _targetRadius[i] = tr;
                _radiusVel[i] = 0f;

                _isMerging[i] = false;
                _mergeDst[i] = -1;
                _mergeFinalRadius[i] = 0f;

                _seedA[i] = Random.Range(0f, 1000f);
                _seedB[i] = Random.Range(0f, 1000f);
                _seedC[i] = Random.Range(0f, 1000f);
            }

            _count = srcCount;
        }
        else if (srcCount < _count)
        {
            // 源减少（比如你未来想外部删点），这里直接截断
            _count = srcCount;
        }

        // 同步目标半径 + 消费 impulse（但不重置位置）
        for (int i = 0; i < _count; i++)
        {
            _targetRadius[i] = source.GetTargetRadius(i);

            Vector3 imp = source.ConsumeImpulse(i);
            if (imp.sqrMagnitude > 1e-6f)
            {
                _vel[i] += imp;
            }
        }

        Simulate(Time.deltaTime);
        UploadToShader();
    }

    private void Simulate(float dt)
    {
        if (_count <= 0) return;

        // 1) 半径平滑
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

                Vector3 d = _pos[j] - _pos[i];
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

        // 3) 融合动画：src 贴近 dst 并缩小；dst 变大
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
            _pos[i] = Vector3.Lerp(_pos[i], _pos[dst], lerpPos);

            _targetRadius[i] = Mathf.Max(0f, _targetRadius[i] - mergeShrinkSpeed * dt);

            float lerpRad = 1f - Mathf.Exp(-mergeSpeed * dt);
            _targetRadius[dst] = Mathf.Lerp(_targetRadius[dst], _mergeFinalRadius[i], lerpRad);

            if (_radius[i] <= mergeKillRadius || _targetRadius[i] <= mergeKillRadius)
            {
                RemoveVisual(i);
                i--;
            }
        }

        // 4) 阻尼 + 更新位置
        float damp = Mathf.Clamp01(1f - positionDamping * dt);
        for (int i = 0; i < _count; i++)
        {
            _vel[i] *= damp;
            _pos[i] += _vel[i] * dt;
        }
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
        Vector3 dir = (_pos[src] - _pos[dst]);
        if (dir.sqrMagnitude > 1e-6f)
        {
            _vel[dst] += dir.normalized * 0.6f;
        }
    }

    private void RemoveVisual(int index)
    {
        int last = _count - 1;
        if (index < 0 || index > last) return;

        if (index != last)
        {
            _pos[index] = _pos[last];
            _vel[index] = _vel[last];

            _radius[index] = _radius[last];
            _targetRadius[index] = _targetRadius[last];
            _radiusVel[index] = _radiusVel[last];

            _isMerging[index] = _isMerging[last];
            _mergeDst[index] = _mergeDst[last];
            _mergeFinalRadius[index] = _mergeFinalRadius[last];

            _seedA[index] = _seedA[last];
            _seedB[index] = _seedB[last];
            _seedC[index] = _seedC[last];

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

    private void UploadToShader()
    {
        int count = Mathf.Clamp(_count, 0, Max);
        targetMaterial.SetInt(BlobCountID, count);

        float t = Time.time;

        for (int i = 0; i < count; i++)
        {
            float ox = Mathf.Sin(t * flowPosSpeed + _seedA[i]);
            float oy = Mathf.Sin(t * (flowPosSpeed * 1.17f) + _seedB[i]);
            Vector3 flowOffset = new Vector3(ox, oy, 0f) * flowPosAmplitude;

            float breath = 1f + flowRadiusAmplitude * Mathf.Sin(t * flowRadiusSpeed + _seedC[i]);
            float r = Mathf.Max(0.0001f, _radius[i] * breath);

            Vector3 p = _pos[i] + flowOffset;
            _upload[i] = new Vector4(p.x, p.y, p.z, r);
        }

        targetMaterial.SetVectorArray(BlobsID, _upload);
    }
}
