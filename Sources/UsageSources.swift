import Foundation
import Security
import Darwin

// This layer never logs payloads, credentials, accounts, or transport errors.
final class UsageSources {
    static let directSettingsKey = "claudeUsageDirectEnabled"
    private let home: URL
    private let environment: [String: String]
    init(home: URL = FileManager.default.homeDirectoryForCurrentUser, environment: [String: String] = ProcessInfo.processInfo.environment) {
        self.home = home; self.environment = environment
    }
    private func readObject(_ url: URL, limit: Int = 8 * 1024 * 1024) -> [String: Any]? {
        guard let file = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? file.close() }
        guard let bytes = try? file.read(upToCount: limit + 1), bytes.count <= limit else { return nil }
        return UsageParser.object(bytes)
    }
    func claudeAccount() -> UsageAccount {
        let custom = environment["CLAUDE_CONFIG_DIR"].map { URL(fileURLWithPath: $0).appendingPathComponent(".claude.json") }
        let url = custom.flatMap { FileManager.default.fileExists(atPath: $0.path) ? $0 : nil } ?? home.appendingPathComponent(".claude.json")
        return UsageParser.claudeAccount(readObject(url, limit: 1024 * 1024) ?? [:])
    }
    func history() -> ServiceUsage? {
        guard let object = readObject(home.appendingPathComponent("Library/Application Support/Claude/plan-usage-history.json")) else { return nil }
        return UsageParser.history(object, account: claudeAccount())
    }
    func codex() -> (ServiceUsage?, String) {
        let candidates = ["/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex", "/Applications/ChatGPT.app/Contents/Resources/codex",
                          home.appendingPathComponent(".local/bin/codex").path, "/opt/homebrew/bin/codex", "/usr/local/bin/codex"]
        guard let path = candidates.first(where: { FileManager.default.isExecutableFile(atPath: $0) }) else { return (nil, "Codex 를 찾을 수 없음") }
        let process = Process(), input = Pipe(), output = Pipe()
        process.executableURL = URL(fileURLWithPath: path); process.arguments = ["app-server", "--listen", "stdio://"]
        process.standardInput = input; process.standardOutput = output; process.standardError = FileHandle.nullDevice
        defer {
            try? input.fileHandleForWriting.close()
            if process.isRunning {
                process.terminate()
                let end = ProcessInfo.processInfo.systemUptime + 0.25
                while process.isRunning && ProcessInfo.processInfo.systemUptime < end { usleep(10_000) }
                if process.isRunning { kill(process.processIdentifier, SIGKILL) }
            }
            if process.processIdentifier > 0 { process.waitUntilExit() }
            try? output.fileHandleForReading.close()
        }
        do { try process.run() } catch { return (nil, "Codex 연결 실패") }
        let descriptor = output.fileHandleForReading.fileDescriptor
        let flags = fcntl(descriptor, F_GETFL)
        guard flags >= 0, fcntl(descriptor, F_SETFL, flags | O_NONBLOCK) >= 0 else { return (nil, "Codex 연결 실패") }
        func send(_ message: [String: Any]) -> Bool {
            guard var data = try? JSONSerialization.data(withJSONObject: message) else { return false }
            data.append(10); return (try? input.fileHandleForWriting.write(contentsOf: data)) != nil
        }
        guard send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "aipet", "version": "1.0.10"]]]) else { return (nil, "Codex 연결 실패") }
        var buffer = Data(), total = 0, account: [String: Any]?, limits: [String: Any]?
        var initialized = false
        let deadline = ProcessInfo.processInfo.systemUptime + 10
        var bytes = [UInt8](repeating: 0, count: 8192)
        while ProcessInfo.processInfo.systemUptime < deadline {
            let count = Darwin.read(descriptor, &bytes, bytes.count)
            if count > 0 {
                total += count
                guard total <= 1024 * 1024 else { return (nil, "Codex 응답 크기 초과") }
                buffer.append(contentsOf: bytes.prefix(count))
                while let end = buffer.firstIndex(of: 10) {
                    let line = Data(buffer[..<end]); buffer.removeSubrange(...end)
                    guard let object = UsageParser.object(line), let id = UsageParser.number(object["id"]) else { continue }
                    if id == 1 && !initialized {
                        guard object["error"] == nil,
                              send(["method": "initialized", "params": [:]]),
                              send(["id": 2, "method": "account/read", "params": [:]]),
                              send(["id": 3, "method": "account/rateLimits/read", "params": [:]]) else { return (nil, "Codex 연결 실패") }
                        initialized = true
                    } else if id == 2 { account = object }
                    else if id == 3 { limits = object }
                }
                if let account, let limits {
                    let value = UsageParser.codex(account: account, limits: limits, now: Date())
                    return (value, value == nil ? "Codex 사용량 조회 실패" : "")
                }
            } else if count == 0 { break }
            else if errno != EAGAIN && errno != EWOULDBLOCK && errno != EINTR { break }
            usleep(10_000)
        }
        return (nil, "Codex 연결 실패")
    }
    func direct(allowed: () -> Bool) -> (ServiceUsage?, DirectUsageStatus) {
        guard allowed() else { return (nil, .disabled) }
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "Claude Code-credentials", kSecMatchLimit as String: kSecMatchLimitOne, kSecReturnData as String: true]
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        // Permission dialogs can outlive an option change or pause. Recheck before sending a token.
        guard allowed() else { return (nil, .disabled) }
        if status == errSecUserCanceled || status == errSecAuthFailed || status == errSecInteractionNotAllowed { return (nil, .denied) }
        guard status == errSecSuccess, let bytes = item as? Data, bytes.count <= 1024 * 1024,
              let credentials = UsageParser.credentials(bytes) else { return (nil, .missing) }
        if let state = credentials.status(at: Date()) { return (nil, state) }
        let account = claudeAccount()
        guard allowed() else { return (nil, .disabled) }
        let response = UsageHTTP.request(token: credentials.token)
        return UsageParser.direct(status: response.0, data: response.1, account: account, now: Date())
    }
    func snapshot() -> UsageState {
        var state = UsageState()
        let result = codex(); state.codex = result.0; state.codexReason = result.1; state.history = history()
        return state
    }
}

