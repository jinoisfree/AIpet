import Foundation
import CoreGraphics

/// Three shrinking circles lead from the bubble to the pet, the way a thought is drawn.
enum ThoughtTrail {
    static let diameters: [CGFloat] = [15, 10.5, 6.5]
    static let spacing: CGFloat = 3.5
    static let length = diameters.reduce(spacing) { $0 + $1 + spacing }
    /// The largest circle sits against the bubble and the smallest ends toward `pet`, the nearest point of the visible pet.
    static func circles(bubble: CGRect, pet: CGPoint, below: Bool) -> [CGRect] {
        let edge = below ? bubble.maxY : bubble.minY, direction: CGFloat = below ? 1 : -1
        let start = min(max(pet.x, bubble.minX + 24), bubble.maxX - 24)
        let reach = max(length, abs(pet.y - edge))
        var travelled = spacing
        return diameters.map { diameter in
            let along = travelled + diameter / 2
            travelled += diameter + spacing
            var x = start + (pet.x - start) * along / reach - diameter / 2
            if diameter == diameters[0] { x = min(max(x, bubble.minX + 14), bubble.maxX - 14 - diameter) }
            return CGRect(x: x, y: edge + direction * along - diameter / 2, width: diameter, height: diameter)
        }
    }
}

/// The message surface is measured in screen points, independently of the sprite scale.
struct PetLayout {
    static let bubbleSize = CGSize(width: 330, height: 56)
    /// The pet as drawn inside its 192×208 cell, from the cell's top-left corner.
    static let visiblePet = CGRect(x: 54, y: 60, width: 118, height: 134)
    static let petMargin: CGFloat = 4
    static func visibleSprite(in cell: CGRect) -> CGRect {
        let unit = cell.width / 192
        return CGRect(x: cell.minX + visiblePet.minX * unit, y: cell.maxY - visiblePet.maxY * unit,
                      width: visiblePet.width * unit, height: visiblePet.height * unit)
    }
    static func constrainedSprite(_ sprite: CGRect, screen: CGRect) -> CGRect {
        let safe = screen.insetBy(dx: petMargin, dy: petMargin), visible = visibleSprite(in: sprite)
        let x = min(max(visible.minX, safe.minX), max(safe.minX, safe.maxX - visible.width))
        let y = min(max(visible.minY, safe.minY), max(safe.minY, safe.maxY - visible.height))
        return sprite.offsetBy(dx: x - visible.minX, dy: y - visible.minY)
    }
    let scale: CGFloat
    var spriteSize: CGSize { CGSize(width: 192 * scale, height: 208 * scale) }
    /// The bubble keeps room for the thought trail; the cell's empty margin around the pet counts toward it.
    static func gap(scale: CGFloat, below: Bool = false) -> CGFloat {
        max(8, ThoughtTrail.length - (below ? 208 - visiblePet.maxY : visiblePet.minY) * scale)
    }
    /// Where the trail ends: the top of the pet's head, or its feet when the bubble is below. AppKit coordinates.
    static func trailEnd(sprite: CGRect, below: Bool) -> CGPoint {
        let unit = sprite.height / 208
        return CGPoint(x: sprite.minX + visiblePet.midX * unit, y: sprite.maxY - (below ? visiblePet.maxY : visiblePet.minY) * unit)
    }
    var windowSize: CGSize {
        CGSize(width: max(Self.bubbleSize.width, spriteSize.width) + 24,
               height: spriteSize.height + Self.bubbleSize.height + 24 + Self.gap(scale: scale))
    }
    var spriteRect: CGRect {
        CGRect(x: (windowSize.width - spriteSize.width) / 2, y: 12,
               width: spriteSize.width, height: spriteSize.height)
    }
    var bubbleRect: CGRect {
        CGRect(x: (windowSize.width - Self.bubbleSize.width) / 2, y: spriteRect.maxY + Self.gap(scale: scale),
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
        var sprite = PetLayout.constrainedSprite(CGRect(origin: spriteOrigin, size: size), screen: visibleFrame)
        let bubbleSize = PetLayout.bubbleSize
        bubbleBelow = sprite.maxY + PetLayout.gap(scale: scale) + bubbleSize.height > safe.maxY
        let gap = PetLayout.gap(scale: scale, below: bubbleBelow)
        if bubbleBelow { sprite.origin.y = max(sprite.minY, safe.minY + gap + bubbleSize.height) }
        let bubble = CGRect(x: min(max(sprite.midX - bubbleSize.width / 2, safe.minX), max(safe.minX, safe.maxX - bubbleSize.width)),
                            y: bubbleBelow ? sprite.minY - gap - bubbleSize.height : sprite.maxY + gap,
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
