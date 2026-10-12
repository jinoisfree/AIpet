import Foundation

enum UsageSource: String, Codable {
    case codex = "Codex 앱 서버", codexRecord = "Codex 작업 기록", history = "Claude 앱 기록", direct = "직접 조회"
    var lifetime: TimeInterval { self == .codexRecord ? 86400 : self == .history ? 1800 : 3600 }
}
enum UsagePeriod: String, Codable { case fiveHour = "5시간", weekly = "주간" }
struct UsageWindow: Codable, Equatable {
    var period: UsagePeriod
    var used: Double
    var resetsAt: Date?
    func remaining(at now: Date) -> Int? {
        guard used.isFinite, (0...100).contains(used), resetsAt.map({ $0 > now }) ?? true else { return nil }
        return Int((100 - used).rounded(.down))
    }
}
struct UsageAccount: Equatable {
    var email: String?
    var detail: String?
    var verified = false
    var organizationID: String?
    var label: String { verified ? email ?? "계정 확인 불가" : "계정 확인 불가" }
    var masked: String { verified ? email.map(Self.mask) ?? "계정 확인 불가" : "계정 확인 불가" }
    static func mask(_ email: String) -> String {
        guard let at = email.firstIndex(of: "@"), at != email.startIndex, email.index(after: at) != email.endIndex else { return "***" }
        let local = email[..<at]
        return String(local.prefix(local.count <= 2 ? 1 : 2)) + "***" + email[at...]
    }
}
struct ServiceUsage {
    var source: UsageSource
    var observedAt: Date
    var windows: [UsageWindow]
    var account: UsageAccount
    func valid(at now: Date) -> Bool { now >= observedAt.addingTimeInterval(-60) && now.timeIntervalSince(observedAt) < source.lifetime }
    func display(at now: Date) -> String {
        guard valid(at: now), let least = windows.compactMap({ window -> (UsagePeriod, Int)? in
            window.remaining(at: now).map { (window.period, $0) }
        }).min(by: { $0.1 < $1.1 }) else { return "—" }
        return (least.0 == .weekly ? "주 " : "") + "\(least.1)%"
    }
    func windowLabel(_ period: UsagePeriod, at now: Date) -> String {
        guard valid(at: now), let value = windows.first(where: { $0.period == period })?.remaining(at: now) else { return "—" }
        return "\(value)%"
    }
}
enum DirectUsageStatus: String {
    case disabled = "실행 안 함", success = "성공", missing = "로그인 정보 없음", expired = "로그인 만료"
    case unauthorized = "인증 거부", limited = "요청 제한", connection = "연결 실패", denied = "키체인 접근 거부"
}
struct UsageRetry {
    var rateLimits = 0
    var denied = false
    mutating func interval(after status: DirectUsageStatus) -> TimeInterval {
        if status == .denied { denied = true }
        if status == .limited { rateLimits = min(3, rateLimits + 1); return [300.0, 600, 1200][rateLimits - 1] }
        rateLimits = 0; return 120
    }
}
struct UsageCredentials {
    var token: String
    var expiresAt: Date?
    func status(at now: Date) -> DirectUsageStatus? { expiresAt.map { $0 <= now } == true ? .expired : nil }
}
struct UsageState {
    var codex: ServiceUsage?
    var codexRecord: ServiceUsage?
    var codexExecutable = "none"
    var codexUsage: ServiceUsage? { Self.newest(codex, codexRecord) }
    static func newest(_ first: ServiceUsage?, _ second: ServiceUsage?) -> ServiceUsage? {
        guard let first else { return second }; guard let second else { return first }
        return second.observedAt > first.observedAt ? second : first
    }
    func codexFailure(at now: Date) -> String {
        if let usage = codexUsage, usage.display(at: now) != "—" { return "" }
        if let usage = codexUsage, usage.source == .codexRecord {
            return usage.valid(at: now) ? "사용량 창 없음 또는 초기화 시각 경과" : "최근 24시간 Codex 작업 기록 없음"
        }
        if codexReason == "Codex 를 찾을 수 없음" || codexReason == "Codex 앱 실행 불가" {
            return codexReason + " · Codex 로 작업하면 기록에서 표시됩니다"
        }
        return "최근 24시간 Codex 작업 기록 없음 · " + reason(for: codex, fallback: codexReason, at: now)
    }
    func claudeFailure(at now: Date) -> String {
        if claude(at: now)?.display(at: now) != nil && claude(at: now)?.display(at: now) != "—" { return "" }
        if directEnabled { return directStatus == .success ? "사용량 창 없음 또는 초기화 시각 경과" : directStatus.rawValue }
        return "옵션에서 Claude 사용량 직접 조회를 켜세요"
    }
    var history: ServiceUsage?
    var direct: ServiceUsage?
    var codexReason = "조회 대기"
    var historyReason = "기록 없음"
    var directStatus: DirectUsageStatus = .disabled
    var directSuccess: Date?
    var directEnabled = false
    func claude(at now: Date) -> ServiceUsage? {
        if directEnabled, let direct, direct.display(at: now) != "—" { return direct }
        return history
    }
    static func retaining(_ previous: ServiceUsage?, success: ServiceUsage?) -> ServiceUsage? { success ?? previous }
    func summary(at now: Date, compact: Bool = false) -> String {
        let separator = compact ? "·" : " · "
        return "Codex \(codexUsage?.display(at: now) ?? "—")\(separator)Claude Code \(claude(at: now)?.display(at: now) ?? "—")"
    }
    func subtitle(at now: Date, elapsed: TimeInterval, fits: (String) -> Bool = { $0.count <= 32 }) -> String {
        let phase = Int(max(0, elapsed).truncatingRemainder(dividingBy: 11))
        if phase < 5 { let text = summary(at: now); return fits(text) ? text : summary(at: now, compact: true) }
        if phase < 8 { return "Codex \(codexUsage?.account.masked ?? "계정 확인 불가")" }
        return "Claude Code \(claude(at: now)?.account.masked ?? "계정 확인 불가")"
    }
    func reason(for usage: ServiceUsage?, fallback: String, at now: Date) -> String {
        guard let usage else { return fallback }
        if !usage.valid(at: now) { return usage.source == .history ? "기록이 오래됨 · 옵션에서 직접 조회를 켤 수 있음" : "마지막 조회 값의 유효 기간이 지남" }
        return usage.display(at: now) == "—" ? "사용량 창 없음 또는 초기화 시각 경과" : ""
    }
    func diagnostic(at now: Date) -> [String: Any] {
        func service(_ usage: ServiceUsage?, reason: String) -> [String: Any] {
            var result: [String: Any] = ["display": usage?.display(at: now) ?? "—", "account": usage?.account.masked ?? "계정 확인 불가", "reason": reason]
            if let usage {
                result["source"] = usage.source.rawValue
                result["observedAt"] = ISO8601DateFormatter().string(from: usage.observedAt)
                result["windows"] = usage.windows.map { window -> [String: Any] in
                    var row: [String: Any] = ["period": window.period.rawValue, "usedPercent": window.used]
                    if let date = window.resetsAt { row["resetsAt"] = ISO8601DateFormatter().string(from: date) }
                    return row
                }
            }
            return result
        }
        let claude = claude(at: now)
        return ["codex": service(codexUsage, reason: codexFailure(at: now)),
                "codexExecutable": codexExecutable,
                "codexRecord": service(codexRecord, reason: reason(for: codexRecord, fallback: "최근 24시간 Codex 작업 기록 없음", at: now)),
                "claude": service(claude, reason: claudeFailure(at: now)), "directQuery": "실행 안 함"]
    }
    static func example(at now: Date) -> UsageState {
        var state = UsageState()
        state.codex = ServiceUsage(source: .codex, observedAt: now, windows: [UsageWindow(period: .fiveHour, used: 28, resetsAt: now.addingTimeInterval(7200))], account: UsageAccount(email: "codex@example.com", detail: "예시 요금제", verified: true))
        state.history = ServiceUsage(source: .history, observedAt: now, windows: [UsageWindow(period: .fiveHour, used: 20, resetsAt: nil), UsageWindow(period: .weekly, used: 52, resetsAt: nil)], account: UsageAccount(email: "claude@example.com", detail: "예시 조직", verified: true))
        return state
    }
}

