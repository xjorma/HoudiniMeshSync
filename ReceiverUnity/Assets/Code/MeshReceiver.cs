using UnityEngine;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System;

public class MeshReceiver : MonoBehaviour
{
    [SerializeField] private int port = 8666;
    private Thread receiverThread;
    private MeshFilter meshFilter;
    private ConcurrentQueue<Tuple<int[],Vector3[], Vector3[], Color[]>> meshDataQueue = new ConcurrentQueue<Tuple<int [],Vector3[], Vector3[], Color[]>>();

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        // Start a new thread to receive data
        receiverThread = new Thread(new ThreadStart(Receiver));
        receiverThread.Start();
    }

    Vector3[] ReadVector3Array(byte[] buffer, ref int offset)
    {
        int size = BitConverter.ToInt32(buffer, offset) / 3;
        offset += 4;
        Vector3[] result = new Vector3[size];
        for (int i = 0; i < size; i++)
        {
            result[i] = new Vector3(
                BitConverter.ToSingle(buffer, offset) * -1.0f,  // Flip x-axis
                BitConverter.ToSingle(buffer, offset + 4),
                BitConverter.ToSingle(buffer, offset + 8)
            );
            offset += 12;
        }
        return result;
    }

    Color[] ReadColorArray(byte[] buffer, ref int offset)
    {
        int size = BitConverter.ToInt32(buffer, offset) / 3;
        offset += 4;
        Color[] result = new Color[size];
        for (int i = 0; i < size; i++)
        {
            result[i] = new Color(
                (float)buffer[offset] / 255.0f,
                (float)buffer[offset + 1] / 255.0f,
                (float)buffer[offset + 2] / 255.0f 
            );
            offset += 3;
        }
        return result;
    }

    int[] ReadIntArray(byte[] buffer, ref int offset)
    {
        int size = BitConverter.ToInt32(buffer, offset);
        offset += 4;
        int[] result = new int[size];
        for (int i = 0; i < size; i++)
        {
            result[i] = BitConverter.ToInt32(buffer, offset);
            offset += 4;
        }
        return result;
    }

    void Receiver()
    {
        TcpListener listener = new(IPAddress.Any, port);
        listener.Start();
        Debug.Log("Server is listening");
        while (true)
        {
            Vector3[] points = null;
            Vector3[] normals = null;
            Color[] colors = null;
            int[] indices = null;
            using (TcpClient client = listener.AcceptTcpClient())
            {
                Console.WriteLine("Client connected.");

                using (NetworkStream stream = client.GetStream())
                {
                    // Read the length of the data (first 4 bytes)
                    byte[] lengthBuffer = new byte[4];
                    int bytesRead = stream.Read(lengthBuffer, 0, lengthBuffer.Length);
                    if (bytesRead != lengthBuffer.Length)
                    {
                        Console.WriteLine("Failed to read the data length.");
                        return;
                    }

                    int dataLength = BitConverter.ToInt32(lengthBuffer, 0) - 4;
                    Console.WriteLine($"Data length: {dataLength} bytes");

                    // Read the actual data
                    byte[] dataBuffer = new byte[dataLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < dataLength)
                    {
                        int read = stream.Read(dataBuffer, totalBytesRead, dataLength - totalBytesRead);
                        if (read == 0)
                        {
                            Console.WriteLine("Socket connection broken.");
                            return;
                        }
                        totalBytesRead += read;
                        Console.WriteLine($"Received {totalBytesRead} bytes");
                    }

                    Console.WriteLine($"Received data: {totalBytesRead}");
                    int offset = 0;
                    indices = ReadIntArray(dataBuffer, ref offset);
                    points = ReadVector3Array(dataBuffer, ref offset);
                    normals = ReadVector3Array(dataBuffer, ref offset);
                    colors = ReadColorArray(dataBuffer, ref offset);
                }
            }
            meshDataQueue.Enqueue(Tuple.Create(indices, points, normals, colors));
        }
    }

    void Update()
    {
        while (meshDataQueue.TryDequeue(out var meshData))
        {
            int[] indices = meshData.Item1;
            Vector3[] points = meshData.Item2;
            Vector3[] normals = meshData.Item3;
            Color[] colors = meshData.Item4;

            Mesh mesh = new Mesh();
            if (points.Length > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.vertices = points;
            mesh.normals = normals;
            mesh.triangles = indices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            meshFilter.mesh = mesh;
        }
    }

    private void OnApplicationQuit()
    {
        receiverThread.Abort();
        receiverThread = null;
    }
}
