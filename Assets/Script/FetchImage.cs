using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class FetchImage : MonoBehaviour
{
    [Header("把水滴 Quad 的 Renderer 拖进来")]
    public Renderer targetRenderer;

    [Header("外部图片 URL（留空=用 StreamingAssets）")]
    public string imageUrl = "";

    [Header("StreamingAssets 里的文件名（必须完全一致：含后缀）")]
    public string streamingFileName = "test.jpg";

    [Header("写进水滴 Shader 的纹理槽")]
    public string textureProperty = "_ExternalTex";

    [Header("强验证：下载成功后先用 Unlit/Texture 直接显示图片")]
    public bool forceShowAsUnlitTexture = true;

    void Start()
    {
        StartCoroutine(DownloadAndApply());
    }

    IEnumerator DownloadAndApply()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("❌ targetRenderer 没拖！把水滴 Quad 拖到脚本的 Target Renderer。");
            yield break;
        }

        string url = imageUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = Application.streamingAssetsPath + "/" + streamingFileName;

        if (!url.StartsWith("http") && !url.StartsWith("file://"))
            url = "file://" + url;

        Debug.Log("✅ 最终请求地址: " + url);

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ 下载失败: " + req.error + "\nURL: " + url);
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            Debug.Log("✅ 下载成功: " + tex.width + "x" + tex.height);

            // 1) 强验证：直接用 Unlit/Texture 显示（你一眼就能看到）
            if (forceShowAsUnlitTexture)
            {
                Shader unlit = Shader.Find("Unlit/Texture");
                if (unlit != null)
                {
                    Material m = new Material(unlit);
                    m.mainTexture = tex;
                    targetRenderer.material = m;
                    Debug.Log("✅ 已用 Unlit/Texture 强制显示图片（验证通过）");
                    yield break; // 先验证成功，后面再切回水滴 shader
                }
                else
                {
                    Debug.LogWarning("⚠️ 找不到 Unlit/Texture，跳过强验证。");
                }
            }

            // 2) 正式写入你的水滴 shader（如果你关掉强验证才走到这里）
            Material mat = targetRenderer.material;
            mat.SetFloat("_UseExternal", 1f);
            mat.SetTexture(textureProperty, tex);
            Debug.Log("✅ 已写入水滴材质贴图槽: " + textureProperty);
        }
    }
}