// Ephemeral transport with a hard streaming cap. Redirects never receive the bearer token.
private final class UsageHTTP: NSObject, URLSessionDataDelegate {
    private var bytes = Data(), status = 0
    private let finished = DispatchSemaphore(value: 0)
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard response.expectedContentLength <= 1024 * 1024 else { completionHandler(.cancel); return }
        status = (response as? HTTPURLResponse)?.statusCode ?? 0; completionHandler(.allow)
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard bytes.count + data.count <= 1024 * 1024 else { status = 0; bytes.removeAll(); dataTask.cancel(); return }
        bytes.append(data)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if error != nil { status = 0; bytes.removeAll() }; finished.signal()
    }
    static func request(token: String) -> (Int, Data) {
        let delegate = UsageHTTP(), queue = OperationQueue(); queue.maxConcurrentOperationCount = 1
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil; config.urlCache = nil; config.httpShouldSetCookies = false
        config.requestCachePolicy = .reloadIgnoringLocalCacheData; config.timeoutIntervalForRequest = 5; config.timeoutIntervalForResource = 5
        let session = URLSession(configuration: config, delegate: delegate, delegateQueue: queue)
        var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
        request.httpMethod = "GET"; request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta"); request.setValue("application/json", forHTTPHeaderField: "Accept")
        let task = session.dataTask(with: request); task.resume()
        guard delegate.finished.wait(timeout: .now() + 5) == .success else { session.invalidateAndCancel(); return (0, Data()) }
        session.invalidateAndCancel()
        return (delegate.status, delegate.bytes)
    }
}

// Only the main thread calls configure/refresh; all source I/O and mutable cache live on one queue.
final class UsageMonitor {
    private let queue = DispatchQueue(label: "com.jinoisfree.taesik.usage", qos: .utility)
    private let sources = UsageSources()
    private var state = UsageState(), retry = UsageRetry()
    private var codexAt = Date.distantPast, historyAt = Date.distantPast, directAt = Date.distantPast
    private var busy = false, generation = 0
    private let lock = NSLock()
    private var paused = false, enabled = false
    func configure(paused: Bool, enabled: Bool) {
        lock.lock(); let changed = self.enabled != enabled
        self.paused = paused; self.enabled = enabled
        if changed { generation += 1 }; lock.unlock()
        if changed { queue.async { self.retry = UsageRetry(); self.directAt = .distantPast; self.state.direct = nil; self.state.directSuccess = nil; self.state.directStatus = .disabled } }
    }
    private func allowed(direct: Bool = false) -> Bool { lock.lock(); defer { lock.unlock() }; return !paused && (!direct || enabled) }
    func refresh(completion: @escaping (UsageState) -> Void) {
        guard !busy, allowed() else { return }; busy = true
        lock.lock(); let version = generation; let directEnabled = enabled; lock.unlock()
        queue.async {
            let now = Date()
            if self.allowed(), now >= self.codexAt {
                let result = self.sources.codex(); self.state.codex = UsageState.retaining(self.state.codex, success: result.0)
                self.state.codexReason = result.1; self.codexAt = Date().addingTimeInterval(300)
            }
            if self.allowed(), now >= self.historyAt {
                self.state.history = UsageState.retaining(self.state.history, success: self.sources.history()); self.historyAt = Date().addingTimeInterval(30)
            }
            if self.allowed(direct: true), !self.retry.denied, now >= self.directAt {
                let result = self.sources.direct { self.allowed(direct: true) }; self.state.direct = UsageState.retaining(self.state.direct, success: result.0)
                self.state.directStatus = result.1
                if result.0 != nil { self.state.directSuccess = Date() }
                self.directAt = Date().addingTimeInterval(self.retry.interval(after: result.1))
            }
            self.state.directEnabled = directEnabled
            let result = self.state
            DispatchQueue.main.async {
                self.busy = false
                self.lock.lock(); let current = self.generation; self.lock.unlock()
                if self.allowed(), current == version { completion(result) }
            }
        }
    }
}
