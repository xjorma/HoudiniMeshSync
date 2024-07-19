import Foundation
import Network

class MeshReceiver {
    private var listener: NWListener?
    var onDataReceived: ((MeshData) -> Void)?
    
    func startListening(port: UInt16) {
        do {
            listener = try NWListener(using: .tcp, on: NWEndpoint.Port(integerLiteral: port))
        } catch {
            print("Unable to create listener: \(error)")
            return
        }
        
        listener?.newConnectionHandler = { [weak self] connection in
            connection.start(queue: .global())
            self?.receive(on: connection)
        }
        
        listener?.start(queue: .global())
        print("Server is listening on port \(port)")
    }
    
    private func receive(on connection: NWConnection)
    {
        // receive length
        connection.receive(minimumIncompleteLength: 4, maximumLength: 4) { [weak self] data, _, _, error in
            guard let self = self, let data = data else {
                if let error = error {
                    print("Receive error: \(error)")
                }
                return
            }
            
            let dataLength = Int(data.withUnsafeBytes { $0.load(as: Int32.self) }) - 4;
            print("Data length: \(dataLength) bytes");
            
            let meshByteData = self.receiveData(on: connection, length: dataLength);
        }
    }

    private func receiveData(on connection: NWConnection, length: Int)
    {
        var receivedData = Data()
        func receiveChunk()
        {
            let remainingLength = length - receivedData.count;
            connection.receive(minimumIncompleteLength: 1, maximumLength: remainingLength) { [weak self] data, _, _, error in
                guard let self = self, let data = data else {
                    if let error = error {
                        print("Receive error: \(error)")
                    }
                    return
                }

                receivedData.append(data)
                if receivedData.count < length
                {
                    print("Received chunk of size\(receivedData.count) bytes")
                    receiveChunk()
                } 
                else
                {
                    assert(receivedData.count == length, "Size should be the same!")
                    print("Received total data of size: \(receivedData.count) bytes")
                    let meshData = self.parseMeshData(from: receivedData)
                    self.onDataReceived?(meshData)
                }
            }
        }
        receiveChunk()
    }
    
    private func parseMeshData(from data: Data) -> MeshData{
        var offset = 0;
        
        func readIndicesArray() -> [UInt32] {
            let count = Int(data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: UInt32.self) }) / 3
            offset += 4
            var array = [UInt32](repeating: 0, count: count * 3)
            for i in 0..<count 
            {
                let id0 = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: UInt32.self) }
                offset += 4
                let id1 = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: UInt32.self) }
                offset += 4
                let id2 = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: UInt32.self) }
                offset += 4

                array[i * 3 + 0] = id1
                array[i * 3 + 1] = id0
                array[i * 3 + 2] = id2
            }
            return array
        }
        
        func readVector3Array() -> [SIMD3<Float>] {
            let count = Int(data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: UInt32.self) }) / 3
            offset += 4
            var array = [SIMD3<Float>](repeating: SIMD3<Float>(0, 0, 0), count: count)
            for i in 0..<count
            {
                let x = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: Float.self) }
                offset += 4
                let y = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: Float.self) }
                offset += 4
                let z = data.withUnsafeBytes { $0.load(fromByteOffset: offset, as: Float.self) }
                offset += 4
                array[i] = SIMD3<Float>(x, y, z)
            }
            return array
        }
                
        let indices = readIndicesArray()
        let vertices = readVector3Array()
        let normals = readVector3Array()
        
        return MeshData(vertices: vertices, normals: normals, indices: indices)
    }
}
