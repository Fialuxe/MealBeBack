using UnityEngine;
using UnityEditor;

public class GodRayTexture : MonoBehaviour
{
    [MenuItem("Tools/Create GodRay Texture")]
    static void Create()
    {
        int width = 64;
        int height = 256;
        Texture2D tex = new Texture2D(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // 横方向：中央が明るく端が透明
                float xAlpha = 1f - Mathf.Abs((x / (float)width) * 2f - 1f);
                xAlpha = Mathf.Pow(xAlpha, 2f);

                // 縦方向：上下がフェードアウト
                float yAlpha = Mathf.Sin((y / (float)height) * Mathf.PI);

                float alpha = xAlpha * yAlpha;
                tex.SetPixel(x, y, new Color(0.7f, 0.9f, 1f, alpha));
            }
        }

        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        System.IO.File.WriteAllBytes(
            "Assets/Textures/GodRayTex.png", png);
        AssetDatabase.Refresh();
        Debug.Log("GodRayTex.png を生成しました");
    }
}