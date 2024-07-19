
import Combine
import SwiftUI
import RealityKit
import RealityKitContent

struct ContentView: View {
    @ObservedObject var sceneModelView = SceneModelView()
    @State private var myContent: RealityViewContent? = nil
    @State private var myModelEntity: RealityKit.ModelEntity? = nil
    
    func updateTransform() {
        guard let modelEntity = myModelEntity else { return }

        // Create a new transform with the updated rotation and scale
        var transform = modelEntity.transform
        transform.rotation = simd_quatf(angle: sceneModelView.rotation * (Float.pi / 180), axis: SIMD3(x: 0, y: 1, z: 0))
        transform.scale = SIMD3(repeating: sceneModelView.scale)
        
        // Apply the transform to the ModelEntity
        modelEntity.transform = transform
    }

    var body: some View {
        RealityView { content in
            myContent = content
            
            let mesh = createProceduralCube(size: 0.2)
            let material = SimpleMaterial(color: .green, isMetallic: true)
            
            if(myModelEntity != nil)
            {
                content.remove(myModelEntity!)
            }
            // Create a ModelEntity and assign the mesh and material to it
            myModelEntity = ModelEntity(mesh: mesh, materials: [material])
            updateTransform()
            
            // Add the ModelEntity to the content
            content.add(myModelEntity!)
        }
        .onChange(of: sceneModelView.rotation)
        {
            updateTransform()
        }
        .onChange(of: sceneModelView.scale)
        {
            updateTransform()
        }
        .onChange(of: sceneModelView.meshData)
        {
            // Update content when meshData changes
            if let content = myContent
            {
                let mesh = createMesh(from: sceneModelView.meshData!)
                let material = SimpleMaterial(color: .blue, isMetallic: true)
                
                if(myModelEntity != nil)
                {
                    content.remove(myModelEntity!)
                }
                // Create a ModelEntity and assign the mesh and material to it
                myModelEntity = ModelEntity(mesh: mesh, materials: [material])
                updateTransform()
                
                // Add the ModelEntity to the content
                content.add(myModelEntity!)
            }
            else
            {
                print("Hey my content is nil!")
            }
        }
        .onAppear {
            print("RealityView appeared.")
        }
    }
    
    private func createMesh(from meshData: MeshData) -> MeshResource
    {
        var meshDescriptor = MeshDescriptor(name: "DynamicMesh")
        meshDescriptor.positions = MeshBuffers.Positions(meshData.vertices)
        meshDescriptor.normals = MeshBuffers.Normals(meshData.normals)
        meshDescriptor.primitives = .triangles(meshData.indices)
        
        return try! MeshResource.generate(from: [meshDescriptor])
    }
}

#Preview(windowStyle: .volumetric) {
    ContentView()
        .toolbar()
    {
        ToolbarItemGroup(placement: .bottomOrnament)
        {
            Button("Tool")
            {
            }
        }
        ToolbarItemGroup(placement: .bottomOrnament)
        {
            Button("Zoom In")
            {
            }
            Button("Zoom Out")
            {
            }
        }
    }
}

