using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 导入 spidergwen.fbx / Spider-Gwen.fbx 时，把绑定姿势下的高度（米，脚底约 0.04，头顶约 1.85）写入 UV3（TEXCOORD3）的 y。
// 做法：不依赖节点当前姿势或导入单位/轴向，而是用每个网格 bindposes 反推出各骨骼在网格空间的位置，
// 再和 Blender 里读出的骨骼静止高度做最小二乘线性拟合 h = dot(a, p) + b，把拟合结果作用到每个顶点。
// 这样得到的是静态数据，动画和移动都不会改变条纹。
public class BindPoseHeightPostprocessor : AssetPostprocessor
{
    static readonly string[] TargetModels = { "spidergwen.fbx", "Spider-Gwen.fbx" };
    const int Channel = 3; // UV3 = TEXCOORD3（TEXCOORD2 / 7 被 Lassyla 的 Generated 坐标占用）

    // 骨骼名 -> 绑定姿势下的高度（米），来自 Blender 中对 spidergwen.fbx 的读取。
    static readonly Dictionary<string, float> BoneHeights = new Dictionary<string, float>
    {
        {"hero_spidergwen01_S03", 0.0400f},
        {"Bip001", 1.0879f},
        {"Bip001 Pelvis", 1.0879f},
        {"Bip001 L Thigh", 1.0879f},
        {"Bip001 L Calf", 0.6211f},
        {"Bip001 L Foot", 0.1599f},
        {"Bip001 L Toe0", 0.0414f},
        {"Bip001 R Thigh", 1.0879f},
        {"Bip001 R Calf", 0.6211f},
        {"Bip001 R Foot", 0.1599f},
        {"Bip001 R Toe0", 0.0414f},
        {"Bip001 Spine", 1.1661f},
        {"Bip001 LThighTwist", 1.0879f},
        {"Bip001 RThighTwist", 1.0879f},
        {"Bip001 Spine1", 1.2545f},
        {"Bip001 Spine2", 1.3510f},
        {"Bip001 Neck", 1.5533f},
        {"Bip001 Head", 1.6238f},
        {"Bonehood_L", 1.7681f},
        {"Bonehood_R", 1.7681f},
        {"hair bone001", 1.7720f},
        {"hair bone002", 1.7240f},
        {"hair bone003", 1.6349f},
        {"hair bone004", 1.6651f},
        {"hair bone005", 1.6667f},
        {"jaw bone001", 1.6592f},
        {"Bip001 L Clavicle", 1.5213f},
        {"Bip001 L UpperArm", 1.4912f},
        {"Bip001 L Forearm", 1.3271f},
        {"Bip001 L Hand", 1.1311f},
        {"Bip001 L Finger0", 1.0826f},
        {"Bip001 L Finger01", 1.0394f},
        {"Bip001 L Finger1", 1.0560f},
        {"Bip001 L Finger11", 1.0335f},
        {"Bip001 L Finger2", 1.0606f},
        {"Bip001 L Finger21", 1.0286f},
        {"Bip001 L Finger3", 1.0596f},
        {"Bip001 L Finger31", 1.0297f},
        {"Bip001 L Finger4", 1.0548f},
        {"Bip001 L Finger41", 1.0360f},
        {"Bip001 L ForeTwist", 1.3270f},
        {"Bip001 L ForeTwist1", 1.3075f},
        {"Bip001 L ForeTwist2", 1.2879f},
        {"Bip001 L ForeTwist3", 1.2683f},
        {"Bip001 L ForeTwist4", 1.2487f},
        {"Bip001 L ForeTwist5", 1.2291f},
        {"Bip001 L ForeTwist6", 1.2095f},
        {"Bip001 L ForeTwist7", 1.1899f},
        {"Bip001 L ForeTwist8", 1.1703f},
        {"Bip001 L ForeTwist9", 1.1507f},
        {"Bip001 LUpArmTwist", 1.4912f},
        {"Bip001 R Clavicle", 1.5213f},
        {"Bip001 R UpperArm", 1.4912f},
        {"Bip001 R Forearm", 1.3271f},
        {"Bip001 R Hand", 1.1311f},
        {"Bip001 R Finger0", 1.0826f},
        {"Bip001 R Finger01", 1.0394f},
        {"Bip001 R Finger1", 1.0560f},
        {"Bip001 R Finger11", 1.0335f},
        {"Bip001 R Finger2", 1.0606f},
        {"Bip001 R Finger21", 1.0286f},
        {"Bip001 R Finger3", 1.0596f},
        {"Bip001 R Finger31", 1.0297f},
        {"Bip001 R Finger4", 1.0548f},
        {"Bip001 R Finger41", 1.0360f},
        {"Bip001 R ForeTwist", 1.3271f},
        {"Bip001 R ForeTwist1", 1.3075f},
        {"Bip001 R ForeTwist2", 1.2879f},
        {"Bip001 R ForeTwist3", 1.2683f},
        {"Bip001 R ForeTwist4", 1.2487f},
        {"Bip001 R ForeTwist5", 1.2291f},
        {"Bip001 R ForeTwist6", 1.2095f},
        {"Bip001 R ForeTwist7", 1.1899f},
        {"Bip001 R ForeTwist8", 1.1703f},
        {"Bip001 R ForeTwist9", 1.1507f},
        {"Bip001 RUpArmTwist", 1.4912f},
        {"Bonehip_L", 1.0879f},
        {"Bonehip_R", 1.0879f},
    };

