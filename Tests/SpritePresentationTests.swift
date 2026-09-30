import Foundation
import CoreGraphics
import ImageIO

@main
struct SpritePresentationTests {
    static func main() throws {
        let input = URL(fileURLWithPath: CommandLine.arguments[1])
        let output = URL(fileURLWithPath: CommandLine.arguments[2])
        let counts = [7, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8]
        let source = CGImageSourceCreateWithURL(input as CFURL, nil)!
        let atlas = CGImageSourceCreateImageAtIndex(source, 0, nil)!
        var images: [[CGImage]] = [], metrics: [[SpriteContentMetrics]] = []
        for (row, count) in counts.enumerated() {
            let frames = (0..<count).map { atlas.cropping(to: CGRect(x: $0 * 192, y: row * 208, width: 192, height: 208))! }
            images.append(frames); metrics.append(frames.map { SpriteContentMetrics.measure($0)! })
        }
        let reference = metrics[10][7]
        precondition(reference.bounds == CGRect(x: 54, y: 60, width: 118, height: 134), "pixel orientation and source reference")
        let referenceTransform = PetSpritePresentation.matching(reference, to: reference)
        precondition(referenceTransform.scale == 1 && referenceTransform.offset == .zero, "smaller reference must remain unchanged")
        var transforms: [[PetSpritePresentation]] = []
        for (row, content) in metrics.enumerated() {
            transforms.append(content.map { PetSpritePresentation.matching(row >= 9 ? $0 : content[0], to: reference) })
        }
        let smaller = referenceTransform.contentBounds(reference.bounds)
        let formerlyLarge = transforms[9][0].contentBounds(metrics[9][0].bounds)
        precondition(abs(formerlyLarge.height - smaller.height) < 0.01, "reported 34% size jump removed")
        precondition(abs(formerlyLarge.midX - smaller.midX) < 0.01 && abs(formerlyLarge.maxY - smaller.maxY) < 0.01, "reported poses share center and ground")
        var directions: [CGRect] = []
        for row in 9...10 {
            for column in 0..<8 { directions.append(transforms[row][column].contentBounds(metrics[row][column].bounds)) }
        }
        for index in 0..<16 {
            precondition(abs(directions[index].height - directions[(index + 1) % 16].height) < 8, "adjacent look directions no longer jump in height")
        }
        for row in 0..<9 {
            let rest = transforms[row][0].contentBounds(metrics[row][0].bounds)
            precondition(rest.height <= reference.bounds.height + 0.01, "ordinary poses use the smaller size budget")
            precondition(abs(rest.maxY - reference.bounds.maxY) < 0.01 && abs(rest.midX - reference.bounds.midX) < 0.01, "state transition shares baseline")
            precondition(transforms[row].allSatisfy { $0.scale == transforms[row][0].scale && $0.offset == transforms[row][0].offset }, "authored animation never auto-zooms per frame")
        }
        let jumpRest = transforms[4][0].contentBounds(metrics[4][0].bounds)
        let jumpPeak = transforms[4][2].contentBounds(metrics[4][2].bounds)
        precondition(jumpRest.maxY - jumpPeak.maxY > 20, "jump retains vertical lift")
        precondition(transforms[5][7].contentBounds(metrics[5][7].bounds).height < 60, "lying-down action remains lying down")
        for scale in [0.25, 0.54, 1, 2] {
            let cell = CGRect(x: -1200, y: -300, width: 192 * scale, height: 208 * scale)
            let interaction = PetSpritePresentation.interactionRect(for: reference.bounds, in: cell)
            precondition(cell.contains(interaction) && abs(interaction.height - reference.bounds.height * scale) < 0.01, "hover and gaze origin follow corrected visible size")
            precondition(!interaction.contains(CGPoint(x: cell.minX + 1, y: cell.maxY - 1)), "transparent padding no longer triggers hover")
            for (row, frames) in metrics.enumerated() {
                for (column, metric) in frames.enumerated() {
                    let transform = transforms[row][column]
                    let rect = transform.drawingRect(in: cell)
                    precondition(transform.scale <= 1 && transform.scale > 0, "never enlarge a small original frame")
                    precondition(abs(rect.width / rect.height - 192.0 / 208.0) < 0.0001, "uniform aspect ratio")
                    let bounds = transform.contentBounds(metric.bounds)
                    precondition(bounds.minX >= 0 && bounds.maxX <= 192 && bounds.minY >= 0 && bounds.maxY <= 208, "visible pixels stay within cell")
                }
            }
        }

        func canvas(width: Int, height: Int) -> CGContext {
            let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
            ctx.setFillColor(CGColor(gray: 0.19, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: width, height: height))
            ctx.interpolationQuality = .high
            return ctx
        }
        func save(_ context: CGContext, _ filename: String) {
            let destination = CGImageDestinationCreateWithURL(output.appendingPathComponent(filename) as CFURL, "public.png" as CFString, 1, nil)!
            CGImageDestinationAddImage(destination, context.makeImage()!, nil)
            precondition(CGImageDestinationFinalize(destination))
        }
        let comparison = canvas(width: 768, height: 208)
        for (index, pose) in [(9, 0), (10, 7), (9, 0), (10, 7)].enumerated() {
            let cell = CGRect(x: index * 192, y: 0, width: 192, height: 208)
            comparison.draw(images[pose.0][pose.1], in: index < 2 ? cell : transforms[pose.0][pose.1].drawingRect(in: cell))
        }
        save(comparison, "sprite-size-comparison.png")
        let overview = canvas(width: 1536, height: 2288)
        for (row, frames) in images.enumerated() {
            for (column, image) in frames.enumerated() {
                let cell = CGRect(x: column * 192, y: (10 - row) * 208, width: 192, height: 208)
                overview.draw(image, in: transforms[row][column].drawingRect(in: cell))
            }
        }
        save(overview, "sprite-size-overview.png")
        print("PASS: 74 real frames; reference unchanged; 34% jump removed; 16-direction continuity; stable state baselines; jump/lying motion preserved; hover bounds; scale range and clipping")
    }
}
