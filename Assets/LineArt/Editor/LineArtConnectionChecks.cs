#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SpiderVerse.LineArt;
public static class LineArtConnectionChecks {
 static readonly Type G=typeof(LineArtGeometry);
 static void Require(bool value,string name){if(!value)throw new Exception(name);}
 static int Chains(LineArtSettings settings,int[] types,Vector3[] ends) {
  var span=G.GetNestedType("Span",BindingFlags.NonPublic);
  var list=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(span));
  for(int i=0;i<ends.Length;i++) {
   var s=Activator.CreateInstance(span);
   void Set(string key,object value)=>span.GetField(key).SetValue(s,value);
   Set("a",Vector3.zero);Set("b",ends[i]);Set("ka","center");Set("kb","end"+i);Set("key","edge"+i);Set("obj",1);Set("type",types[i]);list.Add(s);
  }
  var view=new LineArtGeometry.View{matrix=Matrix4x4.identity,width=512,height=512};
  var chains=(IList)G.GetMethod("ChainSpans",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{list,view,settings});
  foreach(LineArtGeometry.Chain c in chains)Require(!c.closed,"branches must stay open");
  return chains.Count;
 }
 public static void Run(){
  if(!Application.isBatchMode)throw new InvalidOperationException("Run in an isolated batch editor.");
  try {
  var s=new LineArtSettings();var ends=new[]{Vector3.left,Vector3.right,Vector3.up};
  Require(Chains(s,new[]{1,1,1},ends)==3,"legacy T branch");
  s.connectJunctions=true;
  Require(Chains(s,new[]{1,1,1},ends)==2,"straight continuation at T");
  Require(Chains(s,new[]{1,2},new[]{Vector3.left,Vector3.right})==2,"types separate by default");
  s.connectDifferentEdgeTypes=true;
  Require(Chains(s,new[]{1,2},new[]{Vector3.left,Vector3.right})==1,"mixed types connect");
  s.junctionMaxAngle=5;
  Require(Chains(s,new[]{1,1,1},new[]{Vector3.left,new Vector3(1,.3f,0),Vector3.up})==3,"angle rejects bend");
  s.junctionMaxAngle=20;
  Require(Chains(s,new[]{1,1,1},new[]{Vector3.left,new Vector3(1,.3f,0),Vector3.up})==2,"angle accepts bend");
  Require(Chains(s,new[]{1,1,1,1},new[]{Vector3.left,Vector3.right,Vector3.up,Vector3.down})==2,"cross pairs straight");
  var clip=G.GetMethod("ClipTriangle",BindingFlags.Static|BindingFlags.NonPublic);
  Require(((IList)clip.Invoke(null,new object[]{new Vector4(0,0,0,1),new Vector4(.5f,0,0,1),new Vector4(0,.5f,0,1)})).Count==3,"inside triangle");
  Require(((IList)clip.Invoke(null,new object[]{new Vector4(2,0,0,1),new Vector4(3,0,0,1),new Vector4(2,1,0,1)})).Count==0,"outside triangle");
  Debug.Log("LINE_ART_CONNECTION_CHECKS_PASS");EditorApplication.Exit(0);
 }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}

#endif