    // 修改本脚本后递增版本号，Unity 会自动重新导入受影响的模型。
    public override uint GetVersion() { return 4; }

    static bool IsTarget(string path)
    {
        foreach (var m in TargetModels)
            if (path.EndsWith("/" + m, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!IsTarget(assetPath)) return;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (!mesh) continue;
            if (!FitHeight(smr, mesh, out Vector3 a, out float b))
            {
                Debug.LogWarning($"[BindPoseHeight] {smr.name}: 匹配到的骨骼不足，无法拟合高度，UV3 保持为 0。", smr);
                continue;
            }
            var verts = mesh.vertices;
            var uv = new Vector3[verts.Length];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float h = Vector3.Dot(a, verts[i]) + b;
                uv[i] = new Vector3(0f, h, 0f);
                lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }
            mesh.SetUVs(Channel, uv);
            Debug.Log($"[BindPoseHeight] {smr.name}: 高度范围 {lo:F3} ~ {hi:F3} m，UV3 已写入: {mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3)}", smr);
        }
    }

    // 最小二乘求 h = a.x*px + a.y*py + a.z*pz + b。p 为骨骼在网格空间的绑定位置。
    static bool FitHeight(SkinnedMeshRenderer smr, Mesh mesh, out Vector3 a, out float b)
    {
        return FitHeight(smr.bones, mesh.bindposes, out a, out b);
    }

    // bones 提供骨骼名（顺序与 bind 对应）；bind 是目标网格自己的 bindposes。
    public static bool FitHeight(Transform[] bones, Matrix4x4[] bind, out Vector3 a, out float b)
    {
        a = Vector3.zero; b = 0f;
        var ata = new double[4, 5]; // 增广矩阵 [A^T A | A^T h]
        int n = 0;
        for (int i = 0; i < bones.Length && i < bind.Length; i++)
        {
            if (!bones[i] || !BoneHeights.TryGetValue(bones[i].name, out float h)) continue;
            Vector3 p = bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
            double[] r = { p.x, p.y, p.z, 1.0 };
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++) ata[x, y] += r[x] * r[y];
                ata[x, 4] += r[x] * h;
            }
            n++;
        }
        if (n < 4) return false;

        // 高斯消元（带主元选择）
        for (int c = 0; c < 4; c++)
        {
            int piv = c;
            for (int r = c + 1; r < 4; r++) if (System.Math.Abs(ata[r, c]) > System.Math.Abs(ata[piv, c])) piv = r;
            if (System.Math.Abs(ata[piv, c]) < 1e-12) return false;
            for (int k = 0; k < 5; k++) { double t = ata[c, k]; ata[c, k] = ata[piv, k]; ata[piv, k] = t; }
            for (int r = 0; r < 4; r++)
            {
                if (r == c) continue;
                double f = ata[r, c] / ata[c, c];
                for (int k = c; k < 5; k++) ata[r, k] -= f * ata[c, k];
            }
        }
        a = new Vector3((float)(ata[0, 4] / ata[0, 0]), (float)(ata[1, 4] / ata[1, 1]), (float)(ata[2, 4] / ata[2, 2]));
        b = (float)(ata[3, 4] / ata[3, 3]);
        return true;
    }
}
