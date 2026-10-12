import Foundation
import Darwin

@main struct UsageSourceTests {
    static func main() throws {
        // A reader that has already exited must produce EPIPE, never SIGPIPE.
        let pipe = Pipe()
        precondition(fcntl(pipe.fileHandleForWriting.fileDescriptor, F_SETNOSIGPIPE, 1) == 0)
        try pipe.fileHandleForReading.close()
        do {
            try pipe.fileHandleForWriting.write(contentsOf: Data([10]))
            preconditionFailure("closed reader must fail")
        } catch { }
        try pipe.fileHandleForWriting.close()
        let sources = UsageSources(home: URL(fileURLWithPath: "/nonexistent"), environment: [:])
        for _ in 0..<20 {
            let result = sources.codex(executablePath: "/usr/bin/true")
            precondition(result.0 == nil && result.1 == "Codex 연결 실패")
        }
        print("UsageSourceTests: closed-pipe and 20 immediate-exit checks passed")
    }
}
