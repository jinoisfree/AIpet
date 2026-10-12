import Foundation

@main struct UsageTests {
    static var count = 0
    static func check(_ value: @autoclosure () -> Bool, _ label: String) { count += 1; precondition(value(), label) }
    static func data(_ value: Any) -> Data { try! JSONSerialization.data(withJSONObject: value) }
    static func main() {
        let now = Date(timeIntervalSince1970: 1_800_000_000), reset = Date(timeIntervalSince1970: 1_800_003_600)
        let account = UsageAccount(email: "jino@example.com", detail: "Example", verified: true, organizationID: "org-a")
        let five = UsageWindow(period: .fiveHour, used: 28, resetsAt: reset)
        let week = UsageWindow(period: .weekly, used: 52, resetsAt: reset)
        var value = ServiceUsage(source: .codex, observedAt: now, windows: [five, week], account: account)
        check(value.display(at: now) == "주 48%", "least remaining weekly")
        value.windows = [five]; check(value.display(at: now) == "72%", "five hour display")
        check(value.display(at: now.addingTimeInterval(3600)) == "—", "60 minute TTL")
        value.source = .history; check(value.display(at: now.addingTimeInterval(1800)) == "—", "30 minute TTL")
        value.source = .direct; check(value.valid(at: now.addingTimeInterval(3599)), "direct TTL")
        value.windows = [UsageWindow(period: .fiveHour, used: 1, resetsAt: now)]
        check(value.display(at: now) == "—", "reset passed")
        value.windows = [UsageWindow(period: .fiveHour, used: -1, resetsAt: reset), week]
        check(value.display(at: now) == "주 48%", "invalid window ignored")
        for input in [-1.0, 101, Double.nan, Double.infinity] { check(UsageParser.percent(input) == nil, "invalid percent") }
        for input in [true, false] { check(UsageParser.percent(input) == nil, "boolean is not percent") }
        check(UsageParser.percent("42") == nil, "numeric string rejected")
        check(UsageParser.percent(0) == 0 && UsageParser.percent(100) == 100, "percent endpoints")
        for (email, expected) in [("jino@example.com", "ji***@example.com"), ("ab@example.com", "a***@example.com"), ("a@example.com", "a***@example.com"), ("abc@example.com", "ab***@example.com"), ("missing", "***"), ("@example.com", "***"), ("a@", "***")] {
            check(UsageAccount.mask(email) == expected, "mask")
        }
        check(UsageAccount(email: "secret@example.com").masked == "계정 확인 불가", "unverified account")
        let codexAccount: [String: Any] = ["result": ["account": ["email": "jino@example.com", "planType": "plus"]]]
        let window: [String: Any] = ["usedPercent": 28, "windowDurationMins": 300, "resetsAt": reset.timeIntervalSince1970]
        let rates: [String: Any] = ["primary": window, "secondary": NSNull()]
        let parsed = UsageParser.codex(account: codexAccount, limits: ["result": ["rateLimitsByLimitId": ["codex": rates]]], now: now)!
        check(parsed.windows.count == 1 && parsed.display(at: now) == "72%", "secondary null")
        check(parsed.account.label == "jino@example.com" && parsed.account.detail == "plus", "codex account")
        check(UsageParser.codex(account: [:], limits: ["result": ["rateLimits": rates]], now: now)?.display(at: now) == "72%", "legacy limits path")
        check(UsageParser.codex(account: [:], limits: [:], now: now) == nil, "missing limits")
        for invalid: Any in [true, 101, -1, Double.nan] {
            var row = window; row["usedPercent"] = invalid
            check(UsageParser.codex(account: [:], limits: ["result": ["rateLimits": ["primary": row]]], now: now) == nil, "invalid codex percent")
        }
        var long = window; long["windowDurationMins"] = 10080; long["usedPercent"] = 52
        check(UsageParser.codex(account: [:], limits: ["result": ["rateLimits": ["primary": window, "secondary": long]]], now: now)?.display(at: now) == "주 48%", "codex weekly")
        let config: [String: Any] = ["oauthAccount": ["emailAddress": "jino@example.com", "organizationName": "Example", "organizationUuid": "org-a"]]
        check(UsageParser.claudeAccount(config) == account, "claude account")
        let sample: [String: Any] = ["t": now.timeIntervalSince1970 * 1000, "org": "org-a", "u": ["fh": 28, "sd": 52]]
        let history = UsageParser.history(["samples": [sample]], account: account)!
        check(history.account == account && history.display(at: now) == "주 48%", "matching organization")
        var wrong = sample; wrong["org"] = "other"
        check(UsageParser.history(["samples": [wrong]], account: account)?.account.label == "계정 확인 불가", "mismatched organization")
        check(UsageParser.history(["samples": [sample]], account: UsageAccount())?.account.masked == "계정 확인 불가", "missing organization")
        check(UsageParser.history(["samples": [wrong, sample]], account: account)?.account.verified == true, "last sample")
        check(UsageParser.history(["samples": []], account: account) == nil, "empty history")
        check(history.display(at: now.addingTimeInterval(58 * 3600)) == "—", "stale history")
        let directData = data(["five_hour": ["utilization": 28, "resets_at": ISO8601DateFormatter().string(from: reset)], "seven_day": ["utilization": 52, "resets_at": ISO8601DateFormatter().string(from: reset)]])
        let direct = UsageParser.direct(status: 200, data: directData, account: account, now: now)
        check(direct.1 == .success && direct.0?.display(at: now) == "주 48%", "direct success")
        check(direct.0?.source == .direct && direct.0?.account == account, "direct provenance")
        for (code, status) in [(401, DirectUsageStatus.unauthorized), (403, .unauthorized), (429, .limited), (500, .connection), (302, .connection), (0, .connection)] {
            let result = UsageParser.direct(status: code, data: directData, account: account, now: now)
            check(result.0 == nil && result.1 == status, "HTTP mapping")
        }
        for invalid: Any in [true, -1, 101, "NaN"] {
            check(UsageParser.direct(status: 200, data: data(["five_hour": ["utilization": invalid, "resets_at": ISO8601DateFormatter().string(from: reset)]]), account: account, now: now).1 == .connection, "invalid direct percent")
        }
        check(UsageParser.direct(status: 200, data: Data("broken".utf8), account: account, now: now).1 == .connection, "malformed JSON")
        let credentials = UsageParser.credentials(data(["claudeAiOauth": ["accessToken": "synthetic-token", "expiresAt": reset.timeIntervalSince1970 * 1000]]))!
        check(credentials.token == "synthetic-token" && credentials.expiresAt == reset, "credentials extraction")
        check(credentials.status(at: now) == nil && credentials.status(at: reset) == .expired, "expiry")
        check(UsageParser.credentials(data(["claudeAiOauth": ["accessToken": "synthetic-token"]]))?.expiresAt == nil, "optional expiry")
        check(UsageParser.credentials(data(["claudeAiOauth": ["expiresAt": 42]])) == nil, "missing token")
        check(UsageParser.credentials(Data("broken".utf8)) == nil, "invalid credentials")
        var retry = UsageRetry()
        for expected in [300.0, 600, 1200, 1200] { check(retry.interval(after: .limited) == expected, "429 backoff") }
        check(retry.interval(after: .success) == 120 && retry.rateLimits == 0, "success resets backoff")
        check(retry.interval(after: .connection) == 120, "temporary error interval")
        _ = retry.interval(after: .denied); check(retry.denied, "denied blocks")
        retry = UsageRetry(); check(!retry.denied, "toggle/restart rearm")
        check(UsageState.retaining(parsed, success: nil)?.display(at: now) == "72%", "keep last success")
        check(UsageState.retaining(parsed, success: nil)?.display(at: now.addingTimeInterval(3601)) == "—", "last success still expires")
        var state = UsageState(); state.codex = parsed; state.history = history; state.direct = direct.0
        check(state.claude(at: now)?.source == .history, "direct disabled")
        state.directEnabled = true; check(state.claude(at: now)?.source == .direct, "direct preferred")
        state.direct?.observedAt = now.addingTimeInterval(-3601)
        check(state.claude(at: now)?.source == .history, "expired direct falls back")
        check(state.summary(at: now) == "Codex 72% · Claude Code 주 48%", "summary")
        check(state.subtitle(at: now, elapsed: 0, fits: { _ in false }) == "Codex 72%·Claude Code 주 48%", "compact separator")
        for elapsed in [0.0, 4.9, 11, 15.9] { check(state.subtitle(at: now, elapsed: elapsed) == state.summary(at: now), "five second usage phase") }
        for elapsed in [5.0, 7.9] { check(state.subtitle(at: now, elapsed: elapsed) == "Codex ji***@example.com", "codex phase") }
        for elapsed in [8.0, 10.9] { check(state.subtitle(at: now, elapsed: elapsed) == "Claude Code ji***@example.com", "claude phase") }
        let diagnostic = String(data: data(state.diagnostic(at: now)), encoding: .utf8)!
        check(!diagnostic.contains("jino@example.com") && !diagnostic.contains("synthetic-token"), "diagnostic privacy")
        check(state.diagnostic(at: now)["directQuery"] as? String == "실행 안 함", "snapshot never directly queries")
        check(state.reason(for: history, fallback: "", at: now.addingTimeInterval(1801)).contains("기록이 오래됨"), "stale reason")
        print("UsageTests: \(count) checks passed")
    }
}
