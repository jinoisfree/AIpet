import AppKit
import QuartzCore

final class PetSpriteView: NSView {
    var onDraw: ((CGRect) -> Void)?
    override var isOpaque: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func viewDidChangeBackingProperties() { super.viewDidChangeBackingProperties(); needsDisplay = true }
    override func draw(_ dirtyRect: NSRect) { onDraw?(bounds) }
}

final class PetPanel: NSPanel {
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect { frameRect }
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
    private var trail: [NSView] = []
    private let spriteSurface = PetSpriteView()
    private var reveal = BubbleReveal()
    private(set) var bubbleShown = false
    private(set) var bubbleStowed = false
    var bubbleAlwaysShown = false { didSet { updateBubbleShown() } }
    var pointerLocation: () -> NSPoint = { NSEvent.mouseLocation }
    private var hovering = false
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
        animationStarted = ProcessInfo.processInfo.systemUptime; pointerInteraction.reset(); doze = PetDoze()
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
    private var doze = PetDoze()
    /// The pet has nodded off; the bubble says so.
    private(set) var isDozing = false
    var onDozeChanged: (() -> Void)?
    private let dozeDelay: TimeInterval = CommandLine.arguments.contains("--ui-smoke-doze") ? 1 : PetDoze.delay
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
        bubbleSurface = Self.surface(cornerRadius: 20, content: bubbleText)
        addSubview(bubbleSurface)
        // The trail of a thought bubble, in the same material as the bubble.
        trail = ThoughtTrail.diameters.map { Self.surface(cornerRadius: $0 / 2, content: nil) }
        trail.forEach(addSubview)
        ([bubbleSurface!] + trail).forEach { $0.alphaValue = 0 }
        stowBubble(true)
        spriteSurface.wantsLayer = true
        spriteSurface.layer?.zPosition = 1
        spriteSurface.onDraw = { [weak self] cell in self?.drawSprite(in: cell) }
        addSubview(spriteSurface)
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
    private static func surface(cornerRadius: CGFloat, content: NSView?) -> NSView {
        if #available(macOS 26.0, *) {
            let glass = PetGlassView()
            glass.style = .clear
            glass.cornerRadius = cornerRadius
            glass.contentView = content
            return glass
        }
        let glass = PetVibrancyView()
        glass.material = .popover
        glass.blendingMode = .behindWindow
        glass.state = .active
        glass.wantsLayer = true
        glass.layer?.cornerRadius = cornerRadius
        glass.layer?.masksToBounds = true
        if let content { glass.addSubview(content); content.autoresizingMask = [.width, .height] }
        return glass
    }
    private func updateIdentityDescription() {
        setAccessibilityLabel("AIpet · \(identity.name) · \(headline) · 작업 목록 열기")
        toolTip = "\(identity.name) · 클릭: 작업 목록 · 드래그: 이동 · 오른쪽 클릭: 메뉴"
    }
    override var isOpaque: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func layout() {
        super.layout()
        if spriteSurface.frame.size != spriteRect.size { spriteSurface.needsDisplay = true }
        spriteSurface.frame = spriteRect
        bubbleSurface.frame = placement?.bubbleRect ?? PetLayout(scale: petScale).bubbleRect
        let below = placement?.bubbleBelow == true
        let circles = ThoughtTrail.circles(bubble: bubbleSurface.frame, pet: PetLayout.trailEnd(sprite: spriteRect, below: below), below: below)
        for (circle, frame) in zip(trail, circles) { circle.frame = frame }
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
        updateBubbleShown()
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
        if previewRow == PetAnimationTimeline.dozeRow {
            frame = PetAnimationTimeline.dozeFrame(elapsed: now - animationStarted, reducedMotion: reduced)
            behavior = "조는 중 미리보기"
        } else if let row = previewRow, row >= 9 {
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
        // Left alone with nothing to do, look at or react to, the pet nods off.
        let quiet = animation == .idle && previewRow == nil && !previewLook && !looking && pointer == .inactive
        let dozing = doze.update(now: now, quiet: quiet, after: dozeDelay)
        if let dozing {
            frame = PetAnimationTimeline.dozeFrame(elapsed: dozing, reducedMotion: reduced)
            behavior = "조는 중"
        } else if isDozing { animationStarted = now }
        if isDozing != (dozing != nil) { isDozing = dozing != nil; onDozeChanged?() }
        if displayedFrame != frame { displayedFrame = frame; spriteSurface.needsDisplay = true }
        let side = placement?.bubbleBelow == true ? "아래" : "위"
        setAccessibilityHelp("클릭: 작업 목록 · 드래그: 이동 · 오른쪽 클릭: 메뉴 · 동작: \(behavior) · 행 \(frame.row), 프레임 \(frame.column) · 말풍선: \(side) · \(bubbleShown ? "표시" : "숨김")")
    }
    override func draw(_ dirtyRect: NSRect) {
        NSColor.clear.setFill(); bounds.fill(using: .copy)
    }
    private func drawSprite(in cell: CGRect) {
        atlas.draw(row: displayedFrame.row, frame: displayedFrame.column, in: cell, view: spriteSurface)
        if displayedFrame.row == PetAnimationTimeline.dozeRow { drawZs(PetAnimationTimeline.dozeZs(column: displayedFrame.column), in: cell) }
    }
    /// Each z is written white with a dark edge, like the pet's own outline, in the square it is given.
    private func drawZs(_ squares: [CGRect], in cell: CGRect) {
        let unit = cell.width / PetSpritePresentation.cellSize.width
        for square in squares {
            let left = cell.minX + square.minX * unit, right = cell.minX + square.maxX * unit
            let top = cell.maxY - square.minY * unit, bottom = cell.maxY - square.maxY * unit
            let z = NSBezierPath()
            z.move(to: NSPoint(x: left, y: top)); z.line(to: NSPoint(x: right, y: top))
            z.line(to: NSPoint(x: left, y: bottom)); z.line(to: NSPoint(x: right, y: bottom))
            z.lineCapStyle = .round; z.lineJoinStyle = .round
            for (width, color) in [(4.0, NSColor(red: 16/255, green: 12/255, blue: 12/255, alpha: 1)), (1.9, NSColor.white)] {
                color.setStroke(); z.lineWidth = width * unit; z.stroke()
            }
        }
    }
    func revealBubble() { reveal.alert(now: ProcessInfo.processInfo.systemUptime); updateBubbleShown() }
    private func updateBubbleShown() {
        if let window, window.isVisible {
            let head = atlas.interactionRect(in: spriteRect), pointer = pointerLocation()
            func over(_ rect: CGRect) -> Bool { window.convertToScreen(convert(rect, to: nil)).contains(pointer) }
            hovering = over(head) || (hovering && bubbleShown && over(head.union(bubbleSurface.frame)))
        } else { hovering = false }
        let held = previewRow != nil || previewLook || didDrag || hovering
        let shown = reveal.update(now: ProcessInfo.processInfo.systemUptime, held: held) || bubbleAlwaysShown
        guard shown != bubbleShown else { return }
        bubbleShown = shown
        let surfaces = [bubbleSurface!] + trail
        if shown { stowBubble(false) }
        if NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            surfaces.forEach { $0.alphaValue = shown ? 1 : 0 }
            stowBubble(!shown)
        } else {
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = 0.18
                surfaces.forEach { $0.animator().alphaValue = shown ? 1 : 0 }
            }, completionHandler: { [weak self] in
                if let self, !self.bubbleShown { self.stowBubble(true) }
            })
        }
    }
    private func stowBubble(_ stowed: Bool) {
        guard stowed != bubbleStowed else { return }
        bubbleStowed = stowed
        ([bubbleSurface!] + trail).forEach { $0.isHidden = stowed }
    }
    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        if atlas.opaque(at: local, in: spriteRect, row: displayedFrame.row, frame: displayedFrame.column) { return self }
        return bubbleShown && bubbleSurface.frame.contains(local) ? self : nil
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
