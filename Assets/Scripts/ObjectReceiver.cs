using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Goes in the separate application (with ObjectMessage.cs and serial.cs). Listens on a local
/// TCP port and builds every GameObject that ObjectSender sends, at the position and rotation
/// that came with it.
///
/// The port is taken from the "-objectPort" command-line argument that ObjectSender passes,
/// or from the Inspector value when the app is started by hand.
/// </summary>
public class ObjectReceiver : MonoBehaviour
{
    public int port = 5055;
    public Transform spawnParent;   // optional: built objects are put under this

    // Socket work happens on background threads; Unity objects can only be made on the main
    // thread, so messages wait here until Update picks them up.
    private readonly ConcurrentQueue<string> received = new ConcurrentQueue<string>();
    private readonly List<TcpClient> clients = new List<TcpClient>();
    private TcpListener listener;
    private Thread acceptThread;
    private volatile bool running;

    private void Start()
    {
        // keep building objects even when this window is not in front
        Application.runInBackground = true;
        port = GetPortArgument(port);

        // objects pasted through a VRPortalToolkit remote portal are finished off here
        // (through any of them, including portals to the other applications)
        foreach (VRPortalToolkit.Remote.RemotePortalEnd end in FindObjectsOfType<VRPortalToolkit.Remote.RemotePortalEnd>())
            end.objectArrived += OnPortalObjectArrived;

        try
        {
            // loopback only: nothing outside this computer can send objects in
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (SocketException e)
        {
            Debug.LogWarning("ObjectReceiver: could not listen on port " + port + ": " + e.Message);
            return;
        }

        running = true;
        acceptThread = new Thread(AcceptLoop);
        acceptThread.IsBackground = true;
        acceptThread.Start();
        print("ObjectReceiver: listening on port " + port);
    }

    private void Update()
    {
        while (received.TryDequeue(out string json))
            BuildObject(json);
    }

    private void BuildObject(string json)
    {
        ObjectMessage message;
        try
        {
            message = JsonUtility.FromJson<ObjectMessage>(json);
        }
        catch (ArgumentException e)
        {
            Debug.LogWarning("ObjectReceiver: message was not valid JSON: " + e.Message);
            return;
        }
        if (message == null)
            return;
        if (message.isAssembly)
        {
            LoadAssembly(message.assemblyName, message.assemblyBase64);
            return;
        }
        if (string.IsNullOrEmpty(message.objectJson))
            return;

        // links to objects in the sending app mean nothing here, so they are cleared
        GameObject go = serial.FromJson(message.objectJson, false);
        if (go == null)
        {
            Debug.LogWarning("ObjectReceiver: could not build the object");
            return;
        }
        if (spawnParent != null)
            go.transform.SetParent(spawnParent, true);
        go.transform.SetPositionAndRotation(message.position, message.rotation);
        print("ObjectReceiver: built " + go.name + " at " + message.position);
    }

    /// <summary>
    /// Loads a script DLL sent by ObjectSender, so serial.FromJson can add its scripts.
    /// Only works in a Mono build (not IL2CPP). A DLL that is already loaded is kept as it is:
    /// a running app cannot swap code, so restart this app to pick up a changed version.
    /// </summary>
    private void LoadAssembly(string name, string base64)
    {
        foreach (System.Reflection.Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (loaded.GetName().Name == name)
            {
                print("ObjectReceiver: " + name + " is already loaded (restart to use a newer version)");
                return;
            }
        }

        try
        {
            // loaded from a file (like a mod), which is the most dependable way for Unity to
            // accept components from code that arrives while the app is running. Each running
            // copy of this application has its own folder: a loaded DLL is locked, so a second
            // copy could not write over the first one's.
            string folder = System.IO.Path.Combine(Application.temporaryCachePath, "SharedAssemblies",
                System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            System.IO.Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, name + ".dll");
            System.IO.File.WriteAllBytes(path, Convert.FromBase64String(base64));
            System.Reflection.Assembly.LoadFrom(path);
            print("ObjectReceiver: loaded scripts from " + name);
        }
        catch (Exception e)
        {
            Debug.LogWarning("ObjectReceiver: could not load " + name + ": " + e.Message);
        }
    }

    private void AcceptLoop()
    {
        while (running)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                break;   // the listener was stopped
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            lock (clients)
                clients.Add(client);
            Thread readThread = new Thread(() => ReadLoop(client));
            readThread.IsBackground = true;
            readThread.Start();
        }
    }

