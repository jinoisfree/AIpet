import Foundation
import ServiceManagement

/// Opens the pet when the user logs in. On by default; the menu switch is the user's choice and is kept across launches.
enum LaunchAtLogin {
    static let settingsKey = "launchAtLogin"

    static var enabled: Bool {
        get { UserDefaults.standard.object(forKey: settingsKey) == nil ? true : UserDefaults.standard.bool(forKey: settingsKey) }
        set { UserDefaults.standard.set(newValue, forKey: settingsKey); apply() }
    }

    /// Registers or removes this bundle as a login item. Registering again also follows the app to its current location.
    static func apply() {
        let service = SMAppService.mainApp
        do {
            if enabled { if service.status != .enabled { try service.register() } }
            else if service.status == .enabled { try service.unregister() }
        } catch {
            NSLog("AIpet login item update failed: \(error.localizedDescription)")
        }
    }
}
