import Foundation
import CoreGraphics

@main
struct PetLayoutTests {
    static func main() {
        let small = PetLayout(scale: 0.5), large = PetLayout(scale: 2)
        precondition(small.bubbleRect.size == large.bubbleRect.size, "캐릭터 배율이 말풍선에 영향을 주면 안 됩니다.")
        precondition(small.bubbleRect.size == CGSize(width: 330, height: 56), "말풍선 기준 크기가 유지되어야 합니다.")
        precondition(large.spriteRect.width == small.spriteRect.width * 4, "캐릭터는 요청한 배율로 바뀌어야 합니다.")
        precondition(PetLayout(scale: 0.25).spriteSize == CGSize(width: 48, height: 52))
        for scale in [0.25, 0.35, 0.5, 0.8, 1.0, 1.35, 2.0] {
            let layout = PetLayout(scale: scale)
            let window = CGRect(origin: .zero, size: layout.windowSize)
            precondition(window.contains(layout.bubbleRect) && window.contains(layout.spriteRect), "크기 변경 후 내용이 잘리면 안 됩니다.")
            precondition(!layout.bubbleRect.intersects(layout.spriteRect), "말풍선과 캐릭터가 겹치면 안 됩니다.")
            let head = PetLayout.trailEnd(sprite: layout.spriteRect, below: false)
            precondition(layout.bubbleRect.minY - head.y >= ThoughtTrail.length - 0.001, "말풍선과 머리 사이에 생각 방울이 들어갈 자리")
            precondition(abs(head.x - layout.bubbleRect.midX) <= 17 * scale + 0.001 && head.y < layout.spriteRect.maxY)
        }
        for screen in [CGRect(x: 0, y: 30, width: 1440, height: 870),
                       CGRect(x: -1920, y: -250, width: 1920, height: 1050),
                       CGRect(x: 0, y: 0, width: 800, height: 520)] {
            for scale in [0.25, 0.5, 1, 2] {
                for x in [screen.minX - 1000, screen.midX, screen.maxX + 100] {
                    for y in [screen.minY - 1000, screen.midY, screen.maxY + 100] {
                        let placement = PetPlacement(spriteOrigin: CGPoint(x: x, y: y), scale: scale, visibleFrame: screen)
                        let globalSprite = placement.spriteRect.offsetBy(dx: placement.windowFrame.minX, dy: placement.windowFrame.minY)
                        let globalBubble = placement.bubbleRect.offsetBy(dx: placement.windowFrame.minX, dy: placement.windowFrame.minY)
                        let visible = PetLayout.visibleSprite(in: globalSprite)
                        precondition(screen.insetBy(dx: 4, dy: 4).insetBy(dx: -0.001, dy: -0.001).contains(visible), "보이는 그림은 화면 안 4pt")
                        precondition(screen.insetBy(dx: 12, dy: 12).contains(globalBubble), "말풍선은 화면 안 12pt")
                        if x < screen.minX { precondition(abs(visible.minX - screen.minX - 4) < 0.001) }
                        if x > screen.maxX { precondition(abs(visible.maxX - screen.maxX + 4) < 0.001) }
                        if y < screen.minY { precondition(abs(visible.minY - screen.minY - 4) < 0.001) }
                        if y > screen.maxY { precondition(abs(visible.maxY - screen.maxY + 4) < 0.001) }
                        let restored = PetPlacement(spriteOrigin: globalSprite.origin, scale: scale, visibleFrame: screen)
                        precondition(restored.globalSprite == globalSprite, "저장 위치를 복원해도 그림이 움직이지 않음")
                        precondition(!globalSprite.intersects(globalBubble), "말풍선이 펫을 가리지 않음")
                        precondition(globalBubble.size == CGSize(width: 330, height: 56), "가장자리에서도 말풍선 고정 크기")
                        let below = placement.bubbleBelow, bubble = placement.bubbleRect
                        let end = PetLayout.trailEnd(sprite: placement.spriteRect, below: below)
                        let circles = ThoughtTrail.circles(bubble: bubble, pet: end, below: below)
                        let window = CGRect(origin: .zero, size: placement.windowFrame.size)
                        precondition(circles.map(\.width) == [15, 10.5, 6.5] && circles.allSatisfy { $0.width == $0.height }, "큰 방울부터 작은 방울까지 셋")
                        precondition(circles.allSatisfy { window.contains($0) && !$0.intersects(bubble) }, "방울은 창 안, 말풍선 밖")
                        precondition(circles.allSatisfy { below ? $0.minY > bubble.maxY && $0.maxY <= end.y + 0.001 : $0.maxY < bubble.minY && $0.minY >= end.y - 0.001 },
                                     "방울은 말풍선과 펫 사이에만")
                        precondition(zip(circles, circles.dropFirst()).allSatisfy { below ? $1.minY > $0.maxY : $1.maxY < $0.minY }, "서로 겹치지 않고 펫 쪽으로 이어짐")
                        precondition(circles[0].minX >= bubble.minX + 14 && circles[0].maxX <= bubble.maxX - 14, "첫 방울은 말풍선의 둥근 모서리 안쪽")
                        precondition(abs(circles[2].midX - end.x) <= abs(circles[0].midX - end.x) + 0.001, "펫 쪽으로 다가감")
                    }
                }
            }
        }
        let left = PetPlacement(spriteOrigin: .zero, scale: 0.25, visibleFrame: CGRect(x: 0, y: 0, width: 1000, height: 800))
        precondition(left.globalSprite.minX == 0 && left.globalBubble.minX == 12, "그림은 허용된 위치를 유지하고 말풍선만 화면 안으로 이동")
        let top = PetPlacement(spriteOrigin: CGPoint(x: 988, y: 790), scale: 0.25, visibleFrame: CGRect(x: 0, y: 0, width: 1000, height: 800))
        precondition(top.bubbleBelow && PetLayout.visibleSprite(in: top.globalSprite).maxX == 996 && top.globalBubble.maxX == 988, "우측 그림 4pt, 아래 말풍선 12pt")
        let screen = CGRect(x: 0, y: 0, width: 1000, height: 800)
        var motion = PetBubbleMotion()
        motion.retarget(CGPoint(x: 12, y: 12), visibleFrame: screen, now: 0)
        motion.retarget(CGPoint(x: 658, y: 732), visibleFrame: screen, now: 0)
        let first = motion.step(now: 1.0 / 30)
        precondition(first.x > 12 && first.x < 658 && first.y > 12 && first.y < 732, "순간 이동하지 않고 양 축에서 보간")
        var previous = first
        for frame in 2...60 {
            let next = motion.step(now: Double(frame) / 30)
            precondition(next.x >= previous.x && next.y >= previous.y, "과도한 튕김 없이 부드럽게 접근")
            precondition(screen.contains(CGRect(origin: next, size: PetLayout.bubbleSize)), "전환 도중 화면 안에 유지")
            previous = next
        }
        precondition(previous == CGPoint(x: 658, y: 732), "목표 위치에 도착")
        motion.retarget(CGPoint(x: -2000, y: -2000), visibleFrame: screen, now: 2)
        let reverse = motion.step(now: 2.03)
        precondition(reverse.x < previous.x && reverse.x > 12 && reverse.y < previous.y && reverse.y > 12, "반대 방향 전환도 보간")
        motion.retarget(CGPoint(x: 100, y: 100), visibleFrame: screen, now: 3, immediately: true)
        precondition(motion.step(now: 3) == CGPoint(x: 100, y: 100), "동작 줄이기에서는 즉시 배치")
        print("PASS: fixed bubble and sprite-only scaling at 25, 35, 50, 80, 100, 135, 200 percent")
        print("PASS: edge placement, above/below flipping, off-screen recovery, multiple display coordinates, thought trail")
        print("PASS: smooth two-axis following, reversal, settling, transition bounds, reduced motion")
    }
}
