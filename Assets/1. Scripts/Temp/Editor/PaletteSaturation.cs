using System.IO;
using UnityEditor;
using UnityEngine;

// 맵 톤 시험용. 원본 팔레트를 그룹별로 복사해 밝기와 색상은 두고 채도만 바꾼 텍스처를 만들어 복사본 재질에 연결함
[CreateAssetMenu(menuName = "MediaProject/Palette Saturation")]
public class PaletteSaturation : ScriptableObject
{
    [System.Serializable]
    public class Group
    {
        public string name;
        [Tooltip("채도를 바꿀 원본 텍스처. 원본 파일은 읽기만 함")]
        public Texture2D source;
        [Tooltip("1이면 원본 그대로. 1.25면 채도 25% 증가")]
        [Range(0f, 2f)] public float saturation = 1f;
        [Tooltip("재질에서 텍스처가 들어가는 속성 이름")]
        public string textureProperty = "_BaseMap";
        public Material[] materials;
    }

    public string outputFolder = "Assets/Settings/InGameMap/WheelTone/Textures";
    public Group[] groups;

    static readonly Vector3 Luma = new Vector3(0.2126f, 0.7152f, 0.0722f);

    public void Regenerate()
    {
        if (!AssetDatabase.IsValidFolder(outputFolder))
        {
            string parent = Path.GetDirectoryName(outputFolder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(outputFolder));
        }

        foreach (var group in groups)
        {
            if (group.source == null || string.IsNullOrEmpty(group.name)) continue;
            string sourcePath = AssetDatabase.GetAssetPath(group.source);
            string outputPath = $"{outputFolder}/{group.name}_Palette.png";

            // 처음 한 번은 원본을 통째로 복사해 임포트 설정(압축, 필터, sRGB)을 그대로 물려받음
            if (!File.Exists(outputPath)) AssetDatabase.CopyAsset(sourcePath, outputPath);

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(sourcePath));
            Color32[] pixels = texture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Saturate(pixels[i], group.saturation);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            DestroyImmediate(texture);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

            var output = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            foreach (var material in group.materials)
            {
                if (material == null || !material.HasProperty(group.textureProperty)) continue;
                material.SetTexture(group.textureProperty, output);
                EditorUtility.SetDirty(material);
            }
        }
        AssetDatabase.SaveAssets();
    }

    // 선형 공간에서 밝기(휘도)를 기준으로 채도만 늘림. 회색 축에서 같은 방향으로만 밀어서 색상(H)은 유지됨
    static Color32 Saturate(Color32 c, float saturation)
    {
        Vector3 linear = new Vector3(
            Mathf.GammaToLinearSpace(c.r / 255f),
            Mathf.GammaToLinearSpace(c.g / 255f),
            Mathf.GammaToLinearSpace(c.b / 255f));
        float y = Vector3.Dot(linear, Luma);
        Vector3 chroma = linear - new Vector3(y, y, y);

        // 0~1 범위를 넘으면 잘리면서 색상이 틀어지므로 넘기 직전까지만 늘림
        float s = saturation;
        for (int k = 0; k < 3; k++)
        {
            float d = chroma[k];
            if (d > 1e-5f) s = Mathf.Min(s, (1f - y) / d);
            else if (d < -1e-5f) s = Mathf.Min(s, y / -d);
        }
        s = Mathf.Max(s, Mathf.Min(saturation, 1f));

        Vector3 result = new Vector3(y, y, y) + chroma * s;
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(result.x)) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(result.y)) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(result.z)) * 255f),
            c.a);
    }
}

[CustomEditor(typeof(PaletteSaturation))]
class PaletteSaturationEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("텍스처 재생성", GUILayout.Height(28)))
            ((PaletteSaturation)target).Regenerate();
    }
}
