import Foundation
import CryptoKit

// All network requests are intercepted; these tests never fetch or launch an app.
final class UpdateTestProtocol: URLProtocol {
    static var reply: (URLRequest) throws -> (Int, [String: String], Data) = { _ in throw URLError(.notConnectedToInternet) }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            let (status, headers, data) = try Self.reply(request)
            client?.urlProtocol(self, didReceive: HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data)
            client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() { }
}

@main struct AppUpdateTests {
    static var count = 0
    static let manager = FileManager.default
    static let current = AppReleaseVersion("1.0.11")!, next = AppReleaseVersion("1.0.12")!
    static let payload = Data("verified update fixture".utf8)
    static func digest(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
    static func check(_ condition: Bool, _ message: String = "") {
        count += 1; precondition(condition, "update check \(count): \(message)")
    }
    static func fails(_ message: String, _ action: () throws -> Void) {
        do { try action(); check(false, message) } catch { check(true) }
    }
    static func temporary() throws -> URL {
        let root = manager.temporaryDirectory.resolvingSymlinksInPath().appendingPathComponent("aipet-update-tests-\(UUID().uuidString)")
        try manager.createDirectory(at: root, withIntermediateDirectories: true)
        return root
    }
    static func release(_ version: String = "1.0.12") -> [String: Any] {
        ["tag_name": "v\(version)", "draft": false, "prerelease": false, "published_at": "2026-10-14T00:00:00Z",
         "assets": [["name": "AIpet-\(version)-arm64.zip", "state": "uploaded", "size": payload.count,
                     "digest": "sha256:" + digest(payload), "browser_download_url": "https://github.com/jinoisfree/AIpet/releases/download/v\(version)/AIpet-\(version)-arm64.zip"]]]
    }
    static func latest(_ rows: [[String: Any]]) throws -> MacAppUpdate? {
        try MacAppUpdate.latest(JSONSerialization.data(withJSONObject: rows), current: current)
    }
    static func versionsAndSelection() throws {
        check(AppReleaseVersion("1.0.10")! < current)
        check(AppReleaseVersion("1.9.99")! < AppReleaseVersion("1.10.0")!)
        check(AppReleaseVersion("2.0.0")! > next)
        for text in ["", "v1.0.12", "1.0", "1.0.12-preview.1", "01.0.12", "-1.0.12", "1.0.12\n", "1.0.12+build", "9999999999999999999999999.0.0"] {
            check(AppReleaseVersion(text) == nil, text)
        }
        var windows = release("99.0.0"); windows["tag_name"] = "windows-v99.0.0"
        var draft = release("98.0.0"); draft["draft"] = true
        var preview = release("97.0.0"); preview["prerelease"] = true
        check(try latest([windows, draft, preview, release("1.0.11"), release("1.0.2"), release(), release("1.0.13")])?.version.description == "1.0.13")
        check(try latest([release("1.0.11"), release("1.0.10")]) == nil)
        for (key, value): (String, Any) in [("name", "Other.zip"), ("state", "starter"), ("size", true), ("size", "22"), ("size", 0), ("size", -1), ("size", 2.5), ("size", MacAppUpdate.maxDownloadBytes + 1), ("digest", NSNull()), ("digest", "sha256:broken"), ("browser_download_url", "https://evil.example/AIpet.zip"), ("browser_download_url", "http://github.com/jinoisfree/AIpet/releases/download/v1.0.12/AIpet-1.0.12-arm64.zip")] {
            var row = release(); var assets = row["assets"] as! [[String: Any]]; assets[0][key] = value; row["assets"] = assets
            check(try latest([row]) == nil, key)
        }
        for key in ["draft", "prerelease", "published_at", "assets"] {
            var row = release(); row.removeValue(forKey: key); check(try latest([row]) == nil, "missing \(key)")
        }
        var row = release(); row["published_at"] = "broken"; check(try latest([row]) == nil)
        row = release(); row["assets"] = (row["assets"] as! [[String: Any]]) + (row["assets"] as! [[String: Any]])
        check(try latest([row]) == nil, "duplicate assets")
        fails("invalid API response") { _ = try MacAppUpdate.latest(Data("{}".utf8), current: current) }
        for address in ["https://github.com/x", "https://release-assets.githubusercontent.com/x?token=fixture", "https://objects.githubusercontent.com/x"] {
            check(UpdateRedirectPolicy.allowed(URL(string: address)!))
        }
        for address in ["http://github.com/x", "https://github.com.evil.example/x", "https://user@github.com/x", "https://github.com:8443/x", "file:///tmp/update", "https://api.github.com/x"] {
            check(!UpdateRedirectPolicy.allowed(URL(string: address)!))
        }
        let now = Date(timeIntervalSince1970: 100000)
        var preferences = AppUpdatePreferences()
        check(preferences.checkDue(at: now)); preferences.lastCheck = now
        check(!preferences.checkDue(at: now.addingTimeInterval(21599)))
        check(preferences.checkDue(at: now.addingTimeInterval(21600)))
        check(preferences.checkDue(at: now.addingTimeInterval(-1)))
        preferences.deferVersion(next, at: now)
        check(!preferences.shouldPrompt(next, at: now.addingTimeInterval(86399)))
        check(preferences.shouldPrompt(next, at: now.addingTimeInterval(86400)))
        check(preferences.shouldPrompt(AppReleaseVersion("1.0.13")!, at: now))
        preferences.automatic = false
        check(!preferences.checkDue(at: now.addingTimeInterval(90000)))
        let restored = try JSONDecoder().decode(AppUpdatePreferences.self, from: JSONEncoder().encode(preferences))
        check(!restored.automatic && restored.deferredVersion == next.description && restored.lastCheck == now)
    }
    static func networkAndDownloads() async throws {
        let configuration = URLSessionConfiguration.ephemeral; configuration.protocolClasses = [UpdateTestProtocol.self]
        let client = GitHubAppUpdateClient(configuration: configuration)
        let body = try JSONSerialization.data(withJSONObject: [release()])
        UpdateTestProtocol.reply = { request in
            check(request.url == MacAppUpdate.releasesURL, "only release metadata before approval")
            check(request.value(forHTTPHeaderField: "Authorization") == nil)
            check(request.value(forHTTPHeaderField: "X-GitHub-Api-Version") == "2022-11-28")
            return (200, ["Content-Length": String(body.count)], body)
        }
        let update = try await client.check(current: current)!
        check(update.version == next)
        let root = try temporary(); defer { try? manager.removeItem(at: root) }
        let old = root.appendingPathComponent("existing.app"); try payload.write(to: old)
        UpdateTestProtocol.reply = { request in
            check(request.url == update.downloadURL)
            return (200, ["Content-Length": String(payload.count)], payload)
        }
        var finalProgress = 0
        let file = try await client.download(update, directory: root.appendingPathComponent("good")) { finalProgress = $0 }
        check(try Data(contentsOf: file) == payload && finalProgress == 100)
        check(!manager.fileExists(atPath: file.path + ".part"))
        for (index, response) in [(200, [:], Data(repeating: 0, count: payload.count)), (200, [:], payload.dropLast()), (200, [:], payload + Data([1])), (200, ["Content-Length": "999"], payload), (404, [:], payload)].enumerated() {
            UpdateTestProtocol.reply = { _ in response }
            let directory = root.appendingPathComponent("bad-\(index)")
            do { _ = try await client.download(update, directory: directory) { _ in }; check(false, "invalid download \(index)") } catch { check(true) }
            check(try manager.contentsOfDirectory(atPath: directory.path).isEmpty, "no partial archive after failure")
        }
        UpdateTestProtocol.reply = { _ in (200, ["Content-Length": String(payload.count)], payload) }
        let cancelled = root.appendingPathComponent("cancelled")
        let task = Task {
            withUnsafeCurrentTask { $0?.cancel() }
            return try await client.download(update, directory: cancelled) { _ in }
        }
        do { _ = try await task.value; check(false, "cancelled download") } catch { check(true) }
        check(try manager.contentsOfDirectory(atPath: cancelled.path).isEmpty)
        check(try Data(contentsOf: old) == payload, "failures leave existing app alone")
        for response in [(403, [:], Data()), (200, ["Content-Length": "8388609"], Data()), (200, [:], Data("not json".utf8))] {
            UpdateTestProtocol.reply = { _ in response }
            do { _ = try await client.check(current: current); check(false, "bad metadata") } catch { check(true) }
        }
    }

    struct Entry {
        var name: String, localName: String? = nil, mode = 0o100644, expanded = 1, flags = 0, localFlags = 0
    }
    static func zip(_ entries: [Entry]) -> Data {
        func le(_ value: Int, _ size: Int) -> Data { Data((0..<size).map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) }) }
        var data = Data(), directory = Data()
        for entry in entries {
            let name = Data(entry.name.utf8), localName = Data((entry.localName ?? entry.name).utf8), offset = data.count
            data += le(0x04034b50, 4) + le(20, 2) + le(entry.localFlags, 2) + le(0, 2) + le(0, 4) + le(0, 4)
            data += le(1, 4) + le(entry.expanded, 4) + le(localName.count, 2) + le(0, 2) + localName + Data([0])
            directory += le(0x02014b50, 4) + le(0x314, 2) + le(20, 2) + le(entry.flags, 2) + le(0, 2) + le(0, 4) + le(0, 4)
            directory += le(1, 4) + le(entry.expanded, 4) + le(name.count, 2) + le(0, 2) + le(0, 2) + le(0, 2) + le(0, 2) + le(entry.mode << 16, 4) + le(offset, 4) + name
        }
        let offset = data.count; data += directory
        data += le(0x06054b50, 4) + le(0, 2) + le(0, 2) + le(entries.count, 2) + le(entries.count, 2) + le(directory.count, 4) + le(offset, 4) + le(0, 2)
        return data
    }
    static func archiveValidation() throws {
        let entries = [Entry(name: "AIpet.app/Contents/Info.plist"), Entry(name: "AIpet.app/Contents/MacOS/Taesik")]
        try AppUpdateArchive.validate(zip(entries)); check(true)
        for entry in [Entry(name: "../outside"), Entry(name: "AIpet.app/../outside"), Entry(name: "AIpet.app/./outside"), Entry(name: "AIpet.app//outside"), Entry(name: "AIpet.app/link", mode: 0o120777), Entry(name: "AIpet.app/large", expanded: 512 * 1024 * 1024), Entry(name: "AIpet.app/name", localName: "AIpet.app/evil"), Entry(name: "AIpet.app/encrypted", flags: 1), Entry(name: "AIpet.app/local-encrypted", localFlags: 1), entries[0]] {
            fails("unsafe archive: \(entry.name)") { try AppUpdateArchive.validate(zip(entries + [entry])) }
        }
        fails("truncated") { try AppUpdateArchive.validate(zip(entries).dropLast()) }
        fails("missing executable") { try AppUpdateArchive.validate(zip([entries[0]])) }
        fails("not zip") { try AppUpdateArchive.validate(payload) }
    }
    static func fakeApp(_ location: URL, version: String) throws {
        try manager.createDirectory(at: location.appendingPathComponent("Contents/MacOS"), withIntermediateDirectories: true)
        let plist = ["CFBundleIdentifier": "com.jinoisfree.taesik", "CFBundleExecutable": "Taesik", "CFBundleShortVersionString": version]
        try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0).write(to: location.appendingPathComponent("Contents/Info.plist"))
        let executable = location.appendingPathComponent("Contents/MacOS/Taesik")
        try Data(version.utf8).write(to: executable)
        try manager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: executable.path)
    }
    static func replacement() throws {
        for scenario in ["success", "bad staged app", "move failure", "bad installed app", "launch failure", "rollback failure"] {
            let root = try temporary(); defer { try? manager.removeItem(at: root) }
            let destination = root.appendingPathComponent("AIpet.app"), work = root.appendingPathComponent(".aipet-update-test")
            let staged = work.appendingPathComponent("AIpet.app"), backup = work.appendingPathComponent("previous.app")
            let settings = root.appendingPathComponent("settings.json"); try payload.write(to: settings)
            try fakeApp(destination, version: current.description); try fakeApp(staged, version: next.description)
            var launches = 0, moves = 0
            do {
                try AppUpdateInstaller.replace(staged: staged, destination: destination, version: next, verify: { app, expected in
                    if scenario == "bad staged app" || scenario == "bad installed app" && app == destination { throw AppUpdateError.invalidApp }
                    check(try AppUpdateInstaller.version(of: app) == expected)
                }, move: { from, to in
                    moves += 1
                    if scenario == "move failure" && moves == 2 || scenario == "rollback failure" && moves >= 2 { throw AppUpdateError.cannotReplace }
                    try manager.moveItem(at: from, to: to)
                }, launch: { app in
                    launches += 1
                    if scenario == "launch failure" && launches == 1 { throw AppUpdateError.invalidApp }
                    check(app == destination)
                })
                check(scenario == "success")
            } catch { check(scenario != "success") }
            if scenario == "success" {
                check(try AppUpdateInstaller.version(of: destination) == next)
                check(!manager.fileExists(atPath: backup.path) && launches == 1, "old version removed after success")
            } else if scenario == "rollback failure" {
                check(try AppUpdateInstaller.version(of: backup) == current, "keep backup when restoration fails")
                check(!manager.fileExists(atPath: destination.path))
            } else {
                check(try AppUpdateInstaller.version(of: destination) == current, "old version preserved or restored")
                check(!manager.fileExists(atPath: backup.path))
            }
            check(try Data(contentsOf: settings) == payload, "settings preserved in \(scenario)")
        }
        let root = try temporary(); defer { try? manager.removeItem(at: root) }
        let destination = root.appendingPathComponent("AIpet.app")
        try fakeApp(destination, version: next.description)
        fails("downgrade or reinstall") { try AppUpdateInstaller.replace(staged: root.appendingPathComponent("missing"), destination: destination, version: current) }
        let link = root.appendingPathComponent("linked.app")
        try manager.createSymbolicLink(at: link, withDestinationURL: destination)
        fails("symlink destination") { _ = try AppUpdateInstaller.version(of: link) }
    }
    static func signedArchive(_ archive: URL) throws {
        let root = try temporary(); defer { try? manager.removeItem(at: root) }
        let destination = root.appendingPathComponent("AIpet.app")
        try fakeApp(destination, version: current.description)
        let prepared = try AppUpdateInstaller.prepare(archive: archive, destination: destination, version: next)
        check(try AppUpdateInstaller.version(of: destination) == current, "preparation leaves old app intact")
        var launches = 0
        try AppUpdateInstaller.replace(staged: prepared.work.appendingPathComponent("AIpet.app"), destination: destination, version: next, launch: { _ in launches += 1 })
        try AppUpdateInstaller.verify(destination, version: next)
        check(launches == 1 && !manager.fileExists(atPath: prepared.work.appendingPathComponent("previous.app").path))
        fails("installed executable must be signed") {
            try Data("broken".utf8).write(to: destination.appendingPathComponent("Contents/MacOS/Taesik"))
            try AppUpdateInstaller.verify(destination, version: next)
        }
    }
    static func main() async throws {
        try versionsAndSelection()
        try await networkAndDownloads()
        try archiveValidation()
        try replacement()
        if CommandLine.arguments.count > 1 { try signedArchive(URL(fileURLWithPath: CommandLine.arguments[1])) }
        print("AppUpdateTests: \(count) checks passed")
    }
}
