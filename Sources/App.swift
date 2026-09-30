import AppKit
import SwiftUI

@main
struct TaesikMain {
    static func main() {
        if let index = CommandLine.arguments.firstIndex(of: "--hook"), CommandLine.arguments.count > index + 1,
           let provider = Provider(rawValue: CommandLine.arguments[index + 1]) {
            var destination: URL?
            if let i = CommandLine.arguments.firstIndex(of: "--event-root"), CommandLine.arguments.count > i + 1 { destination = URL(fileURLWithPath: CommandLine.arguments[i + 1]) }
            HookBridge.run(provider: provider, destination: destination)
            return
        }
        if CommandLine.arguments.contains("--snapshot") {
            let snapshot = StatusMonitor().poll(forceDiscovery: true)
            let encoder = JSONEncoder(); encoder.dateEncodingStrategy = .iso8601; encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            if let data = try? encoder.encode(snapshot) { FileHandle.standardOutput.write(data); print("") }
            return
        }
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) { app.run() }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    let model = DashboardModel()
    var panel: PetPanel!
    var pet: PetView!
    var dashboard: NSWindow?
    var optionsWindow: NSWindow?
    var sizeSlider: NSSlider?
    var sizeValueLabel: NSTextField?
    var petNameField: NSTextField?
    var petNameHint: NSTextField?
    var statusItem: NSStatusItem!
    var timer: Timer?
    var previewTimer: Timer?
    var menu: NSMenu!
    var isPreview = false
    var currentSnapshot: Snapshot?
    let monitor = StatusMonitor()
    let monitorQueue = DispatchQueue(label: "com.jinoisfree.taesik.monitor", qos: .utility)
    var polling = false
    var bubbleMotion = PetBubbleMotion()
    var placementTarget: PetPlacement?

