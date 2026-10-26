using System;
using System.IO;
using System.Net;
using UnityEngine;

/// <summary>
/// What ObjectSender sends to ObjectReceiver. Both applications need this file and serial.cs.
/// A message is one of:
///  - an object: a GameObject as JSON (made by serial.ToJson) and where to put it
///  - code: a script DLL (assemblyName + assemblyBase64) for the receiver to load, so it can
///    add scripts it was not built with. These are sent before any objects that use them.
///
/// On the socket every message is a 4-byte length (network byte order) followed by that many
/// bytes of UTF-8 JSON.
/// </summary>
[Serializable]
public class ObjectMessage
{
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public string objectJson;

    public string assemblyName;     // e.g. "SharedBehaviours"; empty for an object message
    public string assemblyBase64;   // the DLL file's bytes

    public bool isAssembly => !string.IsNullOrEmpty(assemblyBase64);

    // a message bigger than this is treated as a broken connection rather than read
    public const int MaxBytes = 256 * 1024 * 1024;

    public static byte[] ToFrame(ObjectMessage message)
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        byte[] length = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(body.Length));
        byte[] frame = new byte[length.Length + body.Length];
        Buffer.BlockCopy(length, 0, frame, 0, length.Length);
        Buffer.BlockCopy(body, 0, frame, length.Length, body.Length);
        return frame;
    }

    /// <summary>
    /// Reads one message's JSON from the stream. Returns null when the other side has closed
    /// the connection. Blocks until a whole message has arrived, so call it off the main thread.
    /// </summary>
    public static string ReadFrame(Stream stream)
    {
        byte[] lengthBytes = new byte[4];
        if (!ReadExactly(stream, lengthBytes, 4))
            return null;
        int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));
        if (length < 0 || length > MaxBytes)
            throw new InvalidDataException("Object message length " + length + " is not valid");

        byte[] body = new byte[length];
        if (!ReadExactly(stream, body, length))
            return null;
        return System.Text.Encoding.UTF8.GetString(body);
    }

    private static bool ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0)
                return false;   // closed part way through
            read += n;
        }
        return true;
    }
}
