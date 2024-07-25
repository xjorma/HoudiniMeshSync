# HoudiniMeshSync

## Apple Vision Pro
Sync mesh between Houdini and the Apple Vision Pro. Run Houdini on a Mac computer, sending the mesh through the network to your Vision Pro.

![Title](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/RPReplay_Final1721394613.jpg)
https://www.youtube.com/watch?v=kyJoJuaRGVE

### HDA Setup

![HdaSetup](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/hdasetting.png)

- **IP Address**: The IP address of your Apple Vision Pro.
- **Port**: Port used for network communication. If you change it, you will also have to change it in the client code.
- **ChunkSize**: Mesh is sliced by chunks before sending. **8192** is a good value.
- **Invert Triangle**: Invert triangle orientation. Mainly used for debugging purposes.
- **Verbose**: Logs information if the transfer is successful.

### TODO (Might take time since I don't own a Vision Pro yet)

- <s>Optimize file transfer (At least better share the vertex). </s> **(Done)**
- Use 16bits indices in less than 2<sup>16</sup> vertices.
- Use hand gestures to rotate and zoom.
- Have nice round buttons instead of text (Better for eye tracking).
- Support for optional vertex color (Cd).