    private void ReadLoop(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            while (running)
            {
                string json = ObjectMessage.ReadFrame(stream);
                if (json == null)
                    break;   // the sender disconnected
                received.Enqueue(json);
            }
        }
        catch (Exception e)
        {
            if (running)
                Debug.LogWarning("ObjectReceiver: connection lost: " + e.Message);
        }
        finally
        {
            lock (clients)
                clients.Remove(client);
            client.Close();
        }
    }

    private static int GetPortArgument(int fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-objectPort" && int.TryParse(args[i + 1], out int value))
                return value;
        }
        return fallback;
    }

    /// <summary>
    /// An object pasted onto the remote portal in the VR app (WindowingSystem) has landed where
    /// the paste ray carried on to. As a paste does there, it is lifted to rest on the surface,
    /// and given the paste format (sent as the object's tag): Unformatted, Source or Destination.
    /// Objects carried or thrown through are left as the portal placed them.
    /// </summary>
    private void OnPortalObjectArrived(VRPortalToolkit.Remote.RemoteArrival arrival)
    {
        if (arrival.message.carried || arrival.gameObject == null)
            return;
        GameObject go = arrival.gameObject;
        if (spawnParent != null)
            go.transform.SetParent(spawnParent, true);
        if (!arrival.landed)
            return;
        PlaceOnPoint(go, arrival.hit.point);

        string format = arrival.message.tag;
        Renderer destination = arrival.hit.collider != null ? arrival.hit.collider.GetComponentInParent<Renderer>() : null;
        if (format == "Destination" && (destination == null || destination.sharedMaterial == null))
            format = "Source";   // nothing to take a material from, so keep the source look
        if (format != "Unformatted" && format != "Destination")
            return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterial == null)
                continue;
            // Unformatted: same shader with its default values. Destination: the surface's material.
            r.sharedMaterial = format == "Unformatted"
                ? new Material(r.sharedMaterial.shader)
                : new Material(destination.sharedMaterial);
        }
    }

    /// <summary>
    /// Puts an object so it rests just above a point instead of overlapping the surface there
    /// (the physics engine would shove it out and send it flying). A spinning object is lifted
    /// clear of its whole turning circle. Matches WindowingSystem.PlaceOnPoint.
    /// </summary>
    private static void PlaceOnPoint(GameObject target, Vector3 point)
    {
        const float gap = 0.005f;
        Transform root = target.transform;
        root.position = point;

        float lowest = 0f;          // lowest point, relative to the object's origin
        float furthest = 0f;        // furthest point from the origin in any direction
        bool found = false;
        foreach (MeshFilter mf in target.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null)
                continue;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 offset = mf.transform.TransformPoint(corner) - root.position;
                lowest = found ? Mathf.Min(lowest, offset.y) : offset.y;
                furthest = Mathf.Max(furthest, offset.magnitude);
                found = true;
            }
        }
        if (!found)
            return;

        // SpinX/SpinY come from the shared DLL, which this app may only have loaded at runtime
        bool spins = false;
        foreach (MonoBehaviour behaviour in target.GetComponentsInChildren<MonoBehaviour>())
        {
            string name = behaviour != null ? behaviour.GetType().Name : "";
            if (name == "SpinX" || name == "SpinY")
                spins = true;
        }
        float lift = spins ? furthest : -lowest;
        root.position = point + Vector3.up * (lift + gap);
    }

    private void OnDestroy()
    {
        running = false;
        foreach (VRPortalToolkit.Remote.RemotePortalEnd end in FindObjectsOfType<VRPortalToolkit.Remote.RemotePortalEnd>())
            end.objectArrived -= OnPortalObjectArrived;
        if (listener != null)
            listener.Stop();
        lock (clients)
        {
            foreach (TcpClient client in clients)
                client.Close();
            clients.Clear();
        }
    }
}
