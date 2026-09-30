import Foundation
import CoreGraphics

/// The message surface is measured in screen points, independently of the sprite scale.
struct PetLayout {
    static let bubbleSize = CGSize(width: 330, height: 56)
    let scale: CGFloat
    var spriteSize: CGSize { CGSize(width: 192 * scale, height: 208 * scale) }
    var windowSize: CGSize {
        CGSize(width: max(Self.bubbleSize.width, spriteSize.width) + 24,
               height: spriteSize.height + Self.bubbleSize.height + 32)
    }
    var spriteRect: CGRect {
        CGRect(x: (windowSize.width - spriteSize.width) / 2, y: 12,
               width: spriteSize.width, height: spriteSize.height)
    }
    var bubbleRect: CGRect {
        CGRect(x: (windowSize.width - Self.bubbleSize.width) / 2, y: spriteRect.maxY + 8,
               width: Self.bubbleSize.width, height: Self.bubbleSize.height)
    }
}

/// The sprite is the position anchor. The bubble shifts independently to stay on the display.
struct PetPlacement {
    let windowFrame: CGRect
    let spriteRect: CGRect
    let bubbleRect: CGRect
    let bubbleBelow: Bool
    var globalSprite: CGRect { spriteRect.offsetBy(dx: windowFrame.minX, dy: windowFrame.minY) }
    var globalBubble: CGRect { bubbleRect.offsetBy(dx: windowFrame.minX, dy: windowFrame.minY) }

    init(globalSprite: CGRect, globalBubble: CGRect) {
        windowFrame = globalSprite.union(globalBubble).insetBy(dx: -12, dy: -12)
        spriteRect = globalSprite.offsetBy(dx: -windowFrame.minX, dy: -windowFrame.minY)
        bubbleRect = globalBubble.offsetBy(dx: -windowFrame.minX, dy: -windowFrame.minY)
        bubbleBelow = globalBubble.midY < globalSprite.midY
    }

    init(spriteOrigin: CGPoint, scale: CGFloat, visibleFrame: CGRect) {
        let safe = visibleFrame.insetBy(dx: 12, dy: 12)
        let size = PetLayout(scale: scale).spriteSize
        var sprite = CGRect(x: min(max(spriteOrigin.x, safe.minX), max(safe.minX, safe.maxX - size.width)),
                            y: min(max(spriteOrigin.y, safe.minY), max(safe.minY, safe.maxY - size.height)),
                            width: size.width, height: size.height)
        let bubbleSize = PetLayout.bubbleSize
        bubbleBelow = sprite.maxY + 8 + bubbleSize.height > safe.maxY
        if bubbleBelow { sprite.origin.y = max(sprite.minY, safe.minY + 8 + bubbleSize.height) }
        let bubble = CGRect(x: min(max(sprite.midX - bubbleSize.width / 2, safe.minX), max(safe.minX, safe.maxX - bubbleSize.width)),
                            y: bubbleBelow ? sprite.minY - 8 - bubbleSize.height : sprite.maxY + 8,
                            width: bubbleSize.width, height: bubbleSize.height)
        windowFrame = sprite.union(bubble).insetBy(dx: -12, dy: -12)
        spriteRect = sprite.offsetBy(dx: -windowFrame.minX, dy: -windowFrame.minY)
        bubbleRect = bubble.offsetBy(dx: -windowFrame.minX, dy: -windowFrame.minY)
    }
}

/// Frame-rate independent easing. Keep the bubble inside the current screen even during a transition.
struct PetBubbleMotion {
    private(set) var position: CGPoint?
    private var target = CGPoint.zero
    private var lastTime: TimeInterval?
    private var bounds = CGRect.zero

    mutating func retarget(_ point: CGPoint, visibleFrame: CGRect, now: TimeInterval, immediately: Bool = false) {
        bounds = visibleFrame.insetBy(dx: 12, dy: 12)
        target = clamped(point)
        if position == nil || immediately { position = target; lastTime = now }
        else if let position { self.position = clamped(position) }
    }

    mutating func step(now: TimeInterval) -> CGPoint {
        let dt = min(0.1, max(0, now - (lastTime ?? now)))
        lastTime = now
        let previous = position ?? target
        let fraction = 1 - exp(-dt / 0.065)
        let next = CGPoint(x: previous.x + (target.x - previous.x) * fraction,
                           y: previous.y + (target.y - previous.y) * fraction)
        position = hypot(next.x - target.x, next.y - target.y) < 0.2 ? target : clamped(next)
        return position!
    }

    private func clamped(_ point: CGPoint) -> CGPoint {
        CGPoint(x: min(max(point.x, bounds.minX), max(bounds.minX, bounds.maxX - PetLayout.bubbleSize.width)),
                y: min(max(point.y, bounds.minY), max(bounds.minY, bounds.maxY - PetLayout.bubbleSize.height)))
    }
}
