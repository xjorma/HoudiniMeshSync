import Foundation
import RealityKit

struct MeshData: Equatable {
    var vertices: [SIMD3<Float>]
    var normals: [SIMD3<Float>]
    var indices: [UInt32]
    
    static func == (lhs: MeshData, rhs: MeshData) -> Bool {
        return lhs.vertices == rhs.vertices &&
               lhs.normals == rhs.normals &&
               lhs.indices == rhs.indices
    }
}
