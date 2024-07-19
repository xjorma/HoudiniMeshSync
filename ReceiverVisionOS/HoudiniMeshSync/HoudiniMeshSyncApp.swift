//
//  HoudiniMeshSyncApp.swift
//  HoudiniMeshSync
//
//  Created by David Gallardo on 2024-06-30.
//

import SwiftUI

@main
struct HoudiniMeshSyncApp: App {
    let sceneModelView = SceneModelView()
    var body: some Scene {
        WindowGroup {
            ContentView(sceneModelView: sceneModelView)
            .toolbar()
            {
                /*
                ToolbarItemGroup(placement: .bottomOrnament)
                {
                    Button("Tool")
                    {
                    }
                }
                 */
                ToolbarItemGroup(placement: .bottomOrnament)
                {
                    Button("Rotate Left")
                    {
                        sceneModelView.TurnLeft()
                    }
                    Button("Rotate Right")
                    {
                        sceneModelView.TurnRight()
                    }
                    Button("Zoom In")
                    {
                        sceneModelView.ZoomIn()
                    }
                    Button("Zoom Fit")
                    {
                        sceneModelView.ZoomFit()
                    }
                    Button("Zoom Out")
                    {
                        sceneModelView.ZoomOut()
                    }
                }
            }
        }.windowStyle(.volumetric)
    }
}
