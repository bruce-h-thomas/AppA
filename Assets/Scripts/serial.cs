using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class serial : MonoBehaviour
{
    // What gets written to the JSON string: a flat list of the object and its children.
    [Serializable]
    private class ObjectData
    {
        public List<NodeData> nodes = new List<NodeData>();
    }

    [Serializable]
    private class NodeData
    {
        public string name;
        public int parent;          // index into nodes, -1 for the root
        public bool active;
        public int layer;
        public Vector3 position;    // world position for the root, local for children
        public Quaternion rotation;
        public Vector3 scale;

        public bool hasMesh;
        public Vector3[] vertices;
        public int[] triangles;
        public Vector3[] normals;
        public Vector2[] uv;

        public bool hasRenderer;
        public string shader;
        public Color color;
        public string texturePng;   // main texture as a base64 PNG, empty if none
        public Vector2 textureScale = Vector2.one;    // tiling
        public Vector2 textureOffset = Vector2.zero;

        public string collider;     // "", "box", "sphere", "capsule" or "mesh"
        public bool colliderTrigger;
        public bool colliderConvex;
        public Vector3 colliderCenter;
        public Vector3 colliderSize;    // box size, or radius in x for a sphere or capsule
        public float colliderHeight;    // capsule only
        public int colliderDirection;   // capsule only: 0 = X, 1 = Y, 2 = Z

        public bool hasBody;
        public float mass;
        public bool useGravity;
        public bool isKinematic;

        public List<ScriptData> scripts = new List<ScriptData>();

        // Unity's ids for the original's own parts, so that scripts which point at them
        // (e.g. a grab script's list of its own colliders) can be pointed at the rebuilt parts.
        public int idObject;
        public int idTransform;
        public int idMeshFilter;
        public int idRenderer;
        public int idCollider;
        public int idBody;
        // every part of the original, including ones that are not rebuilt (e.g. a collider type
        // that is not saved), so links to those are cleared instead of left pointing at the original
        public List<int> ownIds = new List<int>();
    }

    [Serializable]
    private class ScriptData
    {
        public string type;     // full type name, used to add the component again
        public bool enabled;
        public int id;          // Unity's id for the original script
        public string json;     // the script's fields, from JsonUtility
    }

    /// <summary>
    /// Turns a GameObject (and its children) into a JSON string: transform, mesh, colour,
    /// main texture, collider, Rigidbody and scripts. Links from a script to other scene
    /// objects only survive while the app keeps running, not in a string loaded in a later run.
    /// </summary>
    public static string ToJson(GameObject go)
    {
        return ToJson(go, go != null ? go.transform.localScale : Vector3.one);
    }

    /// <summary>
    /// Same, but saves the object with the given scale instead of the one it has right now.
    /// Use this for a shrunken carried copy: serial.ToJson(heldCopy, heldCopyFullScale).
    /// </summary>
    public static string ToJson(GameObject go, Vector3 fullScale)
    {
        ObjectData data = new ObjectData();
        if (go != null)
            AddNode(data, go.transform, -1);
        if (data.nodes.Count > 0)
        {
            data.nodes[0].position = go.transform.position;
            data.nodes[0].rotation = go.transform.rotation;
            data.nodes[0].scale = fullScale;
        }
        return JsonUtility.ToJson(data);
    }

    /// <summary>
    /// Builds a new GameObject from a string made by ToJson. Returns null if the string is not valid.
    /// keepOutsideLinks: script links to objects that are not part of this object (e.g. the XR
    /// Interaction Manager) are kept. Pass false when the string came from another application
    /// or an earlier run: those links are Unity id numbers that mean nothing here, and could
    /// even match an unrelated object, so they are cleared instead.
    /// </summary>
    public static GameObject FromJson(string json, bool keepOutsideLinks = true)
    {
        ObjectData data = null;
        try
        {
            data = JsonUtility.FromJson<ObjectData>(json);
        }
        catch (ArgumentException e)
        {
            Debug.LogWarning("serial.FromJson: not valid JSON: " + e.Message);
        }
        if (data == null || data.nodes == null || data.nodes.Count == 0)
            return null;

        // original part id -> rebuilt part id, for fixing up the links inside scripts
        Dictionary<int, int> idMap = new Dictionary<int, int>();

        GameObject[] made = new GameObject[data.nodes.Count];
        for (int i = 0; i < data.nodes.Count; i++)
        {
            NodeData node = data.nodes[i];
            GameObject go = new GameObject(node.name);
            made[i] = go;
            go.layer = node.layer;

            // Build everything switched off. Scripts must not start running (Awake/OnEnable)
            // until their saved values have been put back, which happens at the very end.
            if (i == 0)
                go.SetActive(false);

            // parents always come before their children in the list
            if (node.parent >= 0 && node.parent < i)
                go.transform.SetParent(made[node.parent].transform, false);
            MapId(idMap, node.idObject, go);
            MapId(idMap, node.idTransform, go.transform);
            go.transform.localPosition = node.position;
            go.transform.localRotation = node.rotation;
            go.transform.localScale = node.scale;

            Mesh mesh = null;
            if (node.hasMesh && node.vertices != null && node.triangles != null)
            {
                mesh = new Mesh();
                mesh.name = node.name;
                if (node.vertices.Length > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = node.vertices;
                mesh.triangles = node.triangles;
                if (node.uv != null && node.uv.Length == node.vertices.Length)
                    mesh.uv = node.uv;
                if (node.normals != null && node.normals.Length == node.vertices.Length)
                    mesh.normals = node.normals;
                else
                    mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                MeshFilter meshFilter = go.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;
                MapId(idMap, node.idMeshFilter, meshFilter);
            }

            if (node.hasRenderer)
            {
                Shader shader = Shader.Find(node.shader);
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                Material material = new Material(shader);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", node.color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", node.color);

                if (!string.IsNullOrEmpty(node.texturePng))
                {
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.LoadImage(Convert.FromBase64String(node.texturePng));   // resizes to the PNG
                    // a string saved before tiling was added has no scale: treat that as 1 x 1
                    Vector2 tiling = node.textureScale == Vector2.zero ? Vector2.one : node.textureScale;
                    foreach (string property in new string[] { "_BaseMap", "_MainTex" })
                    {
                        if (!material.HasProperty(property))
                            continue;
                        material.SetTexture(property, texture);
                        material.SetTextureScale(property, tiling);
                        material.SetTextureOffset(property, node.textureOffset);
                    }
                }
                MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                MapId(idMap, node.idRenderer, meshRenderer);
            }

            if (node.collider == "box")
            {
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.center = node.colliderCenter;
                box.size = node.colliderSize;
                box.isTrigger = node.colliderTrigger;
            }
            else if (node.collider == "sphere")
            {
                SphereCollider sphere = go.AddComponent<SphereCollider>();
                sphere.center = node.colliderCenter;
                sphere.radius = node.colliderSize.x;
                sphere.isTrigger = node.colliderTrigger;
            }
            else if (node.collider == "capsule")
            {
                CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
                capsule.center = node.colliderCenter;
                capsule.radius = node.colliderSize.x;
                capsule.height = node.colliderHeight;
                capsule.direction = node.colliderDirection;
                capsule.isTrigger = node.colliderTrigger;
            }
            else if (node.collider == "mesh" && mesh != null)
            {
                MeshCollider meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = node.colliderConvex;
                meshCollider.isTrigger = node.colliderConvex && node.colliderTrigger;
            }
            MapId(idMap, node.idCollider, go.GetComponent<Collider>());

            if (node.hasBody)
            {
                Rigidbody body = go.AddComponent<Rigidbody>();
                body.mass = node.mass;
                body.useGravity = node.useGravity;
                body.isKinematic = node.isKinematic;
                MapId(idMap, node.idBody, body);
            }

            if (i > 0)
                go.SetActive(node.active);
        }

        // Scripts, step 1: add every script (they stay asleep because the root is switched off)
        List<MonoBehaviour> addedScripts = new List<MonoBehaviour>();
        List<ScriptData> addedData = new List<ScriptData>();
        for (int i = 0; i < data.nodes.Count; i++)
        {
            if (data.nodes[i].scripts == null)
                continue;
            foreach (ScriptData script in data.nodes[i].scripts)
            {
                Type type = FindType(script.type);
                if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    Debug.LogWarning("serial.FromJson: script " + script.type + " was not found, so it was left out");
                    continue;
                }

                // adding one script can bring another with it (RequireComponent): use that one
                // rather than adding a second
                MonoBehaviour added = null;
                foreach (Component existing in made[i].GetComponents(type))
                {
                    MonoBehaviour candidate = existing as MonoBehaviour;
                    if (candidate != null && candidate.GetType() == type && !addedScripts.Contains(candidate))
                    {
                        added = candidate;
                        break;
                    }
                }
                if (added == null)
                    added = made[i].AddComponent(type) as MonoBehaviour;
                if (added == null)
                    continue;

                addedScripts.Add(added);
                addedData.Add(script);
                MapId(idMap, script.id, added);
            }
        }

        // Scripts, step 2: put the saved values back, with links to the original's own parts
        // changed to point at the rebuilt parts. Links to other scene objects are left as they are,
        // or cleared (id 0 = nothing) when keepOutsideLinks is false.
        // A link to one of the original's own parts that was not rebuilt is always cleared: left
        // alone it would tie the copy to the original (e.g. a grab script registering the
        // original's collider as its own).
        HashSet<int> originalParts = new HashSet<int>();
        foreach (NodeData node in data.nodes)
            if (node.ownIds != null)
                originalParts.UnionWith(node.ownIds);

        for (int i = 0; i < addedScripts.Count; i++)
        {
            string fields = Regex.Replace(addedData[i].json, "\"instanceID\"\\s*:\\s*(-?\\d+)", match =>
            {
                int oldId;
                int newId;
                if (!int.TryParse(match.Groups[1].Value, out oldId))
                    return match.Value;
                if (idMap.TryGetValue(oldId, out newId))
                    return "\"instanceID\":" + newId;
                if (originalParts.Contains(oldId))
                    return "\"instanceID\":0";
                return keepOutsideLinks ? match.Value : "\"instanceID\":0";
            });
            JsonUtility.FromJsonOverwrite(fields, addedScripts[i]);
            addedScripts[i].enabled = addedData[i].enabled;
        }

        // everything is in place: switch the object on, which starts its scripts
        made[0].SetActive(data.nodes[0].active);
        return made[0];
    }

    // Gets a texture's pixels as a PNG. Works for any texture, including imported ones that are
    // compressed or have Read/Write switched off: those are drawn into a temporary render
    // target and read back from there.
    private static byte[] TextureToPng(Texture texture)
    {
        Texture2D direct = texture as Texture2D;
        if (direct != null && direct.isReadable &&
            (direct.format == TextureFormat.RGBA32 || direct.format == TextureFormat.ARGB32 || direct.format == TextureFormat.RGB24))
            return direct.EncodeToPNG();

        if (texture.width <= 0 || texture.height <= 0)
            return null;

        RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D readable = null;
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();
            return readable.EncodeToPNG();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (readable != null)
                Destroy(readable);
        }
    }

    // Finds a script type from its saved name ("SpinY, SharedBehaviours, Version=..."). Type.GetType
    // does not see DLLs loaded while the app runs (e.g. by ObjectReceiver), so if it fails, look
    // through every loaded assembly for one with the right name.
    private static Type FindType(string assemblyQualifiedName)
    {
        if (string.IsNullOrEmpty(assemblyQualifiedName))
            return null;
        Type type = Type.GetType(assemblyQualifiedName);
        if (type != null)
            return type;

        string[] parts = assemblyQualifiedName.Split(',');
        if (parts.Length < 2)
            return null;
        string typeName = parts[0].Trim();
        string assemblyName = parts[1].Trim();
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name == assemblyName)
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }
        }
        return null;
    }

    private static void MapId(Dictionary<int, int> idMap, int oldId, UnityEngine.Object rebuilt)
    {
        if (oldId != 0 && rebuilt != null)
            idMap[oldId] = rebuilt.GetInstanceID();
    }

    // Adds this transform and then its children, so a parent is always earlier in the list.
    private static void AddNode(ObjectData data, Transform t, int parent)
    {
        // the selection highlight box is not part of the object
        if (t.GetComponent<LineRenderer>() != null)
            return;

        NodeData node = new NodeData();
        node.name = t.name;
        node.parent = parent;
        node.active = t.gameObject.activeSelf;
        node.layer = t.gameObject.layer;
        node.position = t.localPosition;
        node.rotation = t.localRotation;
        node.scale = t.localScale;

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh.isReadable)
            {
                node.hasMesh = true;
                node.vertices = mesh.vertices;
                node.triangles = mesh.triangles;
                node.normals = mesh.normals;
                node.uv = mesh.uv;
            }
            else
                Debug.LogWarning("serial.ToJson: mesh on " + t.name + " is not readable, so it was left out");
        }

        Renderer rend = t.GetComponent<Renderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            Material material = rend.sharedMaterial;
            node.hasRenderer = true;
            node.shader = material.shader.name;
            node.color = Color.white;
            if (material.HasProperty("_BaseColor"))
                node.color = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                node.color = material.GetColor("_Color");

            Texture texture = null;
            string textureProperty = "";
            foreach (string property in new string[] { "_BaseMap", "_MainTex" })
            {
                if (texture == null && material.HasProperty(property))
                {
                    texture = material.GetTexture(property);
                    textureProperty = property;
                }
            }
            node.texturePng = "";
            if (texture != null)
            {
                node.textureScale = material.GetTextureScale(textureProperty);
                node.textureOffset = material.GetTextureOffset(textureProperty);
                byte[] png = TextureToPng(texture);
                if (png != null)
                    node.texturePng = Convert.ToBase64String(png);
                else
                    Debug.LogWarning("serial.ToJson: could not read the texture on " + t.name + ", so it was left out");
            }
        }

        node.collider = "";
        Collider col = t.GetComponent<Collider>();
        if (col != null)
        {
            node.colliderTrigger = col.isTrigger;
            if (col is BoxCollider box)
            {
                node.collider = "box";
                node.colliderCenter = box.center;
                node.colliderSize = box.size;
            }
            else if (col is SphereCollider sphere)
            {
                node.collider = "sphere";
                node.colliderCenter = sphere.center;
                node.colliderSize = new Vector3(sphere.radius, 0, 0);
            }
            else if (col is CapsuleCollider capsule)
            {
                node.collider = "capsule";
                node.colliderCenter = capsule.center;
                node.colliderSize = new Vector3(capsule.radius, 0, 0);
                node.colliderHeight = capsule.height;
                node.colliderDirection = capsule.direction;
            }
            else if (col is MeshCollider meshCollider)
            {
                node.collider = "mesh";
                node.colliderConvex = meshCollider.convex;
            }
        }

        Rigidbody body = t.GetComponent<Rigidbody>();
        if (body != null)
        {
            node.hasBody = true;
            node.mass = body.mass;
            node.useGravity = body.useGravity;
            node.isKinematic = body.isKinematic;
        }

        foreach (MonoBehaviour script in t.GetComponents<MonoBehaviour>())
        {
            // a script whose file is missing shows up as null
            if (script == null)
                continue;
            ScriptData scriptData = new ScriptData();
            scriptData.type = script.GetType().AssemblyQualifiedName;
            scriptData.enabled = script.enabled;
            scriptData.id = script.GetInstanceID();
            scriptData.json = JsonUtility.ToJson(script);
            node.scripts.Add(scriptData);
        }

        node.idObject = t.gameObject.GetInstanceID();
        node.idTransform = t.GetInstanceID();
        node.idMeshFilter = mf != null ? mf.GetInstanceID() : 0;
        node.idRenderer = rend != null ? rend.GetInstanceID() : 0;
        node.idCollider = col != null ? col.GetInstanceID() : 0;
        node.idBody = body != null ? body.GetInstanceID() : 0;
        node.ownIds.Add(node.idObject);
        foreach (Component part in t.GetComponents<Component>())
            if (part != null)
                node.ownIds.Add(part.GetInstanceID());

        int index = data.nodes.Count;
        data.nodes.Add(node);
        foreach (Transform child in t)
            AddNode(data, child, index);
    }
}
