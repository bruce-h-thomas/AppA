using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Starts a separate Unity application, connects to it over a local TCP socket and sends it
/// GameObjects to build. The other application needs ObjectReceiver, ObjectMessage and serial.cs.
///
/// Typical use from another script:
///     await sender.StartAndConnect();
///     await sender.SendObject(someObject, new Vector3(0, 1, 2), Quaternion.identity);
///
/// Starting another program only works when this app runs on a PC (the Editor or PC VR),
/// not as a standalone Android/Quest build.
/// </summary>
public class ObjectSender : MonoBehaviour
{
    public string receiverExePath = "";        // the other application's .exe
    public string receiverArguments = "";      // extra command-line arguments for it
    public string host = "127.0.0.1";
    public int port = 5055;                    // passed to the receiver as -objectPort
    public float connectTimeout = 30f;         // seconds to keep trying while the receiver starts up
    public bool closeReceiverOnExit = true;

    private Process receiverProcess;
    private TcpClient client;
    private NetworkStream stream;
    private bool sending;

    public bool isConnected => client != null && client.Connected && stream != null;

    /// <summary>
    /// Starts the receiver application. Returns false if it could not be started.
    /// </summary>
    public bool StartReceiverApp()
    {
        if (receiverProcess != null && !receiverProcess.HasExited)
            return true;   // already running
        if (string.IsNullOrEmpty(receiverExePath))
        {
            UnityEngine.Debug.LogWarning("ObjectSender: set Receiver Exe Path first");
            return false;
        }

        try
        {
            ProcessStartInfo info = new ProcessStartInfo(receiverExePath,
                (receiverArguments + " -objectPort " + port).Trim());
            info.UseShellExecute = false;
            receiverProcess = Process.Start(info);
            print("ObjectSender: started " + receiverExePath);
            return receiverProcess != null;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("ObjectSender: could not start " + receiverExePath + ": " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// Connects to the receiver, retrying until it is listening or connectTimeout runs out
    /// (the receiver takes a few seconds to start). Returns true once connected.
    /// </summary>
    public async Task<bool> ConnectToReceiver()
    {
        if (isConnected)
            return true;

        float giveUpAt = Time.realtimeSinceStartup + connectTimeout;
        while (Time.realtimeSinceStartup < giveUpAt)
        {
            TcpClient attempt = new TcpClient();
            try
            {
                await attempt.ConnectAsync(host, port);
                client = attempt;
                client.NoDelay = true;
                stream = client.GetStream();
                print("ObjectSender: connected to " + host + ":" + port);
                return true;
            }
            catch (SocketException)
            {
                attempt.Close();
                // not listening yet; stop early if the receiver has already quit
                if (receiverProcess != null && receiverProcess.HasExited)
                    break;
                await Task.Delay(500);
            }
        }
        UnityEngine.Debug.LogWarning("ObjectSender: could not connect to " + host + ":" + port);
        return false;
    }

    /// <summary>
    /// Starts the receiver application and connects to it.
    /// </summary>
    public async Task<bool> StartAndConnect()
    {
        if (!StartReceiverApp())
            return false;
        return await ConnectToReceiver();
    }

    /// <summary>
    /// Sends a GameObject to the receiver, which builds it at the given position and rotation
    /// (in the receiver's world). Returns false if it could not be sent.
    /// </summary>
    public async Task<bool> SendObject(GameObject go, Vector3 position, Quaternion rotation)
    {
        if (go == null)
            return false;
        if (!isConnected)
        {
            UnityEngine.Debug.LogWarning("ObjectSender: not connected");
            return false;
        }

        ObjectMessage message = new ObjectMessage();
        message.position = position;
        message.rotation = rotation;
        message.objectJson = serial.ToJson(go);   // must happen on the main thread
        return await SendFrame(ObjectMessage.ToFrame(message), go.name);
    }

    /// <summary>
    /// Sends a JSON string already made by serial.ToJson, e.g. the copied object from Copy.
    /// </summary>
    public async Task<bool> SendObjectJson(string objectJson, Vector3 position, Quaternion rotation)
    {
        if (string.IsNullOrEmpty(objectJson) || !isConnected)
            return false;

        ObjectMessage message = new ObjectMessage();
        message.position = position;
        message.rotation = rotation;
        message.objectJson = objectJson;
        return await SendFrame(ObjectMessage.ToFrame(message), "object");
    }

    private async Task<bool> SendFrame(byte[] frame, string what)
    {
        // a texture can make a message several MB; sending one at a time keeps frames whole
        while (sending)
            await Task.Yield();
        sending = true;
        try
        {
            await stream.WriteAsync(frame, 0, frame.Length);
            print("ObjectSender: sent " + what + " (" + frame.Length + " bytes)");
            return true;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("ObjectSender: send failed: " + e.Message);
            Disconnect();
            return false;
        }
        finally
        {
            sending = false;
        }
    }

    public void Disconnect()
    {
        if (stream != null)
            stream.Close();
        if (client != null)
            client.Close();
        stream = null;
        client = null;
    }

    /// <summary>
    /// Closes the receiver application (politely first, then forcefully).
    /// </summary>
    public void StopReceiverApp()
    {
        Disconnect();
        if (receiverProcess == null)
            return;
        try
        {
            if (!receiverProcess.HasExited)
            {
                receiverProcess.CloseMainWindow();
                if (!receiverProcess.WaitForExit(2000))
                    receiverProcess.Kill();
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("ObjectSender: could not close the receiver: " + e.Message);
        }
        receiverProcess.Dispose();
        receiverProcess = null;
    }

    private void OnDestroy()
    {
        if (closeReceiverOnExit)
            StopReceiverApp();
        else
            Disconnect();
    }
}
