import AppKit

MainActor.assumeIsolated {
    let app = NSApplication.shared
    app.delegate = App.shared
    app.run()
}
