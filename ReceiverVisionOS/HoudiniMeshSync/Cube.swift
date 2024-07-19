//
//  Cube.swift
//  FirstTest
//
//  Created by David Gallardo on 2024-04-23.
//

import RealityKit

// Function to create a procedural cube mesh
func createProceduralCube(size: Float) -> MeshResource {
    var descriptor = MeshDescriptor()
    // Define the vertices
    descriptor.positions = MeshBuffers.Positions([
        SIMD3<Float>(-size, -size, -size), // Bottom-left-front
        SIMD3<Float>( size, -size, -size), // Bottom-right-front
        SIMD3<Float>( size,  size, -size), // Top-right-front
        SIMD3<Float>(-size,  size, -size), // Top-left-front
        SIMD3<Float>(-size, -size,  size), // Bottom-left-back
        SIMD3<Float>( size, -size,  size), // Bottom-right-back
        SIMD3<Float>( size,  size,  size), // Top-right-back
        SIMD3<Float>(-size,  size,  size)  // Top-left-back
    ])

    // Define the indices for faces (quads)
    descriptor.primitives = .triangles([
        1, 0, 2, 3, 2, 0, // Front face
        4, 5, 6, 6, 7, 4, // Back face
        0, 4, 7, 7, 3, 0, // Left face
        5, 1, 6, 2, 6, 1, // Right face
        2, 3, 6, 7, 6, 3, // Top face
        0, 1, 5, 5, 4, 0  // Bottom face
    ])

    // Create and return the MeshResource
    return try! MeshResource.generate(from: [descriptor])
    //return MeshResource.generateBox(size: size)
}
