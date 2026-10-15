import Foundation
import CryptoKit

struct AppReleaseVersion: Equatable, Comparable, CustomStringConvertible {
    let major: Int, minor: Int, patch: Int
    init?(_ text: String) {
        guard text.range(of: #"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"#, options: .regularExpression) != nil else { return nil }
        let parts = text.split(separator: ".").compactMap { Int($0) }
        guard parts.count == 3 else { return nil }
        major = parts[0]; minor = parts[1]; patch = parts[2]
    }
    static func < (a: Self, b: Self) -> Bool { (a.major, a.minor, a.patch) < (b.major, b.minor, b.patch) }
    var description: String { "\(major).\(minor).\(patch)" }
}

enum AppUpdateError: LocalizedError {
    case invalidRelease, connection, invalidDownload, invalidArchive, invalidApp, cannotReplace, parentRunning
    var errorDescription: String? {
        switch self {
        case .invalidRelease, .connection: return "새 버전을 확인할 수 없습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요."
        case .invalidDownload: return "업데이트 파일 검증에 실패했습니다. 기존 앱은 변경하지 않았습니다."
        case .invalidArchive, .invalidApp: return "올바른 AIpet 업데이트 파일이 아닙니다. 기존 앱은 변경하지 않았습니다."
        case .cannotReplace: return "현재 위치의 앱을 교체할 수 없습니다. 쓰기 가능한 응용 프로그램 폴더에 AIpet을 설치한 뒤 다시 시도해 주세요."
        case .parentRunning: return "기존 AIpet이 종료되지 않아 업데이트를 중단했습니다."
        }
    }
}

struct MacAppUpdate {
    static let repository = "jinoisfree/AIpet"
    static let releasesURL = URL(string: "https://api.github.com/repos/\(repository)/releases?per_page=100")!
    static let maxDownloadBytes = 256 * 1024 * 1024
    let version: AppReleaseVersion, tag: String, downloadURL: URL, size: Int, sha256: String
    var archiveName: String { "AIpet-\(version)-arm64.zip" }
    var valid: Bool {
        tag == "v\(version)" && size > 0 && size <= Self.maxDownloadBytes
            && sha256.range(of: #"\A[0-9a-fA-F]{64}\z"#, options: .regularExpression) != nil
            && downloadURL.absoluteString == "https://github.com/\(Self.repository)/releases/download/\(tag)/\(archiveName)"
    }
    static func latest(_ data: Data, current: AppReleaseVersion) throws -> Self? {
        guard let rows = try JSONSerialization.jsonObject(with: data) as? [Any] else { throw AppUpdateError.invalidRelease }
        var best: Self?
        for item in rows {
            guard let row = item as? [String: Any], row["draft"] as? Bool == false, row["prerelease"] as? Bool == false,
                  let published = row["published_at"] as? String, ISO8601DateFormatter().date(from: published) != nil,
                  let tag = row["tag_name"] as? String, tag.hasPrefix("v"), let version = AppReleaseVersion(String(tag.dropFirst())),
                  version > current, best == nil || version > best!.version,
                  let assets = row["assets"] as? [[String: Any]] else { continue }
            let matching = assets.filter { $0["name"] as? String == "AIpet-\(version)-arm64.zip" }
            guard matching.count == 1, let asset = matching.first, asset["state"] as? String == "uploaded",
                  let bytes = asset["size"] as? NSNumber, CFGetTypeID(bytes) != CFBooleanGetTypeID(),
                  bytes.doubleValue > 0, bytes.doubleValue <= Double(maxDownloadBytes), bytes.doubleValue.rounded() == bytes.doubleValue,
                  let digest = asset["digest"] as? String, digest.hasPrefix("sha256:"),
                  let address = asset["browser_download_url"] as? String, let url = URL(string: address) else { continue }
            let candidate = Self(version: version, tag: tag, downloadURL: url, size: bytes.intValue, sha256: String(digest.dropFirst(7)))
            if candidate.valid { best = candidate }
        }
        return best
    }
}

struct AppUpdatePreferences: Codable {
    var automatic = true
    var lastCheck: Date?
    var deferredVersion: String?
    var deferredUntil: Date?
    func checkDue(at now: Date) -> Bool {
        automatic && (lastCheck == nil || now < lastCheck! || now.timeIntervalSince(lastCheck!) >= 6 * 3600)
    }
    func shouldPrompt(_ version: AppReleaseVersion, at now: Date) -> Bool {
        deferredVersion != version.description || deferredUntil == nil || now >= deferredUntil!
    }
    mutating func deferVersion(_ version: AppReleaseVersion, at now: Date) {
        deferredVersion = version.description; deferredUntil = now.addingTimeInterval(24 * 3600)
    }
}

final class UpdateRedirectPolicy: NSObject, URLSessionTaskDelegate {
    static func allowed(_ url: URL) -> Bool {
        url.scheme == "https" && (url.port == nil || url.port == 443) && url.user == nil && url.password == nil
            && ["github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com"].contains(url.host ?? "")
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        guard task.originalRequest?.url != MacAppUpdate.releasesURL, let url = request.url, Self.allowed(url) else {
            completionHandler(nil); return
        }
        completionHandler(request)
    }
}

final class GitHubAppUpdateClient {
    private let session: URLSession
    init(configuration: URLSessionConfiguration = .ephemeral) {
        configuration.httpShouldSetCookies = false
        configuration.urlCache = nil
        configuration.timeoutIntervalForRequest = 30
        configuration.timeoutIntervalForResource = 600
        session = URLSession(configuration: configuration, delegate: UpdateRedirectPolicy(), delegateQueue: nil)
    }
    deinit { session.invalidateAndCancel() }
    private func request(_ url: URL) -> URLRequest {
        var result = URLRequest(url: url)
        result.setValue("AIpet-Updater/1.0", forHTTPHeaderField: "User-Agent")
        if url.host == "api.github.com" {
            result.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            result.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        }
        return result
    }
    func check(current: AppReleaseVersion) async throws -> MacAppUpdate? {
        var request = request(MacAppUpdate.releasesURL); request.timeoutInterval = 20
        let (bytes, response) = try await session.bytes(for: request)
        let limit = 8 * 1024 * 1024
        guard (response as? HTTPURLResponse)?.statusCode == 200, response.expectedContentLength <= limit else { throw AppUpdateError.connection }
        var data = Data()
        for try await byte in bytes {
            guard data.count < limit else { throw AppUpdateError.invalidRelease }
            data.append(byte)
        }
        try Task.checkCancellation()
        return try MacAppUpdate.latest(data, current: current)
    }
    func download(_ update: MacAppUpdate, directory: URL, progress: @escaping (Int) -> Void) async throws -> URL {
        guard update.valid else { throw AppUpdateError.invalidRelease }
        let fileManager = FileManager.default
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        let archive = directory.appendingPathComponent("AIpet.zip")
        let partial = directory.appendingPathComponent("AIpet.zip.part")
        guard fileManager.createFile(atPath: partial.path, contents: nil, attributes: [.posixPermissions: 0o600]) else { throw AppUpdateError.invalidDownload }
        do {
            let file = try FileHandle(forWritingTo: partial)
            defer { try? file.close() }
            let (bytes, response) = try await session.bytes(for: request(update.downloadURL))
            guard (response as? HTTPURLResponse)?.statusCode == 200,
                  response.expectedContentLength == -1 || response.expectedContentLength == update.size else { throw AppUpdateError.invalidDownload }
            var hash = SHA256(), buffer = Data(), total = 0, reported = -1
            buffer.reserveCapacity(65536)
            for try await byte in bytes {
                total += 1
                guard total <= update.size else { throw AppUpdateError.invalidDownload }
                buffer.append(byte)
                if buffer.count == 65536 {
                    hash.update(data: buffer); try file.write(contentsOf: buffer); buffer.removeAll(keepingCapacity: true)
                    let percent = total * 100 / update.size
                    if percent != reported { reported = percent; progress(percent) }
                }
            }
            hash.update(data: buffer); try file.write(contentsOf: buffer)
            let digest = hash.finalize().map { String(format: "%02x", $0) }.joined()
            guard total == update.size && digest == update.sha256.lowercased() else { throw AppUpdateError.invalidDownload }
            try Task.checkCancellation()
            try file.close()
            try fileManager.moveItem(at: partial, to: archive)
            progress(100)
            return archive
        } catch {
            try? fileManager.removeItem(at: partial)
            throw error
        }
    }
}
