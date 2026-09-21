#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpiderVerse.LineArt.Editor
{
    [CustomEditor(typeof(ObjectLineArtSource))]
    public sealed class ObjectLineArtSourceEditor : UnityEditor.Editor
    {
        static void RefreshViews(){EditorApplication.QueuePlayerLoopUpdate();SceneView.RepaintAll();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();}
        static LineArtAppearance Defaults()
        {
            var feature=Resources.FindObjectsOfTypeAll<ObjectLineArtFeature>().FirstOrDefault(f=>f&&f.isActive);
            return feature?feature.settings.CopyAppearance():new LineArtAppearance();
        }
        void AddLayer(int copy=-1)
        {
            serializedObject.ApplyModifiedProperties();var source=(ObjectLineArtSource)target;
            Undo.RecordObject(source,copy<0?"Add Line Art Layer":"Duplicate Line Art Layer");
            var previous=source.layers??Array.Empty<LineArtLayer>();var result=new LineArtLayer[previous.Length+1];Array.Copy(previous,result,previous.Length);
            var template=copy>=0?previous[copy]:null;
            result[previous.Length]=new LineArtLayer{name=template!=null?template.name+" Copy":"Layer "+(previous.Length+1),enabled=true,useSourceRenderers=template?.useSourceRenderers??true,renderers=template?.renderers!=null?(Renderer[])template.renderers.Clone():Array.Empty<Renderer>(),appearance=template?.appearance?.CopyAppearance()??Defaults()};
            source.layers=result;EditorUtility.SetDirty(source);PrefabUtility.RecordPrefabInstancePropertyModifications(source);serializedObject.Update();var added=serializedObject.FindProperty("layers").GetArrayElementAtIndex(previous.Length);added.isExpanded=true;added.FindPropertyRelative("appearance").isExpanded=true;RefreshViews();
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using(new EditorGUI.DisabledScope(true))EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("renderers"),new GUIContent("Source Renderers"),true);
            var layers=serializedObject.FindProperty("layers");
            EditorGUILayout.Space();EditorGUILayout.LabelField("Stroke Layers",EditorStyles.boldLabel);
            if(layers.arraySize==0)EditorGUILayout.HelpBox("Legacy single layer: uses the Renderer Feature appearance. Add Layer copies that appearance and keeps the current renderer selection.",MessageType.Info);
            else EditorGUILayout.HelpBox("Drawn in list order; later layers appear on top. Appearance is independent per layer. Edge types, occlusion and connections remain shared in the Renderer Feature.",MessageType.None);
            int remove=-1,duplicate=-1,move=-1,direction=0;
            for(int i=0;i<layers.arraySize;i++)
            {
                var layer=layers.GetArrayElementAtIndex(i);var enabled=layer.FindPropertyRelative("enabled");var name=layer.FindPropertyRelative("name");
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);EditorGUILayout.BeginHorizontal();
                enabled.boolValue=EditorGUILayout.Toggle(enabled.boolValue,GUILayout.Width(18));
                layer.isExpanded=EditorGUILayout.Foldout(layer.isExpanded,string.IsNullOrEmpty(name.stringValue)?"Layer "+(i+1):name.stringValue,true);
                using(new EditorGUI.DisabledScope(i==0))if(GUILayout.Button("↑",GUILayout.Width(24))){move=i;direction=-1;}
                using(new EditorGUI.DisabledScope(i==layers.arraySize-1))if(GUILayout.Button("↓",GUILayout.Width(24))){move=i;direction=1;}
                if(GUILayout.Button("Copy",GUILayout.Width(42)))duplicate=i;
                if(GUILayout.Button("×",GUILayout.Width(24)))remove=i;
                EditorGUILayout.EndHorizontal();
                if(layer.isExpanded)
                {
                    EditorGUILayout.PropertyField(name);
                    var inherit=layer.FindPropertyRelative("useSourceRenderers");EditorGUILayout.PropertyField(inherit);
                    if(!inherit.boolValue)EditorGUILayout.PropertyField(layer.FindPropertyRelative("renderers"),new GUIContent("Layer Renderers"),true);
                    var appearance=layer.FindPropertyRelative("appearance");EditorGUILayout.PropertyField(appearance,new GUIContent("Appearance"),true);
                }
                EditorGUILayout.EndVertical();
            }
            if(remove>=0)layers.DeleteArrayElementAtIndex(remove);
            if(move>=0)layers.MoveArrayElement(move,move+direction);
            if(serializedObject.ApplyModifiedProperties())RefreshViews();
            if(duplicate>=0)AddLayer(duplicate);
            if(GUILayout.Button("Add Layer"))AddLayer();
            if(GUILayout.Button("Select Shared Stroke Settings"))LineArtSetup.SelectSettings();
        }
    }
}
#endif
