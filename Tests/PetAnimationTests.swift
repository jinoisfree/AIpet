import Foundation
import CoreGraphics

@main
struct PetAnimationTests {
    static func main() {
        let origin = CGPoint(x: -1200, y: 200)
        for index in 0..<16 {
            let angle = Double(index) * .pi / 8
            let target = CGPoint(x: origin.x + sin(angle) * 100, y: origin.y + cos(angle) * 100)
            precondition(PetAnimationTimeline.lookFrame(from: origin, toward: target) ==
                         PetAnimationFrame(row: 9 + index / 8, column: index % 8), "16방향 및 다중 모니터 좌표")
        }
        precondition(PetAnimationTimeline.lookFrame(from: origin, toward: origin) == nil)
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: CGPoint(x: CGFloat.nan, y: 0)) == nil)
        let up = PetAnimationFrame(row: 9, column: 0)
        let next = PetAnimationFrame(row: 9, column: 1)
        func direction(_ degrees: Double) -> CGPoint { CGPoint(x: sin(degrees * .pi / 180) * 100, y: cos(degrees * .pi / 180) * 100) }
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: direction(12), previous: up) == up, "direction boundary noise does not flicker")
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: direction(14), previous: up) == next, "intentional direction change still follows cursor")
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: direction(11), previous: next) == next, "reverse boundary has the same margin")
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: direction(348), previous: up) == up, "angular margin wraps across zero")
        precondition(PetAnimationTimeline.lookFrame(from: .zero, toward: .zero, previous: next) == next, "center dead zone retains last look instead of popping to idle")
        precondition(PetAnimationTimeline.dragAnimation(horizontalDelta: -3) == .left)
        precondition(PetAnimationTimeline.dragAnimation(horizontalDelta: 3) == .right)
        precondition(PetAnimationTimeline.dragAnimation(horizontalDelta: 0, previous: .left) == .left)
        precondition(PetAnimationTimeline.frame(for: .running, elapsed: 0.13).column == 1)
        precondition(PetAnimationTimeline.frame(for: .running, elapsed: 0.7).column == 5)
        precondition(PetAnimationTimeline.frame(for: .running, elapsed: 2.47).row == 0, "세 번 재생 후 대기로 복귀")
        precondition(PetAnimationTimeline.frame(for: .left, elapsed: 4, loop: true).row == 2, "드래그 중 방향 유지")
        precondition(PetAnimationTimeline.frame(for: .idle, elapsed: 1.67).column == 0)
        precondition(PetAnimationTimeline.frame(for: .idle, elapsed: 1.69).column == 1)
        for animation in PetAnimation.allCases {
            precondition(PetAnimationTimeline.frame(for: animation, elapsed: 30, reducedMotion: true) ==
                         PetAnimationFrame(row: animation.rawValue, column: 0), "동작 줄이기 준수")
            for time in stride(from: 0.0, to: 30, by: 0.05) {
                let frame = PetAnimationTimeline.frame(for: animation, elapsed: time)
                precondition((0..<9).contains(frame.row) && (0..<8).contains(frame.column), "유효한 스프라이트 셀")
            }
        }
        precondition(!PetAnimation.waiting.allowsLook && !PetAnimation.failed.allowsLook)
        print("PASS: 16 look directions, drag direction, original timing, idle return, reduced motion")
    }
}
