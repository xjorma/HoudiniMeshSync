import Combine
import Dispatch

class SceneModelView: ObservableObject {
    @Published var meshData: MeshData?
    @Published var rotation: Float = 0
    @Published var scale: Float = 1

    
    private var meshReceiver: MeshReceiver
    
    init() {
        self.meshReceiver = MeshReceiver()
        self.setupReceiver()
    }
    
    public func ZoomIn()
    {
        scale *= 1.1;
    }
    
    public func ZoomOut()
    {
        scale /= 1.1
    }
    
    public func ZoomFit()
    {
        // Ensure meshData is not nil
        guard let meshData = meshData else {
            return
        }
        // Flatten the array of SIMD3<Float> to a single array of Float containing all components
        let allComponents = meshData.vertices.flatMap { [$0.x, $0.y, $0.z] }

        // Find the maximum absolute value using the max function
        let maxValue = allComponents.map { abs($0) }.max() ?? 0.0
        
        scale =  0.35 * 1.0 / maxValue
    }

    public func TurnLeft()
    {
        rotation -= 10;
    }
    
    public func TurnRight()
    {
        rotation += 10;
    }
    
    
    private func setupReceiver() {
        meshReceiver.onDataReceived = { [weak self] data in
            DispatchQueue.main.async
            {
                self?.meshData = data
                print("Scene model view dispatch")
            }
        }
        meshReceiver.startListening(port: 8666)
    }
}
