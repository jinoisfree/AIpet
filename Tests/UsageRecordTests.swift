import Foundation

@main struct UsageRecordTests {
    static var count = 0
    static func check(_ value: @autoclosure () -> Bool) { count += 1; precondition(value(), "usage record check \(count)") }
    static func main() throws {
        let now = Date(), reset = now.addingTimeInterval(172800)
        func line(_ at: Date, used: Any = 28, minutes: Int = 300) -> [String: Any] {
            ["type": "event_msg", "timestamp": ISO8601DateFormatter().string(from: at), "payload": ["type": "token_count", "rate_limits": ["primary": ["used_percent": used, "window_minutes": minutes, "resets_at": reset.timeIntervalSince1970], "secondary": NSNull(), "plan_type": "example"]]]
        }
        let sample = UsageParser.codexRecord(line(now.addingTimeInterval(-60)))!
        check(sample.source == .codexRecord && sample.windows.count == 1)
        check(sample.display(at: now) == "72%")
        check(sample.account.label == "계정 확인 불가" && sample.account.detail == "example")
        check(UsageParser.codexRecord(line(now, minutes: 10080))?.display(at: now) == "주 72%")
        check(UsageParser.codexRecord(line(now, minutes: 60)) == nil)
        for used: Any in [-1.0, 101.0, Double.nan, Double.infinity, true, false] { check(UsageParser.codexRecord(line(now, used: used)) == nil) }
        var missing = line(now); missing.removeValue(forKey: "timestamp"); check(UsageParser.codexRecord(missing) == nil)
        missing["timestamp"] = "broken"; check(UsageParser.codexRecord(missing) == nil)
        check(sample.valid(at: sample.observedAt.addingTimeInterval(86399)))
        check(!sample.valid(at: sample.observedAt.addingTimeInterval(86400)))
        var expired = sample; expired.windows[0].resetsAt = now; check(expired.display(at: now) == "—")
        var state = UsageState(); state.codexRecord = sample
        state.codex = ServiceUsage(source: .codex, observedAt: sample.observedAt.addingTimeInterval(-1), windows: sample.windows, account: UsageAccount(email: "account@example.com", verified: true))
        check(state.codexUsage?.source == .codexRecord)
        check(state.subtitle(at: now, elapsed: 5) == "Codex 계정 확인 불가")
        check(state.codexUsage?.account.email == nil)
        state.codex?.observedAt = now; check(state.codexUsage?.source == .codex)
        check(state.codexFailure(at: now).isEmpty)
        state.codex = nil; check(state.codexFailure(at: sample.observedAt.addingTimeInterval(86400)) == "최근 24시간 Codex 작업 기록 없음")
        state.codexRecord = nil; state.codexReason = "Codex 를 찾을 수 없음"
        check(state.codexFailure(at: now) == "Codex 를 찾을 수 없음 · Codex 로 작업하면 기록에서 표시됩니다")
        state.codexReason = "Codex 앱 실행 불가"; check(state.codexFailure(at: now).hasPrefix("Codex 앱 실행 불가 ·"))
        check(state.claudeFailure(at: now) == "옵션에서 Claude 사용량 직접 조회를 켜세요")
        state.directEnabled = true; state.directStatus = .expired; check(state.claudeFailure(at: now) == "로그인 만료")
        var decoder = TranscriptDecoder(provider: .codex, sessionID: "test")
        _ = decoder.consume(["type": "event_msg", "timestamp": ISO8601DateFormatter().string(from: now.addingTimeInterval(-120)), "payload": ["type": "task_complete"]])
        let before = decoder.last
        check(decoder.consume(line(now)) == nil)
        check(decoder.last == before)
        check(decoder.latestUsage?.display(at: now) == "72%")
        _ = decoder.consume(line(now.addingTimeInterval(-60), used: 99)); check(decoder.latestUsage?.display(at: now) == "72%")
        _ = decoder.consume(["type": "session_meta", "payload": ["id": "child", "parent_thread_id": "parent"]])
        check(decoder.consume(line(now.addingTimeInterval(1), used: 42)) == nil)
        check(decoder.latestUsage?.windows[0].used == 42 && decoder.last == before)
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        for (name, at, used) in [("older", now.addingTimeInterval(-120), 99), ("newer", now.addingTimeInterval(-60), 28)] {
            var data = try JSONSerialization.data(withJSONObject: line(at, used: used)); data.append(10)
            try data.write(to: root.appendingPathComponent(name + ".jsonl"))
        }
        let monitor = StatusMonitor(codexRoot: root, claudeRoot: root.appendingPathComponent("none"), eventRoot: root.appendingPathComponent("events"))
        let snapshot = monitor.poll(now: now, forceDiscovery: true)
        check(snapshot.tasks.isEmpty); check(monitor.latestCodexUsage?.windows[0].used == 28)
        state.codexRecord = monitor.latestCodexUsage
        let diagnostic = try JSONSerialization.data(withJSONObject: state.diagnostic(at: now))
        check(!String(decoding: diagnostic, as: UTF8.self).contains("account@example.com"))
        check(state.diagnostic(at: now)["codexExecutable"] as? String == "none")
        print("UsageRecordTests: \(count) checks passed")
    }
}
