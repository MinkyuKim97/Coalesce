using UnityEngine;

public class BlobDriver : MonoBehaviour
{
    [Header("目标材质（把 MetaballsMat 拖进来）")]
    public Material targetMaterial;

    [Header("最多水滴数（和 shader 里 MAX_BLOBS 对齐，别超过 64）")]
    [Range(1, 64)]
    public int maxBlobs = 16;

    [Header("测试用：水滴半径")]
    [Range(0.05f, 2.0f)]
    public float radius = 0.35f;

    [Header("测试用：水滴分布范围（越小越集中）")]
    public float spread = 1.5f;

    private static readonly int UseExternalID = Shader.PropertyToID("_UseExternal");
    private static readonly int BlobCountID = Shader.PropertyToID("_BlobCount");
    private static readonly int BlobsID = Shader.PropertyToID("_Blobs");

    private Vector4[] _blobs;

    private void Awake()
    {
        _blobs = new Vector4[64];
    }

    private void Start()
    {
        if (targetMaterial == null)
        {
            Debug.LogError("BlobDriver: 没有指定 targetMaterial（把 MetaballsMat 拖进来）。");
            enabled = false;
            return;
        }

        // 强制使用外部数据模式
        targetMaterial.SetFloat(UseExternalID, 1f);

        // 先生成一组固定测试点（你确认视觉 OK 后，再换成真实坐标）
        for (int i = 0; i < maxBlobs; i++)
        {
            float t = (maxBlobs <= 1) ? 0f : (i / (float)(maxBlobs - 1));
            float x = Mathf.Lerp(-spread, spread, t);
            float y = Mathf.Sin(t * Mathf.PI * 2f) * 0.5f * spread;
            float z = 0f;

            _blobs[i] = new Vector4(x, y, z, radius);
        }

        Upload();
    }

    private void Update()
    {
        // 这里先不做复杂交互：每帧只是把数据上传，保证你后面接实时坐标时逻辑一致
        Upload();
    }

    /// <summary>
    /// 把一个“归一化坐标(0~1)”映射到 shader 空间，并更新水滴（你接设备坐标时最常用这一套）
    /// </summary>
    public void SetFromNormalizedPoints(Vector2[] points01, float r)
    {
        int count = Mathf.Min(points01.Length, 64);
        for (int i = 0; i < count; i++)
        {
            // 0~1 映射到 -spread~spread
            float x = (points01[i].x * 2f - 1f) * spread;
            float y = (points01[i].y * 2f - 1f) * spread;
            _blobs[i] = new Vector4(x, y, 0f, r);
        }

        maxBlobs = Mathf.Clamp(count, 1, 64);
        Upload();
    }

    /// <summary>
    /// 直接设置某个水滴（你后面如果用“多人->水滴池”的方式，会很方便）
    /// </summary>
    public void SetBlob(int index, Vector3 pos, float r)
    {
        if (index < 0 || index >= 64) return;
        _blobs[index] = new Vector4(pos.x, pos.y, pos.z, r);
    }

    private void Upload()
    {
        int count = Mathf.Clamp(maxBlobs, 1, 64);
        targetMaterial.SetInt(BlobCountID, count);
        targetMaterial.SetVectorArray(BlobsID, _blobs);
    }
}
