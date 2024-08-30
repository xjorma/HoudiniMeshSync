# HoudiniMeshSync

**Disclaimer**: This was developed as a proof of concept, but if people find it useful, I might spend time to develop it further into a more polished product.

## Apple Vision Pro

Sync mesh between Houdini and the Apple Vision Pro. Run Houdini or blender on a Mac computer, sending the mesh through the network to your Vision Pro.

![Title](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/RPReplay_Final1721394613.jpg)
https://www.youtube.com/watch?v=kyJoJuaRGVE

## Looking Glass Factory

![Title](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/lookingglassthumb.jpg)
https://youtu.be/kyJoJuaRGVE

Compile and run `ReceiverUnityLookingGlass` and you are ready to share with Houdini or Blender

## Meta Quest

![Title](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/metathumb.jpg)
https://youtube.com/shorts/3UlCPRU08R4


Compile and run `ReceiverUnityMetaQuest` (Works on PC and also on standalone).  
If you are using it on standalone, don't forget to set your Quest's IP address in Houdini or Blender.

For screen casting, install [NDI](https://ndi.video/tools/), launch **Screen Capture** (enable the mouse cursor capture in "Capture Settings" from the tray icon), and use the **Router** to create an output called **"ScreenCast"**.


## HDA Setup

![HdaSetup](https://github.com/xjorma/HoudiniMeshSync/blob/main/Images/hdasetting.png)

- **IP Address**: The IP address of your Apple Vision Pro.
- **Port**: Port used for network communication. If you change it, you will also have to change it in the client code.
- **ChunkSize**: Mesh is sliced by chunks before sending. **8192** is a good value.
- **Invert Triangle**: Invert triangle orientation. Mainly used for debugging purposes.
- **Verbose**: Logs information if the transfer is successful.

## TODO (Might take time since I don't own a Vision Pro yet)

- <s>Optimize file transfer (At least better share the vertex). </s> **(Done)**
- Use 16bits indices in less than 2<sup>16</sup> vertices.
- Use hand gestures to rotate and zoom.
- Have nice round buttons instead of text (Better for eye tracking).
- Support for optional vertex color (Cd).