enum UsageParser {
    static func number(_ value: Any?) -> Double? {
        guard let value = value as? NSNumber, CFGetTypeID(value) != CFBooleanGetTypeID(), value.doubleValue.isFinite else { return nil }
        return value.doubleValue
    }
    static func percent(_ value: Any?) -> Double? { guard let value = number(value), (0...100).contains(value) else { return nil }; return value }
    static func object(_ data: Data) -> [String: Any]? { (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] }
    static func isoDate(_ value: Any?) -> Date? {
        guard let text = value as? String else { return nil }
        let format = ISO8601DateFormatter()
        if let date = format.date(from: text) { return date }
        format.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; return format.date(from: text)
    }
    static func codex(account response: [String: Any], limits: [String: Any], now: Date) -> ServiceUsage? {
        let account = (response["result"] as? [String: Any])?["account"] as? [String: Any]
        let result = limits["result"] as? [String: Any] ?? [:]
        guard let rate = ((result["rateLimitsByLimitId"] as? [String: Any])?["codex"] as? [String: Any]) ?? result["rateLimits"] as? [String: Any] else { return nil }
        let windows = ["primary", "secondary"].compactMap { key -> UsageWindow? in
            guard let window = rate[key] as? [String: Any], let used = percent(window["usedPercent"]),
                  let minutes = number(window["windowDurationMins"]), minutes == 300 || minutes == 10080 else { return nil }
            let reset = number(window["resetsAt"]).map(Date.init(timeIntervalSince1970:))
            return UsageWindow(period: minutes == 300 ? .fiveHour : .weekly, used: used, resetsAt: reset)
        }
        guard !windows.isEmpty else { return nil }
        return ServiceUsage(source: .codex, observedAt: now, windows: windows, account: UsageAccount(email: account?["email"] as? String, detail: account?["planType"] as? String, verified: account?["email"] is String))
    }
    static func claudeAccount(_ config: [String: Any]) -> UsageAccount {
        let account = config["oauthAccount"] as? [String: Any] ?? [:]
        return UsageAccount(email: account["emailAddress"] as? String, detail: account["organizationName"] as? String, verified: account["emailAddress"] is String, organizationID: account["organizationUuid"] as? String)
    }
    static func history(_ object: [String: Any], account: UsageAccount) -> ServiceUsage? {
        guard let sample = (object["samples"] as? [[String: Any]])?.last,
              let time = number(sample["t"]), let values = sample["u"] as? [String: Any] else { return nil }
        let windows = [("fh", UsagePeriod.fiveHour), ("sd", .weekly)].compactMap { key, period -> UsageWindow? in
            percent(values[key]).map { UsageWindow(period: period, used: $0, resetsAt: nil) }
        }
        guard !windows.isEmpty else { return nil }
        let matches = account.organizationID != nil && sample["org"] as? String == account.organizationID
        return ServiceUsage(source: .history, observedAt: Date(timeIntervalSince1970: time / 1000), windows: windows, account: matches ? account : UsageAccount())
    }
    static func codexRecord(_ object: [String: Any]) -> ServiceUsage? {
        guard object["type"] as? String == "event_msg", let payload = object["payload"] as? [String: Any],
              payload["type"] as? String == "token_count", let rate = payload["rate_limits"] as? [String: Any],
              let date = isoDate(object["timestamp"]) else { return nil }
        let windows = ["primary", "secondary"].compactMap { key -> UsageWindow? in
            guard let row = rate[key] as? [String: Any], let used = percent(row["used_percent"]),
                  let minutes = number(row["window_minutes"]), minutes == 300 || minutes == 10080 else { return nil }
            return UsageWindow(period: minutes == 300 ? .fiveHour : .weekly, used: used,
                               resetsAt: number(row["resets_at"]).map(Date.init(timeIntervalSince1970:)))
        }
        guard !windows.isEmpty else { return nil }
        return ServiceUsage(source: .codexRecord, observedAt: date, windows: windows,
                            account: UsageAccount(detail: rate["plan_type"] as? String))
    }
    static func credentials(_ data: Data) -> UsageCredentials? {
        guard let auth = object(data)?["claudeAiOauth"] as? [String: Any], let token = auth["accessToken"] as? String, !token.isEmpty else { return nil }
        return UsageCredentials(token: token, expiresAt: number(auth["expiresAt"]).map { Date(timeIntervalSince1970: $0 / 1000) })
    }
    static func direct(status: Int, data: Data, account: UsageAccount, now: Date) -> (ServiceUsage?, DirectUsageStatus) {
        if status == 401 || status == 403 { return (nil, .unauthorized) }
        if status == 429 { return (nil, .limited) }
        guard status == 200, let object = object(data) else { return (nil, .connection) }
        let windows = [("five_hour", UsagePeriod.fiveHour), ("seven_day", .weekly)].compactMap { key, period -> UsageWindow? in
            guard let row = object[key] as? [String: Any], let used = percent(row["utilization"]), let reset = isoDate(row["resets_at"]) else { return nil }
            return UsageWindow(period: period, used: used, resetsAt: reset)
        }
        guard !windows.isEmpty else { return (nil, .connection) }
        return (ServiceUsage(source: .direct, observedAt: now, windows: windows, account: account), .success)
    }
}
