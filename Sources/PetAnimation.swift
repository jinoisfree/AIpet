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

    /// Nodding off is the idle pose with only its head changing: eyes open, one closing, then both shut while the head
    /// nods forward three times and a z rises with each, then a start awake.
    static let dozeRow = 11
    /// Which drawing each step shows (0 eyes open, 1 one shut, 2 both shut, 3 and 4 the head lower and lowest), for how long, with how many z's.
    static let dozeSteps: [(image: Int, seconds: Double, zs: Int)] =
        [(0, 0.900, 0), (1, 0.450, 0)] + (1...3).flatMap { [(2, 0.600, $0), (3, 0.200, $0), (4, 0.440, $0), (3, 0.200, $0)] } + [(1, 0.150, 0)]
    static let dozeCycle = dozeSteps.reduce(0) { $0 + $1.seconds }
    /// With motion turned down the pet simply sleeps: the last step with its head up and all three z's.
    static let dozeStill = dozeSteps.lastIndex { $0.image == 2 } ?? 0
    static func dozeFrame(elapsed: TimeInterval, reducedMotion: Bool = false) -> PetAnimationFrame {
        if reducedMotion { return PetAnimationFrame(row: dozeRow, column: dozeStill) }
        var remaining = max(0, elapsed.isFinite ? elapsed : 0).truncatingRemainder(dividingBy: dozeCycle)
        for (column, step) in dozeSteps.enumerated() {
            if remaining < step.seconds { return PetAnimationFrame(row: dozeRow, column: column) }
            remaining -= step.seconds
        }
        return PetAnimationFrame(row: dozeRow, column: dozeSteps.count - 1)
    }
    /// The z's over a doze frame, each as the square it is written in, measured in the sprite cell from its top-left corner.
    static func dozeZs(column: Int) -> [CGRect] {
        guard dozeSteps.indices.contains(column) else { return [] }
        let start = CGPoint(x: 132, y: 56)
        return (0..<dozeSteps[column].zs).map { index in
            let size: CGFloat = [6, 8, 10.5][index]
            return CGRect(x: start.x + [0, 9.5, 21.5][index], y: start.y - [0, 12.5, 27.5][index], width: size, height: size)
        }
    }
}

/// The pet nods off once nothing has happened for a while, and anything happening wakes it.
struct PetDoze {
    static let delay: TimeInterval = 180
    private var quietSince: TimeInterval?
    /// `quiet` means the pet is idle with nothing to look at or react to. Returns how long it has been dozing.
    mutating func update(now: TimeInterval, quiet: Bool, after delay: TimeInterval = PetDoze.delay) -> TimeInterval? {
        guard quiet else { quietSince = nil; return nil }
        let since = quietSince ?? now
        quietSince = since
        return now - since >= delay ? now - since - delay : nil
    }
}
