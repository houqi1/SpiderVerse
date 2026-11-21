using UnityEditor;
using UnityEngine;

namespace LassylaPort.Editor
{
    public class LassylaMaterialGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox("Fresnel Ramp 控制边缘宽度，Edge Noise / Scale 控制缺口尺寸，Noise Ramp 控制噪声形成缺口的范围。父材质同时影响身体与头套。\n原截图使用播放中的材质预览。LineArt_Renderer 的 Match Timeline Playback 切换播放和暂停时的颗粒表现。", MessageType.Info);
            var mat=(Material)editor.target;
            if(LassylaForestPreview.Active || SceneVisibilityManager.instance.IsCurrentStageIsolated()){
                if(GUILayout.Button("返回城市视图"))LassylaForestPreview.ReturnToCity();
            }else if(GUILayout.Button("切换到 Blender 材质预览")){
                LassylaForestPreview.Toggle();
            }
            if(mat.parent && GUILayout.Button("打开共用父材质（调整所有部件）")) Selection.activeObject=mat.parent;
            base.OnGUI(editor, properties);
        }
    }
}
