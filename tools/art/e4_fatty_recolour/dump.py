import sys, json
sys.path.insert(0, "C:/Users/MARCO/Documents/GitHub/CluckWars/tools/ui_capture"); import bridge
bridge.init()
CS = r'''
using UnityEngine; using UnityEditor; using System.IO; using System.Text; using System.Linq;
public class E4D { public static string Run(string fbx, string outp) {
  var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
  var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
  var mesh = smr.sharedMesh;
  var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var bw = mesh.boneWeights; var tri = mesh.triangles;
  var sb = new StringBuilder();
  sb.Append("BONES ").Append(string.Join(",", smr.bones.Select(b=>b.name))).Append("\n");
  sb.Append("ROOTROT ").Append(smr.transform.localEulerAngles.ToString("F2")).Append(" ").Append(smr.transform.lossyScale.ToString("F4")).Append(" b=").Append(mesh.bounds.ToString("F3")).Append("\n");
  var ic = System.Globalization.CultureInfo.InvariantCulture;
  for (int i=0;i<v.Length;i++){ int bi = bw.Length>0? bw[i].boneIndex0 : -1;
    sb.Append(string.Format(ic,"V {0:F5} {1:F5} {2:F5} {3:F4} {4:F4} {5:F4} {6:F6} {7:F6} {8}\n", v[i].x,v[i].y,v[i].z,n[i].x,n[i].y,n[i].z,uv[i].x,uv[i].y,bi)); }
  for (int i=0;i<tri.Length;i+=3) sb.Append("T ").Append(tri[i]).Append(' ').Append(tri[i+1]).Append(' ').Append(tri[i+2]).Append('\n');
  File.WriteAllText(outp, sb.ToString());
  return "ok v="+v.Length+" t="+tri.Length/3+" sub="+mesh.subMeshCount+" mat="+smr.sharedMaterial.name;
}}
'''
out = sys.argv[2]
m = bridge.call("script-execute", {"csharpCode": CS, "className": "E4D", "methodName": "Run",
   "parameters": [{"name":"fbx","typeName":"System.String","value":sys.argv[1]},{"name":"outp","typeName":"System.String","value":out}]}, timeout=200)
try: print(m["result"]["structuredContent"]["result"]["value"])
except Exception: print(bridge.text(m))
