import AppKit

final class MacAppUpdater: NSObject, NSWindowDelegate {
    private let client = GitHubAppUpdateClient()
    private let defaults: UserDefaults
    private let key = "AIpet.appUpdates.v1"
    private var preferences: AppUpdatePreferences
    private var timer: Timer?
    private var operation: Task<Void, Never>?
    private var busy = false, stopped = false
    private var progressWindow: NSWindow?
    private var progressLabel: NSTextField?
    private var progressBar: NSProgressIndicator?
    var automatic: Bool {
        get { preferences.automatic }
        set { preferences.automatic = newValue; save() }
    }
    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        preferences = defaults.data(forKey: key).flatMap { try? JSONDecoder().decode(AppUpdatePreferences.self, from: $0) } ?? AppUpdatePreferences()
        super.init()
    }
    private func save() { if let data = try? JSONEncoder().encode(preferences) { defaults.set(data, forKey: key) } }
    func start() {
        timer = Timer.scheduledTimer(withTimeInterval: 20, repeats: false) { [weak self] _ in
            guard let self, !self.stopped else { return }
            self.check(manual: false)
            self.timer = Timer.scheduledTimer(withTimeInterval: 15 * 60, repeats: true) { [weak self] _ in self?.check(manual: false) }
        }
    }
    func stop() {
        stopped = true; timer?.invalidate(); operation?.cancel(); progressWindow?.close()
    }
    func check(manual: Bool) {
        guard !busy, !stopped, manual || preferences.checkDue(at: Date()) else { return }
        busy = true
        operation = Task { @MainActor [weak self] in
            guard let self else { return }
            defer { self.busy = false; self.operation = nil }
            var approved = false
            do {
                guard let text = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
                      let current = AppReleaseVersion(text) else { throw AppUpdateError.invalidApp }
                self.preferences.lastCheck = Date(); self.save()
                let update = try await self.client.check(current: current)
                guard !self.stopped, !Task.isCancelled, manual || self.automatic else { return }
                guard let update else {
                    if manual { self.message("현재 최신 버전입니다.", detail: "AIpet \(current)") }
                    return
                }
                guard manual || self.preferences.shouldPrompt(update.version, at: Date()) else { return }
                NSApp.activate(ignoringOtherApps: true)
                let alert = NSAlert(); alert.messageText = "AIpet \(update.version) 버전이 있습니다."
                alert.informativeText = "현재 버전: \(current)\n\n지금 업데이트할까요? 기존 앱을 교체하고 이름·크기·위치 설정은 유지합니다."
                alert.addButton(withTitle: "업데이트"); alert.addButton(withTitle: "나중에")
                alert.buttons[0].keyEquivalent = ""; alert.buttons[1].keyEquivalent = "\r"
                guard alert.runModal() == .alertFirstButtonReturn else {
                    self.preferences.deferVersion(update.version, at: Date()); self.save(); return
                }
                approved = true
                try await self.install(update)
            } catch {
                if !self.stopped, !Task.isCancelled, manual || approved { self.message("업데이트를 완료하지 못했습니다.", detail: error.localizedDescription) }
            }
        }
    }
    @MainActor private func install(_ update: MacAppUpdate) async throws {
        showProgress()
        let download = AppUpdateInstaller.downloads.appendingPathComponent("download-\(UUID().uuidString)", isDirectory: true)
        var prepared: PreparedAppUpdate?
        var handedOff = false
        defer {
            progressWindow?.close(); progressWindow = nil
            if !handedOff {
                if let prepared { try? FileManager.default.removeItem(at: prepared.work) }
                try? FileManager.default.removeItem(at: download)
            }
        }
        let archive = try await client.download(update, directory: download) { [weak self] percent in
            DispatchQueue.main.async {
                self?.progressBar?.doubleValue = Double(percent)
                self?.progressLabel?.stringValue = percent == 100 ? "새 앱을 확인하고 있습니다." : "새 버전을 다운로드하고 있습니다. \(percent)%"
            }
        }
        try Task.checkCancellation()
        let destination = Bundle.main.bundleURL
        prepared = try await Task.detached { try AppUpdateInstaller.prepare(archive: archive, destination: destination, version: update.version) }.value
        try Task.checkCancellation()
        try prepared!.launchHelper()
        handedOff = true
        NSApp.terminate(nil)
    }
    private func message(_ title: String, detail: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = detail; alert.runModal()
    }
    private func showProgress() {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 420, height: 150), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        window.title = "AIpet 업데이트"; window.isReleasedWhenClosed = false; window.delegate = self
        let label = NSTextField(labelWithString: "새 버전을 다운로드하고 있습니다.")
        label.frame = NSRect(x: 24, y: 105, width: 372, height: 20)
        let bar = NSProgressIndicator(frame: NSRect(x: 24, y: 74, width: 372, height: 16))
        bar.isIndeterminate = false; bar.minValue = 0; bar.maxValue = 100
        let cancel = NSButton(title: "취소", target: self, action: #selector(cancelUpdate)); cancel.bezelStyle = .rounded
        cancel.frame = NSRect(x: 306, y: 20, width: 90, height: 30)
        window.contentView?.addSubview(label); window.contentView?.addSubview(bar); window.contentView?.addSubview(cancel)
        progressWindow = window; progressLabel = label; progressBar = bar
        window.center(); NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil)
    }
    @objc private func cancelUpdate() { operation?.cancel() }
    func windowShouldClose(_ sender: NSWindow) -> Bool { operation?.cancel(); return true }
}