    func applicationDidFinishLaunching(_ notification: Notification) {
        if NSRunningApplication.runningApplications(withBundleIdentifier: "com.jinoisfree.taesik").filter({ $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }).count > 0 && !CommandLine.arguments.contains("--ui-smoke") {
            NSApp.terminate(nil); return
        }
        do { pet = PetView(atlas: try SpriteAtlas(), identity: model.identity) }
        catch { let alert = NSAlert(error: error); alert.runModal(); NSApp.terminate(nil); return }
        panel = PetPanel(contentRect: NSRect(origin: .zero, size: pet.bounds.size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = false
        panel.level = .floating; panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.contentView = pet
        pet.autoresizingMask = [.width, .height]
        pet.onClick = { [weak self] in self?.showDashboard() }
        pet.onMoved = { [weak self] in self?.constrainPosition(); self?.savePosition() }
        pet.onDragMove = { [weak self] origin in self?.placePet(at: origin) }
        pet.onAnimationTick = { [weak self] in self?.advancePlacement() }
        buildMenu()
        pet.onMenu = { [weak self] event in guard let self else { return }; NSMenu.popUpContextMenu(self.menu, with: event, for: self.pet) }
        model.onSelect = { [weak self] id in self?.model.selectedID = id; self?.updatePet() }
        model.onRefresh = { [weak self] in self?.refresh() }
        model.onPause = { [weak self] in self?.togglePause() }
        model.onPreview = { [weak self] in self?.preview() }
        model.onHooks = { [weak self] in self?.showHooksHelp() }
        restorePosition()
        panel.orderFrontRegardless()
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        refresh()
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in self?.refresh() }
        if CommandLine.arguments.contains("--show-dashboard") { showDashboard() }
        if let index = CommandLine.arguments.firstIndex(of: "--ui-smoke"), CommandLine.arguments.count > index + 1 {
            let destination = URL(fileURLWithPath: CommandLine.arguments[index + 1])
            showDashboard()
            DispatchQueue.main.asyncAfter(deadline: .now() + 3) { [weak self] in
                self?.capture(destination: destination)
                NSApp.terminate(nil)
            }
        }
    }

    func refresh() {
        guard !model.paused, !polling else { return }
        polling = true
        monitorQueue.async { [weak self] in
            guard let self else { return }
            let snapshot = self.monitor.poll()
            DispatchQueue.main.async {
                self.polling = false
                if !self.model.paused { self.apply(snapshot) }
            }
        }
    }

    func apply(_ snapshot: Snapshot) {
        currentSnapshot = snapshot
        model.tasks = snapshot.tasks
        model.connections = snapshot.connections
        model.warning = snapshot.warning
        model.lastScan = snapshot.observedAt
        pet.screenFocus = snapshot.screenFocus
        updatePet()
    }

    func updatePet() {
        statusItem.button?.toolTip = "AIpet.v1 · \(model.identity.name) · 작업 중 \(model.tasks.filter { $0.state == .running }.count)개"
        guard !isPreview else { return }
        if model.paused {
            pet.state = .idle; pet.headline = "\(model.identity.subject) 쉬고 있어요"; pet.subtitle = "메뉴에서 다시 시작할 수 있어요"; return
        }
        let active = model.tasks.filter { $0.state == .running }.count
        let urgent = model.tasks.first { $0.state == .waiting || ($0.state == .failed && Date().timeIntervalSince($0.updatedAt) < 60) }
        let selected = model.tasks.first { $0.id == model.selectedID }
        let focused = model.tasks.first { $0.id == currentSnapshot?.screenFocus?.taskID && $0.state == .running }
        let eligible = model.tasks.first { $0.state == .running || ($0.state == .responded && Date().timeIntervalSince($0.updatedAt) < 20) }
        if let item = urgent ?? selected ?? focused ?? eligible {
            pet.state = item.state
            pet.headline = "\(item.provider.name) · \(item.state.label)"
            pet.subtitle = active > 1 ? "\(active)개 작업 진행 중 · 클릭해서 보기" : "\(item.project.isEmpty ? item.shortID : item.project) · 클릭해서 보기"
            if urgent == nil, let headline = PetTaskSummary.runningHeadline(model.tasks) {
                pet.state = .running; pet.headline = headline
            } else if urgent != nil, active > 0 {
                pet.subtitle = "작업 중: \(PetTaskSummary.runningDetail(model.tasks))"
            }
        } else {
            pet.state = .idle
            let unknown = model.tasks.contains { $0.state == .unknown }
            pet.headline = unknown ? "상태를 확인할 작업이 있어요" : model.identity.idleHeadline
            pet.subtitle = "Codex · Claude Code"
        }
    }

    func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "pawprint.fill", accessibilityDescription: "AIpet.v1")
        menu = NSMenu()
        addMenu("작업 목록 열기", #selector(showDashboard))
        addMenu("펫 보이기 / 숨기기", #selector(togglePet))
        addMenu("잠시 쉬기 / 다시 시작", #selector(togglePause))
        menu.addItem(.separator())
        addMenu("옵션…", #selector(showOptions))
        addMenu("위치 초기화", #selector(resetPosition))
        addMenu("동작 미리보기", #selector(preview))
        addMenu("마우스 시선 미리보기", #selector(previewPointer))
        addMenu("승인 알림 연결 안내", #selector(showHooksHelp))
        menu.addItem(.separator())
        addMenu("AIpet.v1 종료", #selector(quit))
        statusItem.menu = menu
    }
    func addMenu(_ title: String, _ selector: Selector) {
        let item = NSMenuItem(title: title, action: selector, keyEquivalent: ""); item.target = self; menu.addItem(item)
    }
    @objc func showDashboard() {
        if dashboard == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 710, height: 610), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
            window.title = model.identity.dashboardTitle; window.titlebarAppearsTransparent = true
            window.isReleasedWhenClosed = false; window.contentView = NSHostingView(rootView: Dashboard(model: model))
            window.minSize = NSSize(width: 680, height: 560); window.center(); dashboard = window
        }
        NSApp.activate(ignoringOtherApps: true); dashboard?.makeKeyAndOrderFront(nil)
    }
    @objc func togglePet() { if panel.isVisible { panel.orderOut(nil) } else { panel.orderFrontRegardless() } }
    @objc func togglePause() {
        model.paused.toggle(); pet.isPaused = model.paused
        if !model.paused { refresh() }; updatePet()
    }
    @objc func showOptions() {
        if optionsWindow == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 360, height: 280), styleMask: [.titled, .closable], backing: .buffered, defer: false)
            window.title = "AIpet.v1 옵션"; window.isReleasedWhenClosed = false
            let content = NSView(frame: NSRect(x: 0, y: 0, width: 360, height: 280))
            let nameLabel = NSTextField(labelWithString: "펫 이름")
            nameLabel.font = .systemFont(ofSize: 14, weight: .semibold)
            nameLabel.frame = NSRect(x: 24, y: 241, width: 160, height: 20); content.addSubview(nameLabel)
            let nameField = NSTextField(string: model.identity.name)
            nameField.frame = NSRect(x: 24, y: 203, width: 220, height: 26)
            nameField.placeholderString = "펫 이름을 입력하세요"
            nameField.setAccessibilityLabel("펫 이름")
            nameField.target = self; nameField.action = #selector(savePetName)
            content.addSubview(nameField)
            let saveName = NSButton(title: "이름 저장", target: self, action: #selector(savePetName))
            saveName.bezelStyle = .rounded; saveName.frame = NSRect(x: 249, y: 200, width: 92, height: 32)
            content.addSubview(saveName)
            let nameHint = NSTextField(labelWithString: "1~20자 · 저장하면 모든 화면에 적용됩니다.")
            nameHint.font = .systemFont(ofSize: 11); nameHint.textColor = .secondaryLabelColor
            nameHint.frame = NSRect(x: 24, y: 175, width: 312, height: 17); content.addSubview(nameHint)
            petNameField = nameField; petNameHint = nameHint
            let title = NSTextField(labelWithString: "펫 크기")
            title.font = .systemFont(ofSize: 14, weight: .semibold)
            title.frame = NSRect(x: 24, y: 125, width: 160, height: 20); content.addSubview(title)
            let value = NSTextField(labelWithString: "100%")
            value.alignment = .right; value.font = .monospacedDigitSystemFont(ofSize: 13, weight: .medium)
            value.frame = NSRect(x: 254, y: 125, width: 82, height: 20); content.addSubview(value)
            let slider = NSSlider(value: 100, minValue: 25, maxValue: 200, target: self, action: #selector(resizePet(_:)))
            slider.frame = NSRect(x: 22, y: 86, width: 316, height: 26)
            slider.isContinuous = true; slider.setAccessibilityLabel("펫 크기")
            slider.toolTip = "캐릭터만 25%부터 200%까지 조절 · 말풍선 크기는 고정"
            content.addSubview(slider)
            for (text, x) in [("25%", 24.0), ("200%", 294.0)] {
                let label = NSTextField(labelWithString: text); label.font = .systemFont(ofSize: 11)
                label.textColor = .secondaryLabelColor; label.frame = NSRect(x: x, y: 65, width: 44, height: 16)
                content.addSubview(label)
            }
            let hint = NSTextField(labelWithString: "말풍선 크기는 그대로 유지됩니다.")
            hint.font = .systemFont(ofSize: 11); hint.textColor = .secondaryLabelColor
            hint.frame = NSRect(x: 24, y: 26, width: 216, height: 17); content.addSubview(hint)
            let reset = NSButton(title: "기본 크기", target: self, action: #selector(resetPetSize))
            reset.bezelStyle = .rounded; reset.frame = NSRect(x: 247, y: 19, width: 94, height: 32)
            content.addSubview(reset)
            window.contentView = content; window.center()
            optionsWindow = window; sizeSlider = slider; sizeValueLabel = value
        }
        let percent = (pet.petScale * 100).rounded()
        sizeSlider?.doubleValue = percent; sizeValueLabel?.stringValue = "\(Int(percent))%"
        petNameField?.stringValue = model.identity.name
        petNameHint?.stringValue = "1~20자 · 저장하면 모든 화면에 적용됩니다."
        petNameHint?.textColor = .secondaryLabelColor
        NSApp.activate(ignoringOtherApps: true); optionsWindow?.makeKeyAndOrderFront(nil)
    }
    @objc func savePetName() {
        guard let field = petNameField else { return }
        if let message = PetIdentity.validationMessage(for: field.stringValue) {
            petNameHint?.stringValue = message; petNameHint?.textColor = .systemRed; return
        }
        let identity = PetIdentity(name: PetIdentity.normalized(field.stringValue))
        identity.save(); model.identity = identity; pet.identity = identity
        field.stringValue = identity.name
        dashboard?.title = identity.dashboardTitle
        if isPreview { updatePreviewSubtitle() }
        updatePet()
        petNameHint?.stringValue = "이름을 저장했어요. 모든 화면에 적용했습니다."
        petNameHint?.textColor = .secondaryLabelColor
    }
    @objc func resizePet(_ sender: NSSlider) { applyPetSize(sender.doubleValue) }
    @objc func resetPetSize() { applyPetSize(100) }
    func applyPetSize(_ value: Double) {
        let percent = min(200, max(25, value.isFinite ? value.rounded() : 100))
        let scale = CGFloat(percent) / 100
        let previous = panel.convertToScreen(pet.convert(pet.spriteRect, to: nil))
        pet.petScale = scale
        placePet(at: NSPoint(x: previous.midX - PetLayout(scale: scale).spriteSize.width / 2, y: previous.minY))
        sizeSlider?.doubleValue = percent; sizeValueLabel?.stringValue = "\(Int(percent))%"
        UserDefaults.standard.set(percent, forKey: "petScale")
        constrainPosition(); savePosition()
    }
    @objc func resetPosition() {
        guard let screen = NSScreen.main else { return }
        placePet(at: NSPoint(x: screen.visibleFrame.maxX - PetLayout(scale: pet.petScale).spriteSize.width - 30,
                            y: screen.visibleFrame.minY + 25))
        savePosition()
    }
    func restorePosition() {
        let defaults = UserDefaults.standard
        let saved = defaults.object(forKey: "petScale") == nil ? 100 : defaults.double(forKey: "petScale")
        let scale = min(200, max(25, saved.isFinite ? saved : 100))
        pet.petScale = CGFloat(scale / 100)
        panel.setContentSize(PetLayout(scale: pet.petScale).windowSize)
        if defaults.object(forKey: "petAnchorX") != nil {
            placePet(at: NSPoint(x: defaults.double(forKey: "petAnchorX"), y: defaults.double(forKey: "petAnchorY")))
        } else if defaults.object(forKey: "petX") != nil {
            panel.setFrameOrigin(NSPoint(x: defaults.double(forKey: "petX"), y: defaults.double(forKey: "petY"))); constrainPosition()
        } else { resetPosition() }
    }
    func constrainPosition() {
        placePet(at: panel.convertToScreen(pet.convert(pet.spriteRect, to: nil)).origin)
    }
    func placePet(at origin: NSPoint) {
        let size = PetLayout(scale: pet.petScale).spriteSize
        let center = NSPoint(x: origin.x + size.width / 2, y: origin.y + size.height / 2)
        let screen = NSScreen.screens.min { a, b in
            func distance(_ frame: NSRect) -> CGFloat {
                let dx = max(0, max(frame.minX - center.x, center.x - frame.maxX))
                let dy = max(0, max(frame.minY - center.y, center.y - frame.maxY))
                return dx * dx + dy * dy
            }
            return distance(a.frame) < distance(b.frame)
        }
        guard let screen else { return }
        let placement = PetPlacement(spriteOrigin: origin, scale: pet.petScale, visibleFrame: screen.visibleFrame)
        placementTarget = placement
        bubbleMotion.retarget(placement.globalBubble.origin, visibleFrame: screen.visibleFrame,
                              now: ProcessInfo.processInfo.systemUptime,
                              immediately: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
        advancePlacement()
    }
    func advancePlacement() {
        guard let target = placementTarget else { return }
        let origin = bubbleMotion.step(now: ProcessInfo.processInfo.systemUptime)
        let placement = PetPlacement(globalSprite: target.globalSprite,
                                     globalBubble: NSRect(origin: origin, size: PetLayout.bubbleSize))
        guard pet.placement?.windowFrame != placement.windowFrame || pet.placement?.bubbleRect != placement.bubbleRect || pet.placement?.spriteRect != placement.spriteRect else { return }
        pet.placement = placement
        panel.setFrame(placement.windowFrame, display: false)
        pet.layoutSubtreeIfNeeded()
    }
    func savePosition() {
        UserDefaults.standard.set(panel.frame.minX, forKey: "petX"); UserDefaults.standard.set(panel.frame.minY, forKey: "petY")
        let origin = panel.convertToScreen(pet.convert(pet.spriteRect, to: nil)).origin
        UserDefaults.standard.set(origin.x, forKey: "petAnchorX"); UserDefaults.standard.set(origin.y, forKey: "petAnchorY")
    }
    @objc func screensChanged() { constrainPosition() }
    @objc func preview() {
        previewTimer?.invalidate(); isPreview = true; pet.previewLook = false
        let names = ["쉬는 중", "오른쪽 이동", "왼쪽 이동", "손 흔들기", "점프", "오류", "확인 대기", "작업 중", "검토", "방향 보기 1", "방향 보기 2"]
        var row = 0
        pet.previewRow = row; pet.headline = "미리보기 · \(names[row])"; updatePreviewSubtitle()
        previewTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }; row += 1
            if row >= names.count {
                timer.invalidate(); self.isPreview = false; self.pet.previewRow = nil; self.updatePet(); return
            }
            self.pet.previewRow = row; self.pet.headline = "미리보기 · \(names[row])"
        }
    }
    @objc func previewPointer() {
        previewTimer?.invalidate(); isPreview = true; pet.previewRow = nil; pet.previewLook = true
        pet.state = .idle; pet.headline = "미리보기 · 마우스 바라보기"
        updatePreviewSubtitle()
        previewTimer = Timer.scheduledTimer(withTimeInterval: 15, repeats: false) { [weak self] _ in
            self?.pet.previewLook = false; self?.isPreview = false; self?.updatePet()
        }
    }
    private func updatePreviewSubtitle() {
        pet.subtitle = pet.previewLook ? "\(model.identity.name) · 포인터를 움직여 보세요" : "\(model.identity.name)의 동작 · 실제 작업과 별개"
    }
    @objc func showHooksHelp() {
        let alert = NSAlert()
        alert.messageText = "승인 대기 알림 연결"
        alert.informativeText = "기본 연결은 Codex와 Claude Code의 로컬 작업 기록을 읽습니다. 승인 대기를 정확히 알리려면 공식 훅을 추가하세요. 앱에 포함된 연결 안내에서 적용할 설정을 먼저 확인할 수 있습니다."
        alert.addButton(withTitle: "연결 안내 열기"); alert.addButton(withTitle: "닫기")
        if alert.runModal() == .alertFirstButtonReturn, let url = Bundle.main.url(forResource: "연결안내", withExtension: "html") { NSWorkspace.shared.open(url) }
    }
    @objc func quit() { NSApp.terminate(nil) }

    func capture(destination: URL) {
        try? FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        for (name, view) in [("pet", pet as NSView), ("dashboard", dashboard!.contentView!)] {
            view.layoutSubtreeIfNeeded()
            guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { continue }
            view.cacheDisplay(in: view.bounds, to: bitmap)
            try? bitmap.representation(using: .png, properties: [:])?.write(to: destination.appendingPathComponent("\(name).png"))
        }
        if let snapshot = currentSnapshot {
            let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]; encoder.dateEncodingStrategy = .iso8601
            try? encoder.encode(snapshot).write(to: destination.appendingPathComponent("snapshot.json"))
        }
    }
}
