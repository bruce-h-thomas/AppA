using System.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using VRPortalToolkit.XRI;

namespace VRPortalToolkit
{
    public class ObjectApp : MonoBehaviour
    {

        public GameObject AppA;
        public GameObject squareBuilding;
        public GameObject roundBuilding;

        private GameObject lastObject;

        public Transform leftController;   // assign XR controller transform
        public Material mat;
        private Vector3 lastPosition;


        // Start is called before the first frame update

        public int texSize = 512;
        private Texture2D tex;
        private Material matTex;
        [SerializeField] private InputActionReference xButtonActionReference;
        [SerializeField] private InputActionReference yButtonActionReference;

        private void OnEnable()
        {
            if (xButtonActionReference != null)
            {
                xButtonActionReference.action.Enable();
                xButtonActionReference.action.performed += OnXButtonPressed;
            }

            if (yButtonActionReference != null)
            {
                yButtonActionReference.action.Enable();
                yButtonActionReference.action.performed += OnYButtonPressed;
            }
        }

        private void OnDisable()
        {
            // Only unsubscribe. The controller actions are shared with the other apps,
            // so disabling them here would turn the buttons off for those apps too.
            if (xButtonActionReference != null)
            {
                xButtonActionReference.action.performed -= OnXButtonPressed;
            }

            if (yButtonActionReference != null)
            {
                yButtonActionReference.action.performed -= OnYButtonPressed;
            }
        }

        private void OnYButtonPressed(InputAction.CallbackContext context)
        {

        }

        private void OnXButtonPressed(InputAction.CallbackContext context)
        {
            lastPosition = leftController.position;

        }

        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            var action = xButtonActionReference.action;
            if (action == null)
                return;

            var yaction = yButtonActionReference.action;
            if (yaction == null)
                return;


