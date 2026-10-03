import AppKit
import QuartzCore

final class SpriteAtlas {
    let counts = [7, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8]
    private var frames: [[NSImage]] = []
    private var presentations: [[PetSpritePresentation]] = []
    private var interactionBounds = CGRect.zero
    init() throws {
        guard let url = Bundle.main.url(forResource: "spritesheet", withExtension: "png", subdirectory: "Pet"),
              let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let atlas = CGImageSourceCreateImageAtIndex(source, 0, nil),
              atlas.width == 1536, atlas.height == 2288 else {
            throw NSError(domain: "Taesik", code: 1, userInfo: [NSLocalizedDescriptionKey: "펫 이미지 파일을 읽을 수 없습니다."])
        }
        var metrics: [[SpriteContentMetrics]] = []
        for (row, count) in counts.enumerated() {
            var images: [NSImage] = [], content: [SpriteContentMetrics] = []
            for column in 0..<count {
                guard let cell = atlas.cropping(to: CGRect(x: column * 192, y: row * 208, width: 192, height: 208)),
                      let measured = SpriteContentMetrics.measure(cell) else {
                    throw NSError(domain: "Taesik", code: 2, userInfo: [NSLocalizedDescriptionKey: "펫 동작 이미지를 읽을 수 없습니다."])
                }
                images.append(NSImage(cgImage: cell, size: NSSize(width: 192, height: 208)))
                content.append(measured)
            }
            frames.append(images); metrics.append(content)
        }
        // The smaller, front-facing look pose from the user's comparison remains unchanged.
        let reference = metrics[10][7]
        interactionBounds = reference.bounds
        for (row, content) in metrics.enumerated() {
            presentations.append(content.map { metric in
                PetSpritePresentation.matching(row >= 9 ? metric : content[0], to: reference)
            })
        }
    }
    func image(row: Int, frame: Int) -> NSImage { frames[row][frame % counts[row]] }
    func drawingRect(row: Int, frame: Int, in cell: CGRect) -> CGRect {
        presentations[row][frame % counts[row]].drawingRect(in: cell)
    }
    func interactionRect(in cell: CGRect) -> CGRect {
        PetSpritePresentation.interactionRect(for: interactionBounds, in: cell)
    }
}

final class PetPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@available(macOS 26.0, *)
final class PetGlassView: NSGlassEffectView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

final class PetVibrancyView: NSVisualEffectView {
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}

final class BubbleTextView: NSView {
    var headline = "AIpet" { didSet { needsDisplay = true } }
    var subtitle = "AI 작업을 살펴보고 있어요" { didSet { needsDisplay = true } }
    var state: WorkState = .idle { didSet { updateStatusIndicator(); needsDisplay = true } }
    private let statusDot = CALayer()
    private var pulseAllowed = true
    private let pulseKey = "workingStatusPulse"
    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        statusDot.frame = CGRect(x: 16, y: 35, width: 6, height: 6)
        statusDot.cornerRadius = 3
        layer?.addSublayer(statusDot)
        updateStatusIndicator()
    }
    required init?(coder: NSCoder) { fatalError() }

    func setPulseAllowed(_ allowed: Bool) {
        guard pulseAllowed != allowed else { return }
        pulseAllowed = allowed; updateStatusIndicator()
    }
    private func updateStatusIndicator() {
        let color: NSColor = state == .waiting ? .systemOrange : state == .failed ? .systemRed : state == .running ? .systemGreen : .systemTeal
        CATransaction.begin(); CATransaction.setDisableActions(true)
        statusDot.backgroundColor = color.cgColor
        statusDot.opacity = 1
        CATransaction.commit()
        if state == .running && pulseAllowed {
            guard statusDot.animation(forKey: pulseKey) == nil else { return }
            let pulse = CABasicAnimation(keyPath: "opacity")
            pulse.fromValue = 1; pulse.toValue = 0.35
            pulse.duration = 0.8; pulse.autoreverses = true; pulse.repeatCount = .infinity
            pulse.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            statusDot.add(pulse, forKey: pulseKey)
        } else { statusDot.removeAnimation(forKey: pulseKey) }
    }
    override var isOpaque: Bool { false }
    override func draw(_ dirtyRect: NSRect) {
        drawText(headline, rect: NSRect(x: 30, y: 27, width: 284, height: 19), size: 13, color: .labelColor, bold: true)
        drawText(subtitle, rect: NSRect(x: 16, y: 10, width: 298, height: 16), size: 11, color: .secondaryLabelColor, bold: false)
    }
    private func drawText(_ text: String, rect: NSRect, size: CGFloat, color: NSColor, bold: Bool) {
        let paragraph = NSMutableParagraphStyle(); paragraph.lineBreakMode = .byTruncatingTail
        (text as NSString).draw(in: rect, withAttributes: [.font: NSFont.systemFont(ofSize: size, weight: bold ? .semibold : .regular), .foregroundColor: color, .paragraphStyle: paragraph])
    }
}

