import Foundation
import CoreGraphics

enum PetAnimation: Int, CaseIterable {
    case idle = 0, right, left, waving, jumping, failed, waiting, running, review

    init(state: WorkState) { self = PetAnimation(rawValue: state.row) ?? .idle }
    var name: String {
        ["쉬는 중", "오른쪽 이동", "왼쪽 이동", "손 흔들기", "점프", "오류", "확인 대기", "작업 중", "검토"][rawValue]
    }
    var allowsLook: Bool { self == .idle || self == .running || self == .waving }
}

struct PetAnimationFrame: Equatable {
    let row: Int
    let column: Int
}

/// Codex v2 timing: play an action three times, then return to the slow idle loop.
enum PetAnimationTimeline {
    private static let idleDurations: [Double] = [0.280, 0.110, 0.110, 0.140, 0.140, 0.320]

    static func frame(for animation: PetAnimation, elapsed: TimeInterval,
                      reducedMotion: Bool = false, loop: Bool = false) -> PetAnimationFrame {
        if reducedMotion { return PetAnimationFrame(row: animation.rawValue, column: 0) }
        let durations: [Double]
        switch animation {
        case .idle: durations = idleDurations.map { $0 * 6 }
        case .right, .left: durations = Array(repeating: 0.120, count: 7) + [0.220]
        case .running: durations = Array(repeating: 0.120, count: 5) + [0.220]
        case .waving: durations = Array(repeating: 0.140, count: 3) + [0.280]
        case .jumping: durations = Array(repeating: 0.140, count: 4) + [0.280]
        case .failed: durations = Array(repeating: 0.140, count: 7) + [0.240]
        case .waiting: durations = Array(repeating: 0.150, count: 5) + [0.260]
        case .review: durations = Array(repeating: 0.150, count: 5) + [0.280]
        }
        let cycle = durations.reduce(0, +)
        let time = max(0, elapsed.isFinite ? elapsed : 0)
        if animation != .idle && !loop && time >= cycle * 3 {
            return frame(for: .idle, elapsed: time - cycle * 3)
        }
        var remaining = time.truncatingRemainder(dividingBy: cycle)
        for (column, duration) in durations.enumerated() {
            if remaining < duration { return PetAnimationFrame(row: animation.rawValue, column: column) }
            remaining -= duration
        }
        return PetAnimationFrame(row: animation.rawValue, column: durations.count - 1)
    }

    /// AppKit global screen coordinates are bottom-up. Index 0 points up; clockwise in 22.5° steps.
    static func lookFrame(from origin: CGPoint, toward target: CGPoint, previous: PetAnimationFrame? = nil) -> PetAnimationFrame? {
        let dx = target.x - origin.x, dy = target.y - origin.y
        guard dx.isFinite, dy.isFinite else { return nil }
        let previousLook = previous.flatMap { (9...10).contains($0.row) && (0..<8).contains($0.column) ? $0 : nil }
        guard hypot(dx, dy) > 1 else { return previousLook }
        let angle = (atan2(dx, dy) * 180 / .pi + 360).truncatingRemainder(dividingBy: 360)
        if let previousLook {
            let center = Double((previousLook.row - 9) * 8 + previousLook.column) * 22.5
            let distance = (angle - center + 540).truncatingRemainder(dividingBy: 360) - 180
            // A small angular margin prevents noisy boundary crossings from flickering between poses.
            if abs(distance) <= 13.25 { return previousLook }
        }
        let direction = Int((angle / 22.5).rounded()) % 16
        return PetAnimationFrame(row: 9 + direction / 8, column: direction % 8)
    }

    static func dragAnimation(horizontalDelta: CGFloat, previous: PetAnimation = .right) -> PetAnimation {
        abs(horizontalDelta) < 0.5 ? previous : horizontalDelta < 0 ? .left : .right
    }
}
