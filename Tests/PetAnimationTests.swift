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
        // Nodding off: only after three quiet minutes, and anything happening starts the count again.
        var doze = PetDoze()
        precondition(doze.update(now: 10, quiet: true) == nil && doze.update(now: 189.9, quiet: true) == nil, "3분이 되기 전에는 졸지 않는다")
        precondition(doze.update(now: 190.5, quiet: true) == 0.5, "조용한 3분 뒤에 졸기 시작한다")
        precondition(doze.update(now: 200, quiet: false) == nil, "무슨 일이 생기면 깬다")
        precondition(doze.update(now: 201, quiet: true) == nil && doze.update(now: 380, quiet: true) == nil, "깬 뒤에는 3분을 다시 센다")
        precondition(doze.update(now: 381, quiet: true) == 0)
        precondition(doze.update(now: 500, quiet: false) == nil && doze.update(now: 501, quiet: true, after: 1) == nil)
        precondition(doze.update(now: 503, quiet: true, after: 1) == 1, "확인용으로 기다림을 줄일 수 있다")
        let steps = PetAnimationTimeline.dozeSteps
        precondition(steps.map(\.image) == [0, 1, 2, 3, 4, 3, 2, 3, 4, 3, 2, 3, 4, 3, 1], "눈 뜸, 한쪽 감김, 감은 채 세 번 까딱, 번쩍")
        precondition(steps.map(\.zs) == [0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 0], "까딱일 때마다 z가 하나씩 는다")
        precondition(abs(PetAnimationTimeline.dozeCycle - 5.82) < 0.0001)
        let columns = [0, 0.89, 0.91, 1.36, 1.96, 2.16, 2.60, 5.66, 5.68, 5.83].map { PetAnimationTimeline.dozeFrame(elapsed: $0).column }
        precondition(columns == [0, 0, 1, 2, 3, 4, 5, 13, 14, 0], "순서대로 돌고 처음으로 돌아온다")
        precondition(PetAnimationTimeline.dozeFrame(elapsed: 100).row == PetAnimationTimeline.dozeRow)
        precondition(PetAnimationTimeline.dozeFrame(elapsed: 0.3, reducedMotion: true) == PetAnimationFrame(row: 11, column: 10), "동작 줄이기에서는 잠든 한 장면")
        precondition(PetAnimationTimeline.dozeZs(column: 10).count == 3)
        for column in steps.indices {
            let zs = PetAnimationTimeline.dozeZs(column: column)
            precondition(zs.allSatisfy { CGRect(x: 2, y: 2, width: 188, height: 204).contains($0) }, "z는 펫의 칸 안에 그린다")
            precondition(zip(zs, zs.dropFirst()).allSatisfy { $1.width > $0.width && $1.minY < $0.minY && $1.minX > $0.maxX }, "위로 갈수록 커진다")
        }
        precondition(PetAnimationTimeline.dozeZs(column: 99).isEmpty)
        print("PASS: 16 look directions, drag direction, original timing, idle return, reduced motion, nodding off")
    }
}