            // Y pressed
            if (yaction.IsPressed())
            {
                Debug.Log("Y Button was pressed!");
                Ray ray = new Ray(leftController.position, leftController.forward);

                if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 100f))
                {

                    print("Hit " + hit.point + " " + hit.normal + " " + hit.textureCoord);
                    Renderer r = hit.collider.GetComponent<Renderer>();
                    Material m = r.material;

                    Texture2D tex = (Texture2D)m.mainTexture;
                    print("Hit texture: " + tex.name + " size: " + tex.width + "x" + tex.height);
                    Vector2 uv = hit.textureCoord;

                    int x = (int)(uv.x * tex.width);
                    int y = (int)(uv.y * tex.height);

                    print("Hit x: " + x + " y: " + y);

                    tex.SetPixel(x, y, UnityEngine.Color.green);
                    tex.Apply();
                }
            }

                // X pressed
                if (action.IsPressed())
            {
                Debug.Log("X button DOWN");
                float distance = Vector3.Distance(leftController.position, lastPosition);
               


                Vector3 p1 = leftController.position - lastPosition;
                p1.y = 0;
              //  Vector3 p2 = lastPosition - lastObject.transform.position;
               // Vector3 p3 = p1 - p2;
                this.transform.position += (p1 * 10f);
               // lastObject.transform.localScale += p3;
                lastPosition = leftController.position;
            }

            // X released
            if (action.WasReleasedThisFrame())
            {
                Debug.Log("X button UP");
            }

        }

        public void addRoundBuilding()
        {
            Vector3 pos = this.transform.parent.position + new Vector3(0, 1f, 0);

            Quaternion rot = Quaternion.identity;   // no rotation

            lastObject = Instantiate(roundBuilding, pos, rot);
        }

        public void addSquareBuilding()
        {
            GameObject go = createCube();
            Vector3 pos = this.transform.parent.position + new Vector3(0, 1f, 0);

            go.transform.position = pos;
            /*
            Vector3 pos = this.transform.parent.position + new Vector3(0, 1f, 0);
            Quaternion rot = Quaternion.identity;   // no rotation
            
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = pos;
            // Create material
            matTex = new Material(Shader.Find("Standard"));

            // Create writable texture
            tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
            ClearTexture(Color.white);

            mat.mainTexture = tex;

            cube.GetComponent<Renderer>().material = mat;
            
            lastObject = Instantiate(squareBuilding, pos, rot);
            */
            /*
            ProBuilderMesh pbMesh = ShapeGenerator.GenerateCube(
    PivotLocation.Center,
    new Vector3(0.2f, 0.2f, 0.2f)
);
            Vector3 pos = this.transform.parent.position + new Vector3(0, 1f, 0);

            // Optional: position it
            pbMesh.transform.position = pos;
            MeshCollider meshCollider = pbMesh.gameObject.AddComponent<MeshCollider>();
            meshCollider.convex = false;
            meshCollider.sharedMesh = pbMesh.GetComponent<MeshFilter>().mesh;
            Rigidbody rb = pbMesh.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = false;
            pbMesh.gameObject.AddComponent<XRPortalInteractable>();
            //  pbMesh.gameObject.layer = 9;
            // Optional: rebuild mesh if you modify it later
            var mr = pbMesh.GetComponent<MeshRenderer>();
            Material newMat = new Material(mr.sharedMaterial);
           // Material mat = mr.sharedMaterial; //pbMesh.GetComponent<MeshRenderer>().shareMaterial;
            print("Hit Material: " + newMat.name);
            Texture2D tex = new Texture2D(1024, 1024);
            ClearTexture(Color.red, tex);
            mat.SetTexture("_MainTex", tex);

            mr.sharedMaterial = newMat;
//            newMat.mainTexture = tex;
            pbMesh.name= "ProBuilderCube";
             
          
            pbMesh.ToMesh();
            pbMesh.Refresh();
            MeshCollider col = pbMesh.GetComponent<MeshCollider>();
            col.sharedMesh = null;      // force Unity to rebuild
            col.sharedMesh = pbMesh.GetComponent<MeshFilter>().mesh;
            lastpb = pbMesh;
            */
        }

        void ClearTexture(UnityEngine.Color c, Texture2D texture)
        {
            int texSize = texture.width;
            UnityEngine.Color[] pixels = new UnityEngine.Color[texSize * texSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = c;

            texture.SetPixels(pixels);
            texture.Apply();
        }

 
        GameObject createCube()
        {
            Mesh mesh = new Mesh();
            mesh.name = "RuntimeCube";

            // 8 vertices
            Vector3[] v =
            {
            new Vector3(-0.5f,-0.5f,-0.5f), // 0
            new Vector3( 0.5f,-0.5f,-0.5f), // 1
            new Vector3( 0.5f, 0.5f,-0.5f), // 2
            new Vector3(-0.5f, 0.5f,-0.5f), // 3

            new Vector3(-0.5f,-0.5f, 0.5f), // 4
            new Vector3( 0.5f,-0.5f, 0.5f), // 5
            new Vector3( 0.5f, 0.5f, 0.5f), // 6
            new Vector3(-0.5f, 0.5f, 0.5f)  // 7
        };
            Vector3[] vertices = {
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),

            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),

            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),

            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f),

            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
        };

            // 12 triangles (two per face)
            int[] t =
            {
                /*
            // Back
            0,1,2, 0,2,3,
            // Front
            4,6,5, 4,7,6,
            // Left
            0,3,7, 0,7,4,
            // Right
            1,6,2, 1,5,6,
            // Top
            3,6,7, 3,2,6,
            // Bottom
            0,5,1, 0,4,5
            */
                        // Back
            0,2,1, 0,3,2,
            // Front
            4,5,6, 4,6,7,
            // Left
            0,7,3, 0,4,7,
            // Right
            1,2,6, 1,6,5,
            // Top
            3,7,6, 3,6,2,
            // Bottom
            0,1,5, 0,5,4
            
        };

            int[] triangles = {
            0, 2, 1, // front
			1, 2, 3,
            4, 5, 6, // back
			5, 7, 6,
            6, 7, 8, //top
			7, 9 ,8,
            1, 3, 4, //bottom
			3, 5, 4,
            1, 11,10,// left
			1, 4, 11,
            3, 12, 5,//right
			5, 12, 13


        };
            // Normals (pointing outwards)
            Vector3[] n = new Vector3[v.Length];
            for (int i = 0; i < n.Length; i++)
                n[i] = v[i].normalized;

            // UVs (simple cube projection)
            /*
            Vector2[] uv =
            {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1),

            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1)
        };
            */
            /*
            Vector2[] uv = new Vector2[]
{
    // FRONT
    new Vector2(0.33f, 0f), new Vector2(0.66f, 0f),
    new Vector2(0.66f, 0.5f), new Vector2(0.33f, 0.5f),

    // BACK
    new Vector2(0f, 0f), new Vector2(0.33f, 0f),
    new Vector2(0.33f, 0.5f), new Vector2(0f, 0.5f),

    // LEFT
    new Vector2(0.66f, 0f), new Vector2(1f, 0f),
    new Vector2(1f, 0.5f), new Vector2(0.66f, 0.5f),

    // RIGHT
    new Vector2(0.33f, 0.5f), new Vector2(0.66f, 0.5f),
    new Vector2(0.66f, 1f), new Vector2(0.33f, 1f),

    // TOP
    new Vector2(0.66f, 0.5f), new Vector2(1f, 0.5f),
    new Vector2(1f, 1f), new Vector2(0.66f, 1f),

    // BOTTOM
    new Vector2(0f, 0.5f), new Vector2(0.33f, 0.5f),
    new Vector2(0.33f, 1f), new Vector2(0f, 1f)
};

            */

            Vector2[] uvs = {
            new Vector2(0, 0.66f),
            new Vector2(0.25f, 0.66f),
            new Vector2(0, 0.33f),
            new Vector2(0.25f, 0.33f),

            new Vector2(0.5f, 0.66f),
            new Vector2(0.5f, 0.33f),
            new Vector2(0.75f, 0.66f),
            new Vector2(0.75f, 0.33f),

            new Vector2(1, 0.66f),
            new Vector2(1, 0.33f),

            new Vector2(0.25f, 1),
            new Vector2(0.5f, 1),

            new Vector2(0.25f, 0),
            new Vector2(0.5f, 0),
        };

            Vector2[] uv = {
            new Vector2(0, 0.66f),
            new Vector2(0.25f, 0.66f),
            new Vector2(0, 0.33f),
            new Vector2(0.25f, 0.33f),

            new Vector2(0.5f, 0.66f),
            new Vector2(0.5f, 0.33f),
            new Vector2(0.75f, 0.66f),
            new Vector2(0.75f, 0.33f) //,

         //   new Vector2(1, 0.66f),
         //   new Vector2(1, 0.33f),

        //    new Vector2(0.25f, 1),
        //    new Vector2(0.5f, 1),

         //   new Vector2(0.25f, 0),
        //    new Vector2(0.5f, 0)
        };
            mesh.vertices = v;
            mesh.triangles = t;
            mesh.normals = n;
            mesh.uv = uv;

            mesh.RecalculateBounds();

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.Optimize();
            mesh.RecalculateNormals();

            // Create GameObject
            GameObject go = new GameObject("RuntimeCube");
            go.transform.position = Vector3.zero;

           

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();


            Material matL = mr.material;
            mf.mesh = mesh;
            //   Material newMat = new Material(mat);
            MeshCollider meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.convex = false;


            Texture2D tex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            ClearTexture(UnityEngine.Color.red,tex);


            matL.mainTexture = tex;
            mr.material = matL;
            return (go);
        }
    

}
}