final class PetView: NSView {
    let atlas: SpriteAtlas
    var identity: PetIdentity { didSet { updateIdentityDescription() } }
    let bubbleText = BubbleTextView()
    private var bubbleSurface: NSView!
    var placement: PetPlacement? { didSet { needsLayout = true; needsDisplay = true } }
    var spriteRect: CGRect { placement?.spriteRect ?? PetLayout(scale: petScale).spriteRect }
    var petScale: CGFloat = 1 { didSet { needsLayout = true; needsDisplay = true } }
    var state: WorkState = .idle { didSet { bubbleText.state = state } }
    var headline = "AIpet" { didSet { bubbleText.headline = headline; updateIdentityDescription() } }
    var subtitle = "AI 작업을 살펴보고 있어요" { didSet { bubbleText.subtitle = subtitle } }
    var onClick: (() -> Void)?
    var onMenu: ((NSEvent) -> Void)?
    var onMoved: (() -> Void)?
    var onDragMove: ((NSPoint) -> Void)?
    var onAnimationTick: (() -> Void)?
    var isPaused = false { didSet {
        animationStarted = ProcessInfo.processInfo.systemUptime; pointerInteraction.reset()
        bubbleText.setPulseAllowed(!isPaused && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
    } }
    var screenFocus: ScreenFocus?
    var previewRow: Int? { didSet { animationStarted = ProcessInfo.processInfo.systemUptime } }
    var previewLook = false
    private var displayedFrame = PetAnimationFrame(row: 0, column: 0)
    private var currentAnimation: PetAnimation = .idle
    private var animationStarted = ProcessInfo.processInfo.systemUptime
    private var dragAnimation: PetAnimation = .right
    private var pointerInteraction = PetPointerInteraction()
    private let windowResolver = ScreenWindowResolver()
    private var spriteTracking: NSTrackingArea?
    private var wasLooking = false
    private var dragStart: NSPoint?
    private var windowStart: NSPoint?
    private var didDrag = false
    private var previousDragMouse: NSPoint?
    private var timer: Timer?

    init(atlas: SpriteAtlas, identity: PetIdentity) {
        self.atlas = atlas
        self.identity = identity
        super.init(frame: NSRect(origin: .zero, size: PetLayout(scale: 1).windowSize))
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        if #available(macOS 26.0, *) {
            let glass = PetGlassView()
            glass.style = .clear
            glass.cornerRadius = 20
            glass.contentView = bubbleText
            bubbleSurface = glass
        } else {
            let glass = PetVibrancyView()
            glass.material = .popover
            glass.blendingMode = .behindWindow
            glass.state = .active
            glass.wantsLayer = true
            glass.layer?.cornerRadius = 20
            glass.layer?.masksToBounds = true
            glass.addSubview(bubbleText)
            bubbleText.autoresizingMask = [.width, .height]
            bubbleSurface = glass
        }
        addSubview(bubbleSurface)
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
        headline = identity.greeting
        bubbleText.headline = headline
        updateIdentityDescription()
        let animationTimer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] _ in
            self?.advanceAnimation()
        }
        timer = animationTimer
        RunLoop.main.add(animationTimer, forMode: .common)
    }
    required init?(coder: NSCoder) { fatalError() }
    private func updateIdentityDescription() {
        setAccessibilityLabel("AIpet · \(identity.name) · \(headline) · 작업 목록 열기")
        toolTip = "\(identity.name) · 클릭: 작업 목록 · 드래그: 이동 · 오른쪽 클릭: 메뉴"
    }
    override var isOpaque: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func layout() {
        super.layout()
        bubbleSurface.frame = placement?.bubbleRect ?? PetLayout(scale: petScale).bubbleRect
        bubbleText.frame = NSRect(origin: .zero, size: PetLayout.bubbleSize)
        updateTrackingAreas()
    }
    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let spriteTracking { removeTrackingArea(spriteTracking) }
        let area = NSTrackingArea(rect: atlas.interactionRect(in: spriteRect),
                                  options: [.mouseEnteredAndExited, .activeAlways], owner: self, userInfo: nil)
        addTrackingArea(area); spriteTracking = area
    }
    override func mouseEntered(with event: NSEvent) { advanceAnimation() }
    override func mouseExited(with event: NSEvent) { advanceAnimation() }

    private func advanceAnimation() {
        guard let window, window.isVisible else { pointerInteraction.reset(); bubbleText.setPulseAllowed(false); return }
        onAnimationTick?()
        bubbleText.setPulseAllowed(!isPaused && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
        guard !isPaused else { return }
        let now = ProcessInfo.processInfo.systemUptime
        let mouse = NSEvent.mouseLocation
        let rect = window.convertToScreen(convert(atlas.interactionRect(in: spriteRect), to: nil))
        let display = NSScreen.screens.first { $0.frame.contains(CGPoint(x: rect.midX, y: rect.midY)) } ?? window.screen
        let reduced = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let pointer = pointerInteraction.update(pointer: mouse, sprite: rect, display: display?.frame ?? .zero,
                                                now: now, enabled: !didDrag && previewRow == nil && !previewLook,
                                                reducedMotion: reduced)
        let animation = previewRow.flatMap(PetAnimation.init(rawValue:)) ?? (didDrag ? dragAnimation : PetAnimation(state: state))
        if currentAnimation != animation { currentAnimation = animation; animationStarted = now }
        var frame = PetAnimationTimeline.frame(for: animation, elapsed: now - animationStarted,
                                               reducedMotion: reduced,
                                               loop: didDrag || previewRow != nil)
        var behavior = animation.name
        var looking = false
        if let row = previewRow, row >= 9 {
            frame = PetAnimationFrame(row: row, column: Int((now - animationStarted) / 0.25) % 8)
            behavior = "시선 미리보기"
        } else if previewRow == nil, !didDrag {
            var target: CGPoint?
            switch pointer {
            case .jumping(let elapsed):
                frame = PetAnimationTimeline.frame(for: .jumping, elapsed: elapsed)
                behavior = "인사 점프 · 1회"
            case .looking:
                target = mouse; behavior = "커서 따라보기 · dot"
            case .inactive:
                if previewLook { target = mouse; behavior = "마우스 시선 미리보기" }
                else if animation.allowsLook, let focus = screenFocus, focus.expiresAt > Date() {
                    target = windowResolver.resolve(focus, now: now)
                    behavior = target == nil ? "\(focus.provider.name) · 대상 창 확인 중" : "\(focus.provider.name) · 먼저 시작한 작업 창 보기"
                }
            }
            if let target, let look = PetAnimationTimeline.lookFrame(from: CGPoint(x: rect.midX, y: rect.midY), toward: target,
                                                                   previous: wasLooking ? displayedFrame : nil) {
                frame = look; looking = true
            }
        }
        if wasLooking && !looking { animationStarted = now }
        wasLooking = looking
        if displayedFrame != frame { displayedFrame = frame; needsDisplay = true }
        let side = placement?.bubbleBelow == true ? "아래" : "위"
        setAccessibilityHelp("클릭: 작업 목록 · 드래그: 이동 · 오른쪽 클릭: 메뉴 · 동작: \(behavior) · 행 \(frame.row), 프레임 \(frame.column) · 말풍선: \(side)")
    }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.setFill(); bounds.fill(using: .copy)
        let rect = atlas.drawingRect(row: displayedFrame.row, frame: displayedFrame.column, in: spriteRect)
        atlas.image(row: displayedFrame.row, frame: displayedFrame.column).draw(in: rect, from: .zero, operation: .sourceOver, fraction: 1)
    }
    override func mouseDown(with event: NSEvent) {
        dragStart = window?.convertPoint(toScreen: event.locationInWindow); previousDragMouse = dragStart
        windowStart = window?.convertToScreen(convert(spriteRect, to: nil)).origin; didDrag = false
    }
    override func mouseDragged(with event: NSEvent) {
        guard let start = dragStart, let origin = windowStart else { return }
        guard let mouse = window?.convertPoint(toScreen: event.locationInWindow) else { return }
        if hypot(mouse.x - start.x, mouse.y - start.y) > 4 { didDrag = true }
        dragAnimation = PetAnimationTimeline.dragAnimation(horizontalDelta: mouse.x - (previousDragMouse?.x ?? start.x), previous: dragAnimation)
        previousDragMouse = mouse
        onDragMove?(NSPoint(x: origin.x + mouse.x - start.x, y: origin.y + mouse.y - start.y))
        advanceAnimation()
    }
    override func mouseUp(with event: NSEvent) {
        if didDrag { onMoved?() } else { onClick?() }
        didDrag = false; dragStart = nil; windowStart = nil; previousDragMouse = nil
        updateTrackingAreas(); advanceAnimation()
    }
    override func rightMouseDown(with event: NSEvent) { onMenu?(event) }
    override func accessibilityPerformPress() -> Bool { onClick?(); return true }
}
